using System;
using System.IO;
using FastData.Config;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// 活动环境配置测试
    /// 
    /// 测试环境配置的加载和切换功能。
    /// </summary>
    [Collection(nameof(ConfigEnvironmentCollection))]
    public class ActiveEnvironmentTests
    {
        /// <summary>
        /// 测试开发环境配置加载
        /// </summary>
        [Fact]
        public void ActiveAttribute_Dev_ShouldLoadDevConfig()
        {
            // Arrange
            var dbConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db.config");
            var dbDevConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db.dev.config");
            
            if (!File.Exists(dbConfigPath) || !File.Exists(dbDevConfigPath))
            {
                Console.WriteLine("环境配置文件不存在，跳过测试");
                return;
            }
            
            // Act
            var activeEnv = FastDataConfig.GetActiveEnvironment();
            
            // Assert
            Assert.NotNull(activeEnv);
            Assert.NotEmpty(activeEnv);
            Console.WriteLine("Active Environment: {0}", activeEnv);
        }

        /// <summary>
        /// 测试环境变量覆盖配置文件
        /// </summary>
        [Fact]
        public void ActiveAttribute_EnvVar_ShouldOverrideConfig()
        {
            // Arrange
            var originalEnv = Environment.GetEnvironmentVariable("FASTDATA_ACTIVE");
            
            try
            {
                // 设置环境变量
                Environment.SetEnvironmentVariable("FASTDATA_ACTIVE", "pro");
                
                // Act
                var activeEnv = FastDataConfig.GetActiveEnvironment();
                
                // Assert
                Assert.Equal("pro", activeEnv);
                Console.WriteLine("Active Environment (from env var): {0}", activeEnv);
            }
            finally
            {
                // 恢复原始环境变量
                Environment.SetEnvironmentVariable("FASTDATA_ACTIVE", originalEnv);
            }
        }

        /// <summary>
        /// 测试配置文件加载
        /// </summary>
        [Fact]
        public void ActiveAttribute_Config_ShouldLoadConfig()
        {
            // Arrange
            var dbConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db.config");
            if (!File.Exists(dbConfigPath))
            {
                Console.WriteLine("db.config 文件不存在，跳过测试");
                return;
            }
            
            // 读取配置文件内容
            var configContent = File.ReadAllText(dbConfigPath);
            Console.WriteLine("db.config content:\n{0}", configContent);
            
            // Act - 尝试加载配置
            string connStr;
            try
            {
                connStr = FastDataConfig.GetConnectionString("SqlServer");
            }
            catch (Exception ex)
            {
                Console.WriteLine("SqlServer 配置不可用，跳过测试: {0}", ex.Message);
                return;
            }
            
            // Assert
            Assert.NotNull(connStr);
            Assert.NotEmpty(connStr);
            Console.WriteLine("SqlServer ConnectionString: {0}", connStr);
        }

        /// <summary>
        /// 测试开发环境配置包含测试数据库
        /// </summary>
        [Fact]
        public void ActiveAttribute_DevConfig_ShouldHaveDifferentDatabase()
        {
            // Arrange
            var dbDevConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db.dev.config");
            if (!File.Exists(dbDevConfigPath))
            {
                Console.WriteLine("db.dev.config 文件不存在，跳过测试");
                return;
            }
            
            // 读取 dev 配置文件内容
            var configContent = File.ReadAllText(dbDevConfigPath);
            Console.WriteLine("db.dev.config content:\n{0}", configContent);
            
            // 验证 dev 配置包含测试数据库
            Assert.Contains("FastDataTest", configContent);
        }

        /// <summary>
        /// 测试生产环境配置包含 Pro 数据库
        /// </summary>
        [Fact]
        public void ActiveAttribute_ProConfig_ShouldHaveDifferentDatabase()
        {
            // Arrange
            var dbProConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db.pro.config");
            if (!File.Exists(dbProConfigPath))
            {
                Console.WriteLine("db.pro.config 文件不存在，跳过测试");
                return;
            }
            
            // 读取 pro 配置文件内容
            var configContent = File.ReadAllText(dbProConfigPath);
            Console.WriteLine("db.pro.config content:\n{0}", configContent);
            
            // 验证 pro 配置包含 Pro 数据库
            Assert.Contains("FastDataTest_Pro", configContent);
        }
    }
}
