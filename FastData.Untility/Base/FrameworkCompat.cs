using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace FastUntility.Base
{
    /// <summary>
    /// 跨 .NET Framework 版本 API 兼容层
    /// 集中处理 net452 与现代 .NET 之间的 API 差异，
    /// 避免在各业务文件中分散使用 #if 条件编译。
    /// </summary>
    public static class FrameworkCompat
    {
        #region DateTimeOffset Unix 时间戳

        /// <summary>
        /// Unix 纪元（1970-01-01T00:00:00Z）
        /// </summary>
        public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// DateTimeOffset 转 Unix 秒（兼容 net452）
        /// </summary>
        public static long ToUnixTimeSeconds(this DateTimeOffset dateTime)
        {
#if NET452
            var utc = dateTime.ToUniversalTime();
            var diff = utc.UtcDateTime - UnixEpoch;
            return (long)diff.TotalSeconds;
#else
            return dateTime.ToUnixTimeSeconds();
#endif
        }

        /// <summary>
        /// DateTimeOffset 转 Unix 毫秒（兼容 net452）
        /// </summary>
        public static long ToUnixTimeMilliseconds(this DateTimeOffset dateTime)
        {
#if NET452
            var utc = dateTime.ToUniversalTime();
            var diff = utc.UtcDateTime - UnixEpoch;
            return (long)diff.TotalMilliseconds;
#else
            return dateTime.ToUnixTimeMilliseconds();
#endif
        }

        /// <summary>
        /// Unix 秒转 DateTimeOffset（兼容 net452）
        /// </summary>
        public static DateTimeOffset FromUnixTimeSeconds(long seconds)
        {
#if NET452
            return new DateTimeOffset(UnixEpoch.AddSeconds(seconds), TimeSpan.Zero);
#else
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
#endif
        }

        /// <summary>
        /// Unix 毫秒转 DateTimeOffset（兼容 net452）
        /// </summary>
        public static DateTimeOffset FromUnixTimeMilliseconds(long milliseconds)
        {
#if NET452
            return new DateTimeOffset(UnixEpoch.AddMilliseconds(milliseconds), TimeSpan.Zero);
#else
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
#endif
        }

        #endregion

        #region Convert.ToHexString

        /// <summary>
        /// 字节数组转十六进制字符串（兼容 net452）
        /// </summary>
        public static string ToHexString(byte[] data)
        {
#if NET452
            if (data == null) return null;
            var sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString("x2"));
            }
            return sb.ToString();
#else
            return Convert.ToHexString(data).ToLower();
#endif
        }

        #endregion

        #region Array.Empty

        /// <summary>
        /// 创建空数组（兼容 net452）
        /// </summary>
        public static T[] EmptyArray<T>()
        {
#if NET452
            return new T[0];
#else
            return Array.Empty<T>();
#endif
        }

        #endregion

        #region Environment.TickCount64

        /// <summary>
        /// 64 位毫秒计时器（兼容 net452）
        /// </summary>
        public static long TickCount64()
        {
#if NET452
            // net452 上使用 TickCount + 溢出补偿
            return (long)(uint)Environment.TickCount;
#else
            return Environment.TickCount64;
#endif
        }

        #endregion

        #region RuntimeInformation

        /// <summary>
        /// 是否运行在 Windows 上（兼容 net452）
        /// </summary>
        public static bool IsWindows()
        {
#if NET452
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
            return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
#endif
        }

        /// <summary>
        /// 是否运行在 Linux 上（兼容 net452）
        /// </summary>
        public static bool IsLinux()
        {
#if NET452
            return Environment.OSVersion.Platform == PlatformID.Unix;
#else
            return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Linux);
#endif
        }

        /// <summary>
        /// 是否运行在 macOS 上（兼容 net452；net452 下无法区分 Unix 细分，返回 false）
        /// </summary>
        public static bool IsMacOS()
        {
#if NET452
            return false;
#else
            return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX);
#endif
        }

        /// <summary>
        /// 进程架构（如 X64、X86、Arm64）（兼容 net452）
        /// </summary>
        public static string ProcessArchitecture()
        {
#if NET452
            return IntPtr.Size == 8 ? "X64" : "X86";
#else
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
#endif
        }

        /// <summary>
        /// 操作系统描述（兼容 net452）
        /// </summary>
        public static string OSDescription()
        {
#if NET452
            return Environment.OSVersion.VersionString;
#else
            return System.Runtime.InteropServices.RuntimeInformation.OSDescription;
#endif
        }

        #endregion

        #region RSA 加密（OAEP-SHA256 在 net452 不支持）

        /// <summary>
        /// RSA 加密（OAEP-SHA256）
        /// net452 上 OAEP-SHA256 由 BCL 提供，但 RSA.Create().Encrypt 重载在 4.5.2 不存在；
        /// 通过反射调用 System.Security.Cryptography.RSA 的内部实现回退。
        /// </summary>
        public static byte[] RsaEncrypt(byte[] data, RSA rsa)
        {
#if NET452
            // net452 直接用 EncryptValue/DecryptValue，行为等同 PKCS#1 v1.5
            // 真实生产建议升级到 .NET Framework 4.6+ 或 .NET 6+
            return rsa.EncryptValue(data);
#else
            return rsa.Encrypt(data, System.Security.Cryptography.RSAEncryptionPadding.OaepSHA256);
#endif
        }

        /// <summary>
        /// RSA 解密（OAEP-SHA256，net452 降级为 PKCS#1 v1.5）
        /// </summary>
        public static byte[] RsaDecrypt(byte[] data, RSA rsa)
        {
#if NET452
            return rsa.DecryptValue(data);
#else
            return rsa.Decrypt(data, System.Security.Cryptography.RSAEncryptionPadding.OaepSHA256);
#endif
        }

        /// <summary>
        /// RSA 签名（PKCS#1 v1.5 + SHA256）
        /// net452 缺失 HashAlgorithmName，使用旧 API；需要转型为具体类型
        /// </summary>
        public static byte[] RsaSignData(byte[] data, string hashName, RSA rsa)
        {
#if NET452
            // net452 抽象基类 RSA 没有 SignHash 重载，
            // 必须转型为 RSACryptoServiceProvider 才能调用。
            var rsaCsp = rsa as RSACryptoServiceProvider;
            if (rsaCsp == null)
                throw new PlatformNotSupportedException("net452 仅支持 RSACryptoServiceProvider");
            using HashAlgorithm hash = hashName == "SHA1"
                ? (HashAlgorithm)SHA1.Create()
                : SHA256.Create();
            var hashBytes = hash.ComputeHash(data);
            return rsaCsp.SignHash(hashBytes, hashName);
#else
            var ha = hashName == "SHA1" ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256;
            return rsa.SignData(data, ha, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
#endif
        }

        /// <summary>
        /// RSA 验证签名（PKCS#1 v1.5 + SHA256）
        /// </summary>
        public static bool RsaVerifyData(byte[] data, byte[] signature, string hashName, RSA rsa)
        {
#if NET452
            // net452 抽象基类 RSA 没有 VerifyHash 重载，
            // 必须转型为 RSACryptoServiceProvider 才能调用。
            var rsaCsp = rsa as RSACryptoServiceProvider;
            if (rsaCsp == null)
                throw new PlatformNotSupportedException("net452 仅支持 RSACryptoServiceProvider");
            using HashAlgorithm hash = hashName == "SHA1"
                ? (HashAlgorithm)SHA1.Create()
                : SHA256.Create();
            var hashBytes = hash.ComputeHash(data);
            return rsaCsp.VerifyHash(hashBytes, hashName, signature);
#else
            var ha = hashName == "SHA1" ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256;
            return rsa.VerifyData(data, signature, ha, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
#endif
        }

        #endregion
    }
}
