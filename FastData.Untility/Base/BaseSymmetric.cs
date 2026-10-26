using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FastUntility.Base
{
    /// <summary>
    /// 对称加密处理类。
    /// <para>
    /// v2 起 <see cref="Encrypto(string)"/> 使用随机 IV + HMAC-SHA256 完整性校验，
    /// 输出格式为 <c>FD2:</c> + Base64(IV ‖ 密文 ‖ 标签)；
    /// 密钥来自 <see cref="ConfigureKey(string)"/> 或环境变量 <c>FASTDATA_ENCRYPTION_KEY</c>。
    /// </para>
    /// <para>
    /// <see cref="Decrypto(string, string)"/> 仍可解密 v1（固定密钥 + 固定 IV）生成的历史密文。
    /// 但 v1 的密钥硬编码在本库源码中，任何拿到源码的人都能解密这些历史数据，
    /// 应尽快用 v2 重新加密。
    /// </para>
    /// </summary>
    public static class BaseSymmetric
    {
        /// <summary>环境变量名：未调用 <see cref="ConfigureKey(string)"/> 时从这里读取密钥。</summary>
        public const string KeyEnvironmentVariable = "FASTDATA_ENCRYPTION_KEY";

        private const string CipherPrefix = "FD2:";
        private const int IvSize = 16;
        private const int AesKeySize = 32;
        private const int TagSize = 32;

        /// <summary>
        /// v1 遗留 AES 密钥。<b>仅用于解密历史密文</b>，不得再用于加密。
        /// </summary>
        private const string LegacyAesKey = "Guz(%&hj7x89H$yuBI012345maT5&fvHUFCy76*h%(HilJ$lhj!y6&(*jkP~!@#$";

        /// <summary>
        /// v1 遗留固定 IV。固定 IV 使 CBC 失去语义安全性，仅用于解密历史密文。
        /// </summary>
        private const string LegacyAesIV = "E4ghj*Ghg7!rNIfb&95GUY86GfghUb#er57HBh(u%g6HJ($jhWk7&!~!@#$%^&*(";

        /// <summary>
        /// v1 遗留 DES 密钥。<b>仅用于解密历史数据</b>，DES 本身已不安全。
        /// </summary>
        private const string LegacyDesKey = "Weizz_2015";

        private static string _configuredKey;

        /// <summary>
        /// 是否允许在未配置密钥时回退到 v1 的不安全加密（硬编码密钥 + 固定 IV）。
        /// 默认 <c>false</c>。仅供迁移期临时开启：开启后 <see cref="Encrypto(string)"/>
        /// 与 <see cref="EncodeGB2312(string)"/> 会生成旧格式密文，并输出一条 Trace 警告。
        /// <para>解密方向任何时候都不需要这个开关。</para>
        /// </summary>
        public static bool AllowInsecureLegacyEncryption { get; set; }

        /// <summary>
        /// 设置加密密钥（任意长度字符串，内部经 SHA-256 派生出 256 位密钥）。
        /// </summary>
        /// <param name="key">主密钥。建议 32 字节以上高熵随机值，Base64 编码后可存于配置。</param>
        public static void ConfigureKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            _configuredKey = key;
        }

        /// <summary>
        /// 清除已配置的密钥（下次将从环境变量读取）。
        /// </summary>
        public static void ClearKey()
        {
            _configuredKey = null;
        }

        /// <summary>
        /// 生成一个随机主密钥（Base64），可直接传给 <see cref="ConfigureKey(string)"/>。
        /// </summary>
        /// <param name="byteLength">随机字节数，默认 32（256 位）。</param>
        public static string GenerateKey(int byteLength = AesKeySize)
        {
            if (byteLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(byteLength));

            var bytes = new byte[byteLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// AES 加密（v2 格式，随机 IV + HMAC 认证）。
        /// </summary>
        /// <param name="source">待加密的明文</param>
        /// <returns><c>FD2:</c> 前缀 + Base64 密文</returns>
        /// <exception cref="InvalidOperationException">未配置密钥且未开启 <see cref="AllowInsecureLegacyEncryption"/>。</exception>
        public static string Encrypto(string source)
        {
            if (source == null)
                return string.Empty;

            var key = ResolveKey();
            if (key != null)
                return EncryptWithKey(source, key);

            if (AllowInsecureLegacyEncryption)
            {
                Trace.TraceWarning(
                    "BaseSymmetric.Encrypto 正在使用 v1 的不安全加密（硬编码密钥 + 固定 IV）。" +
                    "请调用 BaseSymmetric.ConfigureKey(...) 或设置环境变量 " + KeyEnvironmentVariable + "。");
                return EncryptLegacy(source);
            }

            throw new InvalidOperationException(
                "未配置加密密钥。v1 的硬编码密钥已移除，无法安全加密：" +
                "请调用 BaseSymmetric.ConfigureKey(key)，或设置环境变量 " + KeyEnvironmentVariable + "。" +
                "如确需生成旧格式密文（不安全，仅限迁移），可临时设置 " +
                "BaseSymmetric.AllowInsecureLegacyEncryption = true。");
        }

        /// <summary>
        /// AES 加密（v2 格式），使用指定的密钥，不影响静态配置。
        /// </summary>
        public static string EncryptWithKey(string source, string key)
        {
            if (source == null)
                return string.Empty;
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            var master = DeriveMasterKey(key);
            var encryptionKey = DeriveSubKey(master, "enc");
            var macKey = DeriveSubKey(master, "mac");

            var iv = new byte[IvSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }

            byte[] cipher;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey;
                aes.IV = iv;

                using (var encryptor = aes.CreateEncryptor())
                {
                    var plainBytes = Encoding.UTF8.GetBytes(source);
                    cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                }
            }

            var payload = new byte[IvSize + cipher.Length + TagSize];
            Buffer.BlockCopy(iv, 0, payload, 0, IvSize);
            Buffer.BlockCopy(cipher, 0, payload, IvSize, cipher.Length);

            byte[] tag;
            using (var hmac = new HMACSHA256(macKey))
            {
                tag = hmac.ComputeHash(payload, 0, IvSize + cipher.Length);
            }
            Buffer.BlockCopy(tag, 0, payload, IvSize + cipher.Length, TagSize);

            return CipherPrefix + Convert.ToBase64String(payload);
        }

        /// <summary>
        /// AES 解密。自动识别 v2（<c>FD2:</c> 前缀）与 v1 历史密文。
        /// </summary>
        /// <param name="source">密文</param>
        /// <param name="defaultValue">解密失败时的默认返回值</param>
        /// <returns>解密后的明文；失败时返回 <paramref name="defaultValue"/></returns>
        public static string Decrypto(string source, string defaultValue = "")
        {
            string plain;
            string error;
            return TryDecrypto(source, out plain, out error) ? plain : defaultValue;
        }

        /// <summary>
        /// AES 解密（可诊断版本）。失败的<strong>原因</strong>通过 <paramref name="error"/> 返回，
        /// 便于区分「密钥缺失」「完整性校验失败」「格式错误」等场景。
        /// </summary>
        public static bool TryDecrypto(string source, out string plain, out string error)
        {
            plain = null;
            error = null;

            if (string.IsNullOrEmpty(source))
            {
                plain = string.Empty;
                return true;
            }

            if (source.StartsWith(CipherPrefix, StringComparison.Ordinal))
            {
                var key = ResolveKey();
                if (key == null)
                {
                    error = "密文为 v2 格式，但未配置密钥（ConfigureKey 或环境变量 " + KeyEnvironmentVariable + "）。";
                    return false;
                }

                try
                {
                    plain = DecryptWithKey(source, key);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            try
            {
                plain = DecryptLegacy(source);
                return true;
            }
            catch (Exception ex)
            {
                error = "v1 密文解密失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// AES 解密（v2 格式），使用指定的密钥。
        /// </summary>
        /// <exception cref="CryptographicException">完整性校验失败（密钥错误或密文被篡改）。</exception>
        public static string DecryptWithKey(string source, string key)
        {
            if (string.IsNullOrEmpty(source))
                return string.Empty;
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            if (!source.StartsWith(CipherPrefix, StringComparison.Ordinal))
                throw new ArgumentException("不是 v2 密文（缺少 " + CipherPrefix + " 前缀）。", nameof(source));

            var payload = Convert.FromBase64String(source.Substring(CipherPrefix.Length));
            if (payload.Length < IvSize + TagSize + 16)
                throw new CryptographicException("密文长度不足，可能已损坏。");

            var cipherLength = payload.Length - IvSize - TagSize;

            var master = DeriveMasterKey(key);
            var encryptionKey = DeriveSubKey(master, "enc");
            var macKey = DeriveSubKey(master, "mac");

            var iv = new byte[IvSize];
            Buffer.BlockCopy(payload, 0, iv, 0, IvSize);

            var cipher = new byte[cipherLength];
            Buffer.BlockCopy(payload, IvSize, cipher, 0, cipherLength);

            var tag = new byte[TagSize];
            Buffer.BlockCopy(payload, IvSize + cipherLength, tag, 0, TagSize);

            byte[] expectedTag;
            using (var hmac = new HMACSHA256(macKey))
            {
                expectedTag = hmac.ComputeHash(payload, 0, IvSize + cipherLength);
            }

            if (!FixedTimeEquals(expectedTag, tag))
                throw new CryptographicException("密文完整性校验失败：密钥不匹配或数据被篡改。");

            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey;
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                {
                    var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
        }

        #region 密钥派生

        private static string ResolveKey()
        {
            if (!string.IsNullOrEmpty(_configuredKey))
                return _configuredKey;

            var fromEnvironment = Environment.GetEnvironmentVariable(KeyEnvironmentVariable);
            return string.IsNullOrEmpty(fromEnvironment) ? null : fromEnvironment;
        }

        /// <summary>
        /// 把任意长度密钥派生成 256 位主密钥（SHA-256）。
        /// </summary>
        private static byte[] DeriveMasterKey(string key)
        {
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
            }
        }

        /// <summary>
        /// 从主密钥派生用途隔离的子密钥（HMAC-SHA256），避免加解密密钥与 MAC 密钥复用。
        /// </summary>
        private static byte[] DeriveSubKey(byte[] masterKey, string purpose)
        {
            using (var hmac = new HMACSHA256(masterKey))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes("FastData.Aes." + purpose));
            }
        }

        /// <summary>
        /// 常量时间比较，防止通过响应时间侧信道逐字节猜测 MAC。
        /// </summary>
        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            var diff = 0;
            for (var i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }

            return diff == 0;
        }

        #endregion

        #region v1 遗留格式（只读兼容）

        /// <summary>
        /// 解密 v1 密文。v1 使用固定密钥与固定 IV，密钥长度取决于当时
        /// <c>Aes.Create()</c> 的默认 KeySize（.NET 各平台均为 256 位），
        /// 因此先按 32 字节密钥尝试，失败再退回 16 字节，覆盖两种历史环境。
        /// </summary>
        private static string DecryptLegacy(string source)
        {
            var cipherBytes = Convert.FromBase64String(source);

            try
            {
                return DecryptLegacyWith(cipherBytes, AesKeySize);
            }
            catch (CryptographicException)
            {
                return DecryptLegacyWith(cipherBytes, 16);
            }
        }

        private static string DecryptLegacyWith(byte[] cipherBytes, int keyLength)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = BuildLegacyBytes(LegacyAesKey, keyLength);
                aes.IV = BuildLegacyBytes(LegacyAesIV, IvSize);

                using (var decryptor = aes.CreateDecryptor())
                {
                    var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
        }

        /// <summary>
        /// v1 生成旧格式密文（硬编码密钥 + 固定 IV）。仅在
        /// <see cref="AllowInsecureLegacyEncryption"/> 为 true 时使用。
        /// </summary>
        private static string EncryptLegacy(string source)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = BuildLegacyBytes(LegacyAesKey, AesKeySize);
                aes.IV = BuildLegacyBytes(LegacyAesIV, IvSize);

                using (var encryptor = aes.CreateEncryptor())
                {
                    var plainBytes = Encoding.UTF8.GetBytes(source);
                    var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                    return Convert.ToBase64String(cipherBytes);
                }
            }
        }

        /// <summary>
        /// 复刻 v1 的密钥材料构造方式：ASCII 编码后截断/空格补齐到目标长度。
        /// </summary>
        private static byte[] BuildLegacyBytes(string source, int length)
        {
            if (source.Length > length)
                source = source.Substring(0, length);
            else if (source.Length < length)
                source = source.PadRight(length, ' ');

            return Encoding.ASCII.GetBytes(source);
        }

        #endregion

        /// <summary>
        /// DES 加密（已废弃）。DES 密钥硬编码且算法已不安全，仅在
        /// <see cref="AllowInsecureLegacyEncryption"/> 为 true 时可用，用于生成历史兼容数据。
        /// </summary>
        /// <param name="source">待加密的明文（GB2312 编码）</param>
        /// <returns>十六进制编码的密文</returns>
        [Obsolete("DES 算法已不安全，请使用 AES (Encrypto/Decrypto)")]
        public static string EncodeGB2312(string source)
        {
            if (!AllowInsecureLegacyEncryption)
            {
                throw new InvalidOperationException(
                    "EncodeGB2312 使用硬编码 DES 密钥，已禁用。请改用 AES（Encrypto/EncryptWithKey）；" +
                    "如确需生成旧格式数据，可临时设置 BaseSymmetric.AllowInsecureLegacyEncryption = true。");
            }

#pragma warning disable SYSLIB0021
            using (var provider = new DESCryptoServiceProvider())
#pragma warning restore SYSLIB0021
            {
                var keyBytes = Encoding.ASCII.GetBytes(LegacyDesKey.Substring(0, 8));
                provider.Key = keyBytes;
                provider.IV = keyBytes;

                var inputBytes = Encoding.GetEncoding("GB2312").GetBytes(source);

                using (var memoryStream = new MemoryStream())
                using (var cryptoStream = new CryptoStream(memoryStream, provider.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cryptoStream.Write(inputBytes, 0, inputBytes.Length);
                    cryptoStream.FlushFinalBlock();

                    var result = new StringBuilder();
                    foreach (var b in memoryStream.ToArray())
                    {
                        result.AppendFormat("{0:X2}", b);
                    }
                    return result.ToString();
                }
            }
        }

        /// <summary>
        /// DES 解密（已废弃）。保留可用，用于读取历史数据。
        /// </summary>
        /// <param name="source">十六进制编码的密文</param>
        /// <param name="defaultValue">解密失败时的默认返回值</param>
        /// <returns>解密后的明文（GB2312 编码）</returns>
        [Obsolete("DES 算法已不安全，请使用 AES (Encrypto/Decrypto)")]
        public static string DecodeGB2312(string source, string defaultValue = "")
        {
            if (string.IsNullOrEmpty(source))
                return defaultValue;

            try
            {
#pragma warning disable SYSLIB0021
                using (var provider = new DESCryptoServiceProvider())
#pragma warning restore SYSLIB0021
                {
                    var keyBytes = Encoding.ASCII.GetBytes(LegacyDesKey.Substring(0, 8));
                    provider.Key = keyBytes;
                    provider.IV = keyBytes;

                    var bufferLength = source.Length / 2;
                    var buffer = new byte[bufferLength];
                    for (var i = 0; i < bufferLength; i++)
                    {
                        buffer[i] = (byte)Convert.ToInt32(source.Substring(i * 2, 2), 16);
                    }

                    using (var memoryStream = new MemoryStream())
                    using (var cryptoStream = new CryptoStream(memoryStream, provider.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(buffer, 0, buffer.Length);
                        cryptoStream.FlushFinalBlock();
                        return Encoding.GetEncoding("GB2312").GetString(memoryStream.ToArray());
                    }
                }
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// MD5 加密
        /// </summary>
        /// <param name="hashLength">哈希长度，16 或 32</param>
        /// <param name="source">待加密的明文</param>
        /// <returns>MD5 哈希值（小写十六进制）</returns>
        public static string Md5(int hashLength, string source)
        {
            if (source == null)
                return string.Empty;

            using (var md5 = MD5.Create())
            {
                var inputBytes = Encoding.UTF8.GetBytes(source);
                var hashBytes = md5.ComputeHash(inputBytes);

                var hashBuilder = new StringBuilder();
                foreach (var b in hashBytes)
                {
                    hashBuilder.Append(b.ToString("x2"));
                }

                var fullHash = hashBuilder.ToString();

                if (hashLength == 16)
                {
                    return fullHash.Substring(8, 16);
                }

                return fullHash;
            }
        }

        /// <summary>
        /// 计算字符串的 MD5 哈希值（32 位）
        /// </summary>
        /// <param name="input">待哈希的字符串</param>
        /// <returns>MD5 哈希值（小写十六进制）</returns>
        public static string Generate(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            using (var md5 = MD5.Create())
            {
                var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

                var hashBuilder = new StringBuilder(hashBytes.Length * 2);
                for (var i = 0; i < hashBytes.Length; i++)
                {
                    hashBuilder.Append(hashBytes[i].ToString("x2"));
                }

                return hashBuilder.ToString();
            }
        }

        /// <summary>
        /// MD5 加密（旧方法名，为了向后兼容）
        /// </summary>
        /// <param name="code">哈希长度，16 或 32</param>
        /// <param name="Source">待加密的明文</param>
        /// <returns>MD5 哈希值（小写十六进制）</returns>
        public static string md5(int code, string Source)
        {
            return Md5(code, Source);
        }
    }
}
