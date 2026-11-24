using System;
using System.Collections.Generic;
using System.Linq;

namespace FastData.Migrations
{
    /// <summary>
    /// 保留兼容入口的迁移管理器；执行、回滚和历史读取尚未实现，调用时显式报错
    /// </summary>
    public class MigrationManager
    {
        private readonly string _connectionString;
        private readonly List<IMigration> _migrations = new List<IMigration>();
        private const string MigrationNotSupportedHint =
            "数据库迁移执行、回滚和历史记录尚未实现。当前入口仅为兼容保留，不会执行 Up/Down；参阅 MODERN_ORM_FEATURES.md。";

        /// <summary>
        /// Initializes a migration manager.
        /// </summary>
        /// <param name="connectionString">Database connection string.</param>
        public MigrationManager(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <summary>
        /// 添加迁移
        /// </summary>
        public void AddMigration(IMigration migration)
        {
            if (migration == null)
                throw new ArgumentNullException(nameof(migration));

            _migrations.Add(migration);
        }

        /// <summary>
        /// 执行所有未应用的迁移
        /// </summary>
        public void Migrate()
        {
            throw new NotSupportedException(MigrationNotSupportedHint);
        }

        /// <summary>
        /// 回滚到指定版本
        /// </summary>
        public void Rollback(string targetVersion)
        {
            throw new NotSupportedException(MigrationNotSupportedHint);
        }

        /// <summary>
        /// 获取迁移历史
        /// </summary>
        public List<MigrationInfo> GetMigrationHistory()
        {
            throw new NotSupportedException(MigrationNotSupportedHint);
        }
    }

    /// <summary>
    /// 迁移接口
    /// </summary>
    public interface IMigration
    {
        /// <summary>
        /// 迁移版本
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 迁移描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 执行迁移（Up）
        /// </summary>
        void Up();

        /// <summary>
        /// 回滚迁移（Down）
        /// </summary>
        void Down();
    }

    /// <summary>
    /// 迁移基类
    /// </summary>
    public abstract class Migration : IMigration
    {
        /// <inheritdoc />
        public abstract string Version { get; }
        /// <inheritdoc />
        public abstract string Description { get; }

        /// <inheritdoc />
        public abstract void Up();
        /// <inheritdoc />
        public abstract void Down();
    }

    /// <summary>
    /// 迁移信息
    /// </summary>
    public class MigrationInfo
    {
        /// <summary>Gets or sets the migration version.</summary>
        public string Version { get; set; }
        /// <summary>Gets or sets the migration description.</summary>
        public string Description { get; set; }
        /// <summary>Gets or sets when the migration was applied.</summary>
        public DateTime? AppliedAt { get; set; }
    }

    /// <summary>
    /// 迁移异常
    /// </summary>
    public class MigrationException : Exception
    {
        /// <summary>Gets the migration version associated with the exception.</summary>
        public string MigrationVersion { get; }

        /// <summary>Initializes a migration exception.</summary>
        /// <param name="message">Exception message.</param>
        public MigrationException(string message) : base(message)
        {
        }

        /// <summary>Initializes a migration exception with an inner exception.</summary>
        /// <param name="message">Exception message.</param>
        /// <param name="innerException">Inner exception.</param>
        public MigrationException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>Initializes a migration exception for a specific migration version.</summary>
        /// <param name="migrationVersion">Migration version.</param>
        /// <param name="message">Exception message.</param>
        public MigrationException(string migrationVersion, string message) : base(message)
        {
            MigrationVersion = migrationVersion;
        }

        /// <summary>Initializes a migration exception for a specific migration version with an inner exception.</summary>
        /// <param name="migrationVersion">Migration version.</param>
        /// <param name="message">Exception message.</param>
        /// <param name="innerException">Inner exception.</param>
        public MigrationException(string migrationVersion, string message, Exception innerException) 
            : base(message, innerException)
        {
            MigrationVersion = migrationVersion;
        }
    }

    /// <summary>
    /// SQL 迁移构建器 - 帮助创建 SQL 迁移
    /// </summary>
    public class SqlMigrationBuilder
    {
        private readonly List<string> _sqlStatements = new List<string>();

        /// <summary>
        /// 执行 SQL
        /// </summary>
        public void Execute(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL 语句不能为空", nameof(sql));

            _sqlStatements.Add(sql);
        }

        /// <summary>
        /// 创建表
        /// </summary>
        public void CreateTable(string tableName, Action<TableBuilder> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            var builder = new TableBuilder(tableName);
            configure(builder);
            _sqlStatements.Add(builder.ToSql());
        }

        /// <summary>
        /// 删除表
        /// </summary>
        public void DropTable(string tableName)
        {
            _sqlStatements.Add(string.Format("DROP TABLE IF EXISTS {0};", tableName));
        }

        /// <summary>
        /// 添加列
        /// </summary>
        public void AddColumn(string tableName, string columnName, string columnType, bool nullable = true)
        {
            var nullableSql = nullable ? "" : " NOT NULL";
            _sqlStatements.Add(string.Format("ALTER TABLE {0} ADD COLUMN {1} {2}{3};", tableName, columnName, columnType, nullableSql));
        }

        /// <summary>
        /// 删除列
        /// </summary>
        public void DropColumn(string tableName, string columnName)
        {
            _sqlStatements.Add(string.Format("ALTER TABLE {0} DROP COLUMN {1};", tableName, columnName));
        }

        /// <summary>
        /// 创建索引
        /// </summary>
        public void CreateIndex(string indexName, string tableName, string columnName)
        {
            _sqlStatements.Add(string.Format("CREATE INDEX {0} ON {1}({2});", indexName, tableName, columnName));
        }

        /// <summary>
        /// 删除索引
        /// </summary>
        public void DropIndex(string indexName)
        {
            _sqlStatements.Add(string.Format("DROP INDEX IF EXISTS {0};", indexName));
        }

        /// <summary>
        /// Gets the SQL statements collected by the builder.
        /// </summary>
        /// <returns>A copy of collected SQL statements.</returns>
        public List<string> GetSqlStatements()
        {
            return new List<string>(_sqlStatements);
        }
    }

    /// <summary>
    /// 表构建器
    /// </summary>
    public class TableBuilder
    {
        private readonly string _tableName;
        private readonly List<ColumnDefinition> _columns = new List<ColumnDefinition>();
        private string _primaryKey;

        /// <summary>
        /// Initializes a table builder.
        /// </summary>
        /// <param name="tableName">Table name.</param>
        public TableBuilder(string tableName)
        {
            _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        }

        /// <summary>
        /// Adds a column definition.
        /// </summary>
        /// <param name="name">Column name.</param>
        /// <param name="type">Column SQL type.</param>
        /// <param name="nullable">Whether the column allows null values.</param>
        /// <param name="defaultValue">Optional default value SQL.</param>
        /// <returns>The current table builder.</returns>
        public TableBuilder Column(string name, string type, bool nullable = true, string defaultValue = null)
        {
            _columns.Add(new ColumnDefinition
            {
                Name = name,
                Type = type,
                Nullable = nullable,
                DefaultValue = defaultValue
            });
            return this;
        }

        /// <summary>
        /// Sets the primary key column.
        /// </summary>
        /// <param name="columnName">Primary key column name.</param>
        /// <returns>The current table builder.</returns>
        public TableBuilder PrimaryKey(string columnName)
        {
            _primaryKey = columnName;
            return this;
        }

        /// <summary>
        /// Builds the CREATE TABLE SQL statement.
        /// </summary>
        /// <returns>CREATE TABLE SQL.</returns>
        public string ToSql()
        {
            var columnsSql = string.Join(",\n    ", _columns.Select(c =>
            {
                var sql = string.Format("{0} {1}", c.Name, c.Type);
                if (!c.Nullable)
                    sql += " NOT NULL";
                if (!string.IsNullOrEmpty(c.DefaultValue))
                    sql += string.Format(" DEFAULT {0}", c.DefaultValue);
                return sql;
            }));

            var sql = string.Format("CREATE TABLE {0} (\n    {1}", _tableName, columnsSql);
            if (!string.IsNullOrEmpty(_primaryKey))
                sql += string.Format(",\n    PRIMARY KEY ({0})", _primaryKey);
            sql += "\n);";

            return sql;
        }

        private class ColumnDefinition
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public bool Nullable { get; set; }
            public string DefaultValue { get; set; }
        }
    }
}
