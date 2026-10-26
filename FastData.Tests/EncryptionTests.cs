using FastUntility.Base;
using System;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 连接字符串加密测试
    /// </summary>
    public class EncryptionTests
    {
        private const string TestKey = "unit-test-master-key-3f9a1c7e5b2d";

        /// <summary>
        /// v1（固定密钥 + 固定 IV）生成的历史密文，
        /// 由 openssl enc -aes-256-cbc -K Guz(%&amp;hj... -iv E4ghj*Ghg7!rNIfb 独立生成，
        /// 用于确保新实现的兼容解密路径不偏离历史行为。
        /// </summary>
        private const string LegacyCipherText =
            "TrOl4hKbeTUC2eZDbwayvUDqoqm5+l7Ws3JHTUrhn2JCBcoorXohqqDM6UK40iD6sUqsy3pH5ENqhndG1BadPg==";

        private const string LegacyPlainText =
            "Server=127.0.0.1;Database=test_db;Uid=root;Pwd=Password123!";

        private const string CipherPrefix = "FD2:";

        public EncryptionTests()
        {
            BaseSymmetric.ConfigureKey(TestKey);
            BaseSymmetric.AllowInsecureLegacyEncryption = false;
        }

        /// <summary>
        /// 测试加密和解密
        /// </summary>
        [Fact]
        public void Encrypt_Decrypt_RoundTrip()
        {
            var original = LegacyPlainText;

            var encrypted = BaseSymmetric.Encrypto(original);
            var decrypted = BaseSymmetric.Decrypto(encrypted);

            Assert.NotEqual(original, encrypted);
            Assert.Equal(original, decrypted);
        }

        /// <summary>
        /// 测试加密结果不为空
        /// </summary>
        [Fact]
        public void Encrypt_Returns_NonEmpty()
        {
            var original = "test_connection_string";

            var encrypted = BaseSymmetric.Encrypto(original);

            Assert.NotNull(encrypted);
            Assert.NotEmpty(encrypted);
            Assert.StartsWith(CipherPrefix, encrypted);
        }

        /// <summary>
        /// 相同明文必须产生不同密文（随机 IV）。
        /// v1 曾断言二者相等，那等于把固定 IV 的缺陷钉成期望行为。
        /// </summary>
        [Fact]
        public void Encrypt_Same_Input_Different_Output()
        {
            var original = "same_input_string";

            var encrypted1 = BaseSymmetric.Encrypto(original);
            var encrypted2 = BaseSymmetric.Encrypto(original);

            Assert.NotEqual(encrypted1, encrypted2);
            Assert.Equal(original, BaseSymmetric.Decrypto(encrypted1));
            Assert.Equal(original, BaseSymmetric.Decrypto(encrypted2));
        }

        /// <summary>
        /// 测试不同输入产生不同加密结果
        /// </summary>
        [Fact]
        public void Encrypt_Different_Input_Different_Output()
        {
            var input1 = "connection_string_1";
            var input2 = "connection_string_2";

            var encrypted1 = BaseSymmetric.Encrypto(input1);
            var encrypted2 = BaseSymmetric.Encrypto(input2);

            Assert.NotEqual(encrypted1, encrypted2);
        }

        /// <summary>
        /// 测试解密空字符串
        /// </summary>
        [Fact]
        public void Decrypt_Empty_String()
        {
            var decrypted = BaseSymmetric.Decrypto("");

            Assert.NotNull(decrypted);
            Assert.Empty(decrypted);
        }

        /// <summary>
        /// 测试加密特殊字符
        /// </summary>
        [Fact]
        public void Encrypt_Special_Characters()
        {
            var original = "Server=127.0.0.1;Pwd=P@ssw0rd!#$%";

            var encrypted = BaseSymmetric.Encrypto(original);
            var decrypted = BaseSymmetric.Decrypto(encrypted);

            Assert.Equal(original, decrypted);
        }

        /// <summary>
        /// 测试加密长字符串
        /// </summary>
        [Fact]
        public void Encrypt_Long_String()
        {
            var original = new string('A', 1000) + ";Server=long_connection_string_test";

            var encrypted = BaseSymmetric.Encrypto(original);
            var decrypted = BaseSymmetric.Decrypto(encrypted);

            Assert.Equal(original, decrypted);
        }

        /// <summary>
        /// 非 ASCII 明文往返
        /// </summary>
        [Fact]
        public void Encrypt_Unicode_RoundTrip()
        {
            var original = "服务器=127.0.0.1;数据库=测试库;密码=中文口令🔐";

            var encrypted = BaseSymmetric.Encrypto(original);

            Assert.Equal(original, BaseSymmetric.Decrypto(encrypted));
        }

        /// <summary>
        /// 历史（v1）密文必须仍可解密，否则升级会让存量配置全部失效
        /// </summary>
        [Fact]
        public void Legacy_CipherText_Still_Decrypts()
        {
            var decrypted = BaseSymmetric.Decrypto(LegacyCipherText);

            Assert.Equal(LegacyPlainText, decrypted);
        }

        /// <summary>
        /// 密文被篡改时必须拒绝解密，而不是返回垃圾明文
        /// </summary>
        [Fact]
        public void Tampered_CipherText_Is_Rejected()
        {
            var original = "Server=prod;Uid=sa;Pwd=S3cr3t!";
            var encrypted = BaseSymmetric.Encrypto(original);

            var payload = Convert.FromBase64String(encrypted.Substring(CipherPrefix.Length));
            payload[payload.Length - 1] ^= 0xFF; // 破坏 HMAC 标签
            var tampered = CipherPrefix + Convert.ToBase64String(payload);

            Assert.Equal(string.Empty, BaseSymmetric.Decrypto(tampered));

            string plain;
            string error;
            Assert.False(BaseSymmetric.TryDecrypto(tampered, out plain, out error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        /// <summary>
        /// 换密钥必须解不开旧密文
        /// </summary>
        [Fact]
        public void Wrong_Key_Cannot_Decrypt()
        {
            var original = "Server=prod;Uid=sa;Pwd=S3cr3t!";
            var encrypted = BaseSymmetric.EncryptWithKey(original, "key-number-one");

            Assert.Equal(string.Empty, BaseSymmetric.Decrypto(encrypted));

            BaseSymmetric.ConfigureKey("key-number-two");
            Assert.Equal(string.Empty, BaseSymmetric.Decrypto(encrypted));

            BaseSymmetric.ConfigureKey("key-number-one");
            Assert.Equal(original, BaseSymmetric.Decrypto(encrypted));
        }

        /// <summary>
        /// 未配置密钥时加密必须失败，而不是悄悄用硬编码密钥
        /// </summary>
        [Fact]
        public void Encrypt_Without_Key_Throws()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(BaseSymmetric.KeyEnvironmentVariable)))
                return; // 环境注入了密钥，跳过

            BaseSymmetric.ClearKey();
            try
            {
                Assert.Throws<InvalidOperationException>(() => BaseSymmetric.Encrypto("anything"));
            }
            finally
            {
                BaseSymmetric.ConfigureKey(TestKey);
            }
        }

        /// <summary>
        /// 迁移期开关：显式开启后仍可生成 v1 格式密文
        /// </summary>
        [Fact]
        public void Legacy_Encryption_Requires_OptIn()
        {
            var original = "legacy-compat-value";

            BaseSymmetric.ClearKey();
            BaseSymmetric.AllowInsecureLegacyEncryption = true;
            try
            {
                var legacyCipher = BaseSymmetric.Encrypto(original);

                Assert.False(legacyCipher.StartsWith(CipherPrefix, StringComparison.Ordinal));
                Assert.Equal(original, BaseSymmetric.Decrypto(legacyCipher));
            }
            finally
            {
                BaseSymmetric.AllowInsecureLegacyEncryption = false;
                BaseSymmetric.ConfigureKey(TestKey);
            }
        }
    }
}
