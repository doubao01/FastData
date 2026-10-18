using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using FastData.Config;
using Microsoft.Data.SqlClient;

namespace FastData.SyncTool.WinForms.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService()
        {
            // 从配置文件获取连接字符串；不内置任何默认凭据，避免密钥硬编码
            _connectionString = FastDataConfig.GetConnectionString("SqlServer");
        }

        public DatabaseService(string connectionString)
        {
            _connectionString = connectionString;
        }

        private string RequireConnectionString()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                throw new InvalidOperationException("未配置数据库连接字符串。请在配置文件中设置名为 'SqlServer' 的连接字符串，或使用带连接字符串参数的构造函数。");
            }
            return _connectionString;
        }

        public List<string> GetTableList()
        {
            var tables = new List<string>();
            using (var conn = new SqlConnection(RequireConnectionString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tables.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return tables;
        }

        public async Task<int> GetTableCountAsync(string tableName)
        {
            using (var conn = new SqlConnection(RequireConnectionString()))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Format("SELECT COUNT(*) FROM [{0}]", tableName);
                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result);
                }
            }
        }

        public async Task<DataTable> GetTableDataAsync(string tableName, int offset, int batchSize)
        {
            var dataTable = new DataTable();
            using (var conn = new SqlConnection(RequireConnectionString()))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Format(
                        "SELECT * FROM [{0}] ORDER BY (SELECT NULL) OFFSET {1} ROWS FETCH NEXT {2} ROWS ONLY",
                        tableName, offset, batchSize);
                    using (var adapter = new SqlDataAdapter((SqlCommand)cmd))
                    {
                        adapter.Fill(dataTable);
                    }
                }
            }
            return dataTable;
        }
    }
}
