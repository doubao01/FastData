using System;
using FastUntility.Base;

namespace FastData.Example.Example
{
    /// <summary>
    /// 连接字符串加密使用示例
    /// 场景：保护数据库连接密码，防止明文泄露
    /// </summary>
    public static class EncryptionExample
    {
        /// <summary>
        /// 运行所有加密示例
        /// </summary>
        public static void Run()
        {
            Console.WriteLine("--- 连接字符串加密使用示例 ---");
            Console.WriteLine();

            DemoBasicEncryption();
            DemoEncryptedConfig();
            DemoEnvironmentVariables();
            DemoKeyRotation();
        }

        /// <summary>
        /// 示例 1: 基本加解密
        /// 场景：使用 BaseSymmetric 进行加解密
        /// </summary>
        private static void DemoBasicEncryption()
        {
            Console.WriteLine("=== 示例 1: 基本加解密 ===");
            Console.WriteLine("场景：使用 BaseSymmetric 进行连接字符串加解密");
            Console.WriteLine();

            Console.WriteLine("C# 代码：");
            Console.WriteLine(@"  // 1. 加密连接字符串
  var originalConnStr = ""Server=127.0.0.1;Database=app_db;Uid=root;Pwd=MySecretPassword123!"";
  var encrypted = BaseSymmetric.Encrypto(originalConnStr);
  Console.WriteLine($""加密后: {encrypted}"");

  // 2. 解密连接字符串
  var decrypted = BaseSymmetric.Decrypto(encrypted);
  Console.WriteLine($""解密后: {decrypted}"");

  // 3. 验证解密结果
  Console.WriteLine($""验证: {originalConnStr == decrypted}"");
  // 输出: True");
            Console.WriteLine();

            Console.WriteLine("说明：");
            Console.WriteLine("  - BaseSymmetric.Encrypto: 加密字符串");
            Console.WriteLine("  - BaseSymmetric.Decrypto: 解密字符串");
            Console.WriteLine("  - 加密算法: AES-256-CBC + HMAC-SHA256 完整性校验");
            Console.WriteLine("  - 必须先配置密钥: BaseSymmetric.ConfigureKey(key) 或环境变量 FASTDATA_ENCRYPTION_KEY");
            Console.WriteLine("  - 输出格式: FD2: + Base64(IV ‖ 密文 ‖ MAC)");
            Console.WriteLine();
        }

        /// <summary>
        /// 示例 2: 加密配置文件
        /// 场景：在配置文件中存储加密的连接字符串
        /// </summary>
        private static void DemoEncryptedConfig()
        {
            Console.WriteLine("=== 示例 2: 加密配置文件 ===");
            Console.WriteLine("场景：在 db.config 中存储加密的连接字符串");
            Console.WriteLine();

            Console.WriteLine("步骤 1: 加密连接字符串");
            Console.WriteLine("  // 加密连接字符串");
            Console.WriteLine("  var connStr = \"Server=127.0.0.1;Database=app_db;Uid=root;Pwd=MySecretPassword123!\";");
            Console.WriteLine("  var encrypted = BaseSymmetric.Encrypto(connStr);");
            Console.WriteLine("  Console.WriteLine($\"请将以下内容复制到 db.config:\");");
            Console.WriteLine("  Console.WriteLine($\"ConnStr=\\\"{encrypted}\\\"\");");
            Console.WriteLine();

            Console.WriteLine("步骤 2: 配置 db.config");
            Console.WriteLine("  <configuration>");
            Console.WriteLine("    <Connections>");
            Console.WriteLine("      <Add Key=\"Default\"");
            Console.WriteLine("           DbType=\"MySql\"");
            Console.WriteLine("           ConnStr=\"加密后的字符串\"");
            Console.WriteLine("           IsEncrypt=\"true\"");
            Console.WriteLine("           IsDefault=\"true\" />");
            Console.WriteLine("    </Connections>");
            Console.WriteLine("  </configuration>");
            Console.WriteLine();

            Console.WriteLine("步骤 3: 代码中使用");
            Console.WriteLine("  // FastData 会自动检测 IsEncrypt=\"true\"");
            Console.WriteLine("  // 自动解密连接字符串后再连接数据库");
            Console.WriteLine("  var users = FastRead.Query<User>(u => u.IsActive).ToList();");
            Console.WriteLine("  // 无需手动解密，ORM 自动处理");
            Console.WriteLine();

            Console.WriteLine("配置说明：");
            Console.WriteLine("  - IsEncrypt=\"true\": 标记连接字符串已加密");
            Console.WriteLine("  - FastData 自动解密后再使用");
            Console.WriteLine("  - 加密密钥存储在 BaseSymmetric 类中");
            Console.WriteLine();
        }

        /// <summary>
        /// 示例 3: 环境变量覆盖
        /// 场景：使用环境变量存储敏感信息
        /// </summary>
        private static void DemoEnvironmentVariables()
        {
            Console.WriteLine("=== 示例 3: 环境变量覆盖 ===");
            Console.WriteLine("场景：使用环境变量存储敏感信息，避免配置文件泄露");
            Console.WriteLine();

            Console.WriteLine("C# 代码：");
            Console.WriteLine(@"  // 1. 设置环境变量（在部署脚本中）
  // Linux/Mac:
  // export FASTDATA_CONN_DEFAULT=""Server=127.0.0.1;Database=app_db;Uid=root;Pwd=MySecretPassword123!""

  // Windows (PowerShell):
  // $env:FASTDATA_CONN_DEFAULT=""Server=127.0.0.1;Database=app_db;Uid=root;Pwd=MySecretPassword123!""

  // 2. 代码中使用（自动读取环境变量）
  var users = FastRead.Query<User>(u => u.IsActive).ToList(key: ""Default"");
  // FastData 会优先使用环境变量 FASTDATA_CONN_DEFAULT");
            Console.WriteLine();

            Console.WriteLine("环境变量命名规则：");
            Console.WriteLine("  - 格式: FASTDATA_CONN_{KEY}");
            Console.WriteLine("  - 示例: FASTDATA_CONN_DEFAULT, FASTDATA_CONN_LOG");
            Console.WriteLine("  - 优先级: 环境变量 > 配置文件");
            Console.WriteLine();
        }

        /// <summary>
        /// 示例 4: 密钥轮换
        /// 场景：定期更换加密密钥
        /// </summary>
        private static void DemoKeyRotation()
        {
            Console.WriteLine("=== 示例 4: 密钥轮换 ===");
            Console.WriteLine("场景：定期更换加密密钥，提高安全性");
            Console.WriteLine();

            Console.WriteLine("C# 代码：");
            Console.WriteLine(@"  // 轮换密钥（不再需要改源码里的常量）
  var connStr = ""Server=127.0.0.1;Database=app_db;Uid=root;Pwd=MySecretPassword123!"";

  // 生成并配置新的主密钥
  var newKey = BaseSymmetric.GenerateKey();
  BaseSymmetric.ConfigureKey(newKey);

  // 用新密钥重新加密存量配置
  var encryptedWithNewKey = BaseSymmetric.Encrypto(connStr);

  // 2. 批量更新配置文件
  var configFiles = new[] { ""db.config"", ""db.dev.config"", ""db.pro.config"" };
  foreach (var file in configFiles)
  {
      var content = File.ReadAllText(file);
      // 替换旧的加密字符串为新的
      content = ReplaceEncryptedString(content, oldEncrypted, encryptedWithNewKey);
      File.WriteAllText(file, content);
  }

  // 3. 验证新密钥
  var decrypted = BaseSymmetric.Decrypto(encryptedWithNewKey);
  Console.WriteLine($""验证新密钥: {connStr == decrypted}"");");
            Console.WriteLine();

            Console.WriteLine("密钥轮换最佳实践：");
            Console.WriteLine("  - 定期轮换密钥（建议每 90 天）");
            Console.WriteLine("  - 轮换前备份配置文件");
            Console.WriteLine("  - 轮换后验证所有服务正常");
            Console.WriteLine("  - 使用密钥管理服务（如 AWS KMS、Azure Key Vault）");
            Console.WriteLine();
        }
    }
}
