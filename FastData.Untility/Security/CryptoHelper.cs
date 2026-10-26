using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FastUntility.Base;

namespace FastUntility.Security
{
    /// <summary>
    /// AES 加解密工具
    /// </summary>
    public static class AesHelper
    {
        /// <summary>
        /// AES 加密
        /// <para>
        /// 传入 <paramref name="iv"/> 时按调用方指定的 IV 加密，输出纯密文（兼容旧调用方式）；
        /// 不传 <paramref name="iv"/> 时使用随机 IV，并把 IV 前置写入密文头部，
        /// 这样 <see cref="Decrypt"/> 才能取回同一个 IV。
        /// </para>
        /// </summary>
        public static string Encrypt(string plainText, string key, string iv = null)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException(nameof(plainText));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            using var aes = Aes.Create();
            aes.Key = DeriveKeyBytes(key);

            using var ms = new MemoryStream();

            if (!string.IsNullOrEmpty(iv))
            {
                // 显式 IV：不写头部，保持既有输出格式
                aes.IV = DeriveIvBytes(iv);
                using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    var plainBytes = Encoding.UTF8.GetBytes(plainText);
                    cs.Write(plainBytes, 0, plainBytes.Length);
                }
                return Convert.ToBase64String(ms.ToArray());
            }

            // 随机 IV：必须随密文一起输出，否则无法解密
            aes.GenerateIV();
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                cs.Write(plainBytes, 0, plainBytes.Length);
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>
        /// AES 解密
        /// <para>
        /// 传入 <paramref name="iv"/> 时按指定 IV 解密纯密文；
        /// 不传 <paramref name="iv"/> 时从密文头部读取加密时写入的随机 IV。
        /// </para>
        /// </summary>
        public static string Decrypt(string cipherText, string key, string iv = null)
        {
            if (string.IsNullOrEmpty(cipherText))
                throw new ArgumentNullException(nameof(cipherText));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            var cipherBytes = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = DeriveKeyBytes(key);

            if (!string.IsNullOrEmpty(iv))
            {
                aes.IV = DeriveIvBytes(iv);
                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream(cipherBytes);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var reader = new StreamReader(cs);
                return reader.ReadToEnd();
            }

            const int ivLength = 16;
            if (cipherBytes.Length <= ivLength)
                throw new CryptographicException("密文长度不足，缺少 IV 头部或数据已损坏。");

            var ivBytes = new byte[ivLength];
            Array.Copy(cipherBytes, 0, ivBytes, 0, ivLength);
            aes.IV = ivBytes;

            using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
            using (var ms = new MemoryStream(cipherBytes, ivLength, cipherBytes.Length - ivLength))
            using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
            using (var reader = new StreamReader(cs))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// AES 加密（带完整 IV）
        /// </summary>
        public static string EncryptWithIV(string plainText, string key)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException(nameof(plainText));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            using var aes = Aes.Create();
            aes.Key = DeriveKeyBytes(key);
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            // 写入 IV
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                cs.Write(plainBytes, 0, plainBytes.Length);
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>
        /// AES 解密（带完整 IV）
        /// </summary>
        public static string DecryptWithIV(string cipherText, string key)
        {
            if (string.IsNullOrEmpty(cipherText))
                throw new ArgumentNullException(nameof(cipherText));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            var cipherBytes = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            aes.Key = DeriveKeyBytes(key);

            // 读取 IV
            var iv = new byte[16];
            Array.Copy(cipherBytes, 0, iv, 0, 16);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(cipherBytes, 16, cipherBytes.Length - 16);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(cs);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// 把任意长度字符串密钥派生为 32 字节 AES 密钥（UTF-8 编码后截断/零填充）。
        /// <para>按字节处理而非按字符：非 ASCII 密钥不会因字符数达标而字节数不足。</para>
        /// </summary>
        private static byte[] DeriveKeyBytes(string key)
        {
            const int keyLength = 32;
            var raw = Encoding.UTF8.GetBytes(key);
            var result = new byte[keyLength];
            Buffer.BlockCopy(raw, 0, result, 0, Math.Min(raw.Length, keyLength));
            return result;
        }

        /// <summary>
        /// 把任意长度字符串 IV 派生为 16 字节 IV（UTF-8 编码后截断/零填充）。
        /// </summary>
        private static byte[] DeriveIvBytes(string iv)
        {
            const int ivLength = 16;
            var raw = Encoding.UTF8.GetBytes(iv);
            var result = new byte[ivLength];
            Buffer.BlockCopy(raw, 0, result, 0, Math.Min(raw.Length, ivLength));
            return result;
        }

        /// <summary>
        /// 生成随机 IV
        /// </summary>
        public static byte[] GenerateIV()
        {
            using var aes = Aes.Create();
            aes.GenerateIV();
            return aes.IV;
        }

        /// <summary>
        /// 生成随机密钥
        /// </summary>
        public static string GenerateKey(int keySize = 256)
        {
            using var aes = Aes.Create();
            aes.KeySize = keySize;
            aes.GenerateKey();
            return Convert.ToBase64String(aes.Key);
        }
    }

    /// <summary>
    /// RSA 加解密工具
    /// </summary>
    public static class RsaHelper
    {
        /// <summary>
        /// 生成 RSA 密钥对
        /// </summary>
        public static (string PublicKey, string PrivateKey) GenerateKeyPair(int keySize = 2048)
        {
            using var rsa = RSA.Create();
            rsa.KeySize = keySize;
            var publicKey = rsa.ToXmlString(false);
            var privateKey = rsa.ToXmlString(true);
            return (publicKey, privateKey);
        }

        /// <summary>
        /// RSA 加密
        /// </summary>
        public static string Encrypt(string plainText, string publicKey)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException(nameof(plainText));
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentNullException(nameof(publicKey));

            using var rsa = RSA.Create();
            rsa.FromXmlString(publicKey);
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = FrameworkCompat.RsaEncrypt(plainBytes, rsa);
            return Convert.ToBase64String(encryptedBytes);
        }

        /// <summary>
        /// RSA 解密
        /// </summary>
        public static string Decrypt(string cipherText, string privateKey)
        {
            if (string.IsNullOrEmpty(cipherText))
                throw new ArgumentNullException(nameof(cipherText));
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentNullException(nameof(privateKey));

            using var rsa = RSA.Create();
            rsa.FromXmlString(privateKey);
            var cipherBytes = Convert.FromBase64String(cipherText);
            var decryptedBytes = FrameworkCompat.RsaDecrypt(cipherBytes, rsa);
            return Encoding.UTF8.GetString(decryptedBytes);
        }

        /// <summary>
        /// RSA 签名
        /// </summary>
        public static string Sign(string data, string privateKey, string hashAlgorithm = "SHA256")
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentNullException(nameof(privateKey));

            using var rsa = RSA.Create();
            rsa.FromXmlString(privateKey);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var signatureBytes = FrameworkCompat.RsaSignData(dataBytes, hashAlgorithm, rsa);
            return Convert.ToBase64String(signatureBytes);
        }

        /// <summary>
        /// RSA 验证签名
        /// </summary>
        public static bool Verify(string data, string signature, string publicKey, string hashAlgorithm = "SHA256")
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrEmpty(signature))
                throw new ArgumentNullException(nameof(signature));
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentNullException(nameof(publicKey));

            using var rsa = RSA.Create();
            rsa.FromXmlString(publicKey);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var signatureBytes = Convert.FromBase64String(signature);
            return FrameworkCompat.RsaVerifyData(dataBytes, signatureBytes, hashAlgorithm, rsa);
        }
    }

    /// <summary>
    /// HMAC 工具
    /// </summary>
    public static class HmacHelper
    {
        /// <summary>
        /// HMAC-SHA256 签名
        /// </summary>
        public static string HmacSha256(string data, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return FrameworkCompat.ToHexString(hash);
        }

        /// <summary>
        /// HMAC-SHA384 签名
        /// </summary>
        public static string HmacSha384(string data, string key)
        {
            using var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return FrameworkCompat.ToHexString(hash);
        }

        /// <summary>
        /// HMAC-SHA512 签名
        /// </summary>
        public static string HmacSha512(string data, string key)
        {
            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return FrameworkCompat.ToHexString(hash);
        }

        /// <summary>
        /// HMAC-MD5 签名
        /// </summary>
        public static string HmacMd5(string data, string key)
        {
            using var hmac = new HMACMD5(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return FrameworkCompat.ToHexString(hash);
        }

        /// <summary>
        /// 验证 HMAC 签名
        /// </summary>
        public static bool VerifyHmac(string data, string key, string expectedHash, string algorithm = "SHA256")
        {
            var actualHash = algorithm.ToUpper() switch
            {
                "SHA256" => HmacSha256(data, key),
                "SHA384" => HmacSha384(data, key),
                "SHA512" => HmacSha512(data, key),
                "MD5" => HmacMd5(data, key),
                _ => throw new NotSupportedException(string.Format("不支持的算法: {0}", algorithm))
            };
            return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// API Key 工具
    /// </summary>
    public static class ApiKeyHelper
    {
        /// <summary>
        /// 生成 API Key
        /// </summary>
        public static string GenerateApiKey(int length = 32)
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Substring(0, length);
        }

        /// <summary>
        /// 生成带前缀的 API Key
        /// </summary>
        public static string GenerateApiKey(string prefix, int length = 32)
        {
            var key = GenerateApiKey(length);
            return string.Format("{0}_{1}", prefix, key);
        }

        /// <summary>
        /// 哈希 API Key（用于存储）
        /// </summary>
        public static string HashApiKey(string apiKey)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// 验证 API Key
        /// </summary>
        public static bool VerifyApiKey(string apiKey, string hashedApiKey)
        {
            var hashedInput = HashApiKey(apiKey);
            return string.Equals(hashedInput, hashedApiKey, StringComparison.Ordinal);
        }
    }
}
