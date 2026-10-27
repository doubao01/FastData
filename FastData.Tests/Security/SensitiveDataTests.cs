using FastData.Config;
using FastData.DevTools;
using Xunit;

namespace FastData.Tests.Security
{
    /// <summary>
    /// 审查报告指出的敏感信息与假成功问题，回归测试。
    /// </summary>
    public class SensitiveDataTests
    {
        /// <summary>
        /// 连接串脱敏必须覆盖账号与各类密钥字段，而不只是 pwd/password。
        /// 原实现只处理 pwd|password，脱敏后仍会泄漏 uid 与 access key。
        /// </summary>
        [Theory]
        [InlineData("server=1;uid=sa;pwd=secret", "uid=***", "pwd=***")]
        [InlineData("Server=x;User Id=admin;Password=123", "User Id=***", "Password=***")]
        [InlineData("AccountKey=abc123;Endpoint=x", "AccountKey=***", null)]
        [InlineData("token=xyz;host=h", "token=***", null)]
        [InlineData("SharedAccessKey=key1;name=n", "SharedAccessKey=***", null)]
        public void MaskConnStr_Redacts_Credentials(string input, string expectA, string expectB)
        {
            var masked = DataConfig.MaskConnStr(input);

            Assert.Contains(expectA, masked);
            if (expectB != null)
                Assert.Contains(expectB, masked);

            Assert.DoesNotContain("secret", masked);
            Assert.DoesNotContain("abc123", masked);
            Assert.DoesNotContain("xyz", masked);
        }

        /// <summary>
        /// 脱敏不应破坏非凭据字段（服务器地址仍需可读，用于排查）
        /// </summary>
        [Fact]
        public void MaskConnStr_Keeps_Non_Credential_Parts()
        {
            var masked = DataConfig.MaskConnStr("server=127.0.0.1,1433;database=Demo;uid=sa;pwd=x");

            Assert.Contains("server=127.0.0.1,1433", masked);
            Assert.Contains("database=Demo", masked);
        }

        /// <summary>
        /// 空连接串返回空字符串而不是抛异常
        /// </summary>
        [Fact]
        public void MaskConnStr_Handles_Null_And_Empty()
        {
            Assert.Equal(string.Empty, DataConfig.MaskConnStr(null));
            Assert.Equal(string.Empty, DataConfig.MaskConnStr(""));
        }
    }
}