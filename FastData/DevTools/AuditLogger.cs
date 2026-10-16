using System;
using FastData.Context;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using FastData.Property;
using FastData.Model;

namespace FastData.DevTools
{
    /// <summary>
    /// 参数辅助工具：DbParameterCollection 基类不提供 AddWithValue，
    /// 统一通过命令的 CreateParameter 构造参数，兼容所有 ADO.NET 提供程序。
    /// </summary>
    internal static class DevToolsParamHelper
    {
        /// <summary>
        /// 向命令添加参数值
        /// </summary>
        /// <param name="command">数据库命令</param>
        /// <param name="name">参数名</param>
        /// <param name="value">参数值</param>
        public static void AddWithValue(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
    /// <summary>
    /// 审计日志记录器
    /// </summary>
    public interface IAuditLogger
    {
        /// <summary>
        /// 记录审计日志条目
        /// </summary>
        /// <param name="entry">审计日志条目</param>
        void Log(AuditEntry entry);
        /// <summary>
        /// 记录新增操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">新增实体</param>
        /// <param name="dbKey">数据库标识</param>
        void LogAdd<T>(T entity, string dbKey = null) where T : class, new();
        /// <summary>
        /// 记录更新操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="oldEntity">更新前实体</param>
        /// <param name="newEntity">更新后实体</param>
        /// <param name="dbKey">数据库标识</param>
        void LogUpdate<T>(T oldEntity, T newEntity, string dbKey = null) where T : class, new();
        /// <summary>
        /// 记录删除操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">删除实体</param>
        /// <param name="dbKey">数据库标识</param>
        void LogDelete<T>(T entity, string dbKey = null) where T : class, new();
    }

    /// <summary>
    /// 控制台审计日志记录器
    /// </summary>
    public class ConsoleAuditLogger : IAuditLogger
    {
        /// <summary>
        /// 记录审计日志条目到控制台
        /// </summary>
        /// <param name="entry">审计日志条目</param>
        public void Log(AuditEntry entry)
        {
            Console.WriteLine(string.Format("[{0}] {1} - {2}", entry.Timestamp, entry.Action, entry.EntityType));
            Console.WriteLine(string.Format("  操作人: {0}", entry.User));
            Console.WriteLine(string.Format("  主键: {0}", entry.PrimaryKey));
            if (!string.IsNullOrEmpty(entry.Changes))
            {
                Console.WriteLine(string.Format("  变更: {0}", entry.Changes));
            }
        }

        /// <summary>
        /// 记录新增操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">新增实体</param>
        /// <param name="dbKey">数据库标识</param>
        public void LogAdd<T>(T entity, string dbKey = null) where T : class, new()
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Action = "INSERT",
                EntityType = typeof(T).Name,
                Changes = GetEntityChanges(entity),
                DbKey = dbKey
            };
            Log(entry);
        }

        /// <summary>
        /// 记录更新操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="oldEntity">更新前实体</param>
        /// <param name="newEntity">更新后实体</param>
        /// <param name="dbKey">数据库标识</param>
        public void LogUpdate<T>(T oldEntity, T newEntity, string dbKey = null) where T : class, new()
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Action = "UPDATE",
                EntityType = typeof(T).Name,
                Changes = GetEntityDiff(oldEntity, newEntity),
                DbKey = dbKey
            };
            Log(entry);
        }

        /// <summary>
        /// 记录删除操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">删除实体</param>
        /// <param name="dbKey">数据库标识</param>
        public void LogDelete<T>(T entity, string dbKey = null) where T : class, new()
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Action = "DELETE",
                EntityType = typeof(T).Name,
                Changes = GetEntityChanges(entity),
                DbKey = dbKey
            };
            Log(entry);
        }

        private string GetEntityChanges<T>(T entity) where T : class, new()
        {
            var props = PropertyCache.GetPropertiesCached<T>();
            var changes = props.Select(p => string.Format("{0}={1}", p.Name, p.GetValue(entity)));
            return string.Join(", ", changes);
        }

        private string GetEntityDiff<T>(T oldEntity, T newEntity) where T : class, new()
        {
            var props = PropertyCache.GetPropertiesCached<T>();
            var changes = new List<string>();

            foreach (var prop in props)
            {
                var oldValue = prop.GetValue(oldEntity);
                var newValue = prop.GetValue(newEntity);

                if (!Equals(oldValue, newValue))
                {
                    changes.Add(string.Format("{0}: {1} -> {2}", prop.Name, oldValue, newValue));
                }
            }

            return changes.Any() ? string.Join(", ", changes) : "无变更";
        }
    }

    /// <summary>
    /// 数据库审计日志记录器
    /// </summary>
    public class DatabaseAuditLogger : IAuditLogger
    {
        private readonly string _dbKey;

        /// <summary>
        /// 初始化数据库审计日志记录器
        /// </summary>
        /// <param name="dbKey">数据库标识</param>
        public DatabaseAuditLogger(string dbKey)
        {
            _dbKey = dbKey;
        }

        /// <summary>
        /// 将审计日志条目写入数据库
        /// </summary>
        /// <param name="entry">审计日志条目</param>
        public void Log(AuditEntry entry)
        {
            try
            {
                using var db = new DataContext(_dbKey);
                db.cmd.CommandText = @"
                    INSERT INTO AuditLog (Timestamp, Action, EntityType, PrimaryKey, User, Changes, DbKey)
                    VALUES (@Timestamp, @Action, @EntityType, @PrimaryKey, @User, @Changes, @DbKey)";
                db.cmd.Parameters.Clear();
                DevToolsParamHelper.AddWithValue(db.cmd, "@Timestamp", entry.Timestamp);
                DevToolsParamHelper.AddWithValue(db.cmd, "@Action", entry.Action);
                DevToolsParamHelper.AddWithValue(db.cmd, "@EntityType", entry.EntityType);
                DevToolsParamHelper.AddWithValue(db.cmd, "@PrimaryKey", entry.PrimaryKey ?? "");
                DevToolsParamHelper.AddWithValue(db.cmd, "@User", entry.User ?? "System");
                DevToolsParamHelper.AddWithValue(db.cmd, "@Changes", entry.Changes ?? "");
                DevToolsParamHelper.AddWithValue(db.cmd, "@DbKey", entry.DbKey ?? "");
                db.cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("审计日志记录失败: {0}", ex.Message));
            }
        }

        /// <summary>
        /// 记录新增操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">新增实体</param>
        /// <param name="dbKey">数据库标识</param>
        public void LogAdd<T>(T entity, string dbKey = null) where T : class, new()
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Action = "INSERT",
                EntityType = typeof(T).Name,
                Changes = GetEntityChanges(entity),
                DbKey = dbKey
            };
            Log(entry);
        }

        /// <summary>
        /// 记录更新操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="oldEntity">更新前实体</param>
        /// <param name="newEntity">更新后实体</param>
        /// <param name="dbKey">数据库标识</param>
        public void LogUpdate<T>(T oldEntity, T newEntity, string dbKey = null) where T : class, new()
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Action = "UPDATE",
                EntityType = typeof(T).Name,
                Changes = GetEntityDiff(oldEntity, newEntity),
                DbKey = dbKey
            };
            Log(entry);
        }

        /// <summary>
        /// 记录删除操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">删除实体</param>
        /// <param name="dbKey">数据库标识</param>
        public void LogDelete<T>(T entity, string dbKey = null) where T : class, new()
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Action = "DELETE",
                EntityType = typeof(T).Name,
                Changes = GetEntityChanges(entity),
                DbKey = dbKey
            };
            Log(entry);
        }

        private string GetEntityChanges<T>(T entity) where T : class, new()
        {
            var props = PropertyCache.GetPropertiesCached<T>();
            var changes = props.Select(p => string.Format("{0}={1}", p.Name, p.GetValue(entity)));
            return string.Join(", ", changes);
        }

        private string GetEntityDiff<T>(T oldEntity, T newEntity) where T : class, new()
        {
            var props = PropertyCache.GetPropertiesCached<T>();
            var changes = new List<string>();

            foreach (var prop in props)
            {
                var oldValue = prop.GetValue(oldEntity);
                var newValue = prop.GetValue(newEntity);

                if (!Equals(oldValue, newValue))
                {
                    changes.Add(string.Format("{0}: {1} -> {2}", prop.Name, oldValue, newValue));
                }
            }

            return changes.Any() ? string.Join(", ", changes) : "无变更";
        }
    }

    /// <summary>
    /// 审计日志条目
    /// </summary>
    public class AuditEntry
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
        /// <summary>
        /// 操作动作
        /// </summary>
        public string Action { get; set; }
        /// <summary>
        /// 实体类型名称
        /// </summary>
        public string EntityType { get; set; }
        /// <summary>
        /// 主键值
        /// </summary>
        public string PrimaryKey { get; set; }
        /// <summary>
        /// 操作人
        /// </summary>
        public string User { get; set; }
        /// <summary>
        /// 变更内容
        /// </summary>
        public string Changes { get; set; }
        /// <summary>
        /// 数据库标识
        /// </summary>
        public string DbKey { get; set; }
    }

    /// <summary>
    /// 审计装饰器
    /// </summary>
    public static class AuditDecorator
    {
        private static IAuditLogger _logger = new ConsoleAuditLogger();

        /// <summary>
        /// 设置全局审计日志记录器
        /// </summary>
        /// <param name="logger">审计日志记录器</param>
        public static void SetLogger(IAuditLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 新增实体并记录审计日志
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">新增实体</param>
        /// <param name="dbKey">数据库标识</param>
        /// <returns>写入结果</returns>
        public static WriteReturn AddWithAudit<T>(T entity, string dbKey = null) where T : class, new()
        {
            var result = FastData.FastWrite.Add<T>(entity, null, dbKey);
            if (result.IsSuccess)
            {
                _logger.LogAdd(entity, dbKey);
            }
            return result;
        }

        /// <summary>
        /// 更新实体并记录审计日志
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="oldEntity">更新前实体</param>
        /// <param name="newEntity">更新后实体</param>
        /// <param name="dbKey">数据库标识</param>
        /// <returns>写入结果</returns>
        public static WriteReturn UpdateWithAudit<T>(T oldEntity, T newEntity, string dbKey = null) where T : class, new()
        {
            var result = FastData.FastWrite.Update<T>(newEntity, null, null, dbKey);
            if (result.IsSuccess)
            {
                _logger.LogUpdate(oldEntity, newEntity, dbKey);
            }
            return result;
        }

        /// <summary>
        /// 删除实体并记录审计日志
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entity">删除实体</param>
        /// <param name="dbKey">数据库标识</param>
        /// <returns>写入结果</returns>
        public static WriteReturn DeleteWithAudit<T>(T entity, string dbKey = null) where T : class, new()
        {
            var result = FastData.FastWrite.Delete<T>(entity, null, dbKey);
            if (result.IsSuccess)
            {
                _logger.LogDelete(entity, dbKey);
            }
            return result;
        }

        /// <summary>
        /// 批量新增实体并记录审计日志
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entities">新增实体列表</param>
        /// <param name="dbKey">数据库标识</param>
        /// <returns>写入结果</returns>
        public static WriteReturn AddRangeWithAudit<T>(IEnumerable<T> entities, string dbKey = null) where T : class, new()
        {
            var result = FastData.FastWrite.AddList<T>(entities.ToList(), dbKey);
            if (result.IsSuccess)
            {
                foreach (var entity in entities)
                {
                    _logger.LogAdd(entity, dbKey);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 审计日志查询器
    /// </summary>
    public static class AuditLogQuery
    {
        /// <summary>
        /// 查询审计日志
        /// </summary>
        /// <param name="startDate">起始时间</param>
        /// <param name="endDate">结束时间</param>
        /// <param name="action">操作动作</param>
        /// <param name="entityType">实体类型名称</param>
        /// <param name="user">操作人</param>
        /// <param name="dbKey">数据库标识</param>
        /// <returns>审计日志列表</returns>
        public static List<AuditEntry> QueryLogs(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string action = null,
            string entityType = null,
            string user = null,
            string dbKey = null)
        {
            var logs = new List<AuditEntry>();

            try
            {
                using var db = new DataContext(dbKey);
                var sql = "SELECT * FROM AuditLog WHERE 1=1";
                var conditions = new List<string>();

                if (startDate.HasValue)
                {
                    conditions.Add("Timestamp >= @StartDate");
                    DevToolsParamHelper.AddWithValue(db.cmd, "@StartDate", startDate.Value);
                }

                if (endDate.HasValue)
                {
                    conditions.Add("Timestamp <= @EndDate");
                    DevToolsParamHelper.AddWithValue(db.cmd, "@EndDate", endDate.Value);
                }

                if (!string.IsNullOrEmpty(action))
                {
                    conditions.Add("Action = @Action");
                    DevToolsParamHelper.AddWithValue(db.cmd, "@Action", action);
                }

                if (!string.IsNullOrEmpty(entityType))
                {
                    conditions.Add("EntityType = @EntityType");
                    DevToolsParamHelper.AddWithValue(db.cmd, "@EntityType", entityType);
                }

                if (!string.IsNullOrEmpty(user))
                {
                    conditions.Add("User = @User");
                    DevToolsParamHelper.AddWithValue(db.cmd, "@User", user);
                }

                if (conditions.Any())
                {
                    sql += " AND " + string.Join(" AND ", conditions);
                }

                sql += " ORDER BY Timestamp DESC";

                db.cmd.CommandText = sql;
                using var reader = db.cmd.ExecuteReader();

                while (reader.Read())
                {
                    logs.Add(new AuditEntry
                    {
                        Timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp")),
                        Action = reader.GetString(reader.GetOrdinal("Action")),
                        EntityType = reader.GetString(reader.GetOrdinal("EntityType")),
                        PrimaryKey = reader.IsDBNull(reader.GetOrdinal("PrimaryKey")) ? null : reader.GetString(reader.GetOrdinal("PrimaryKey")),
                        User = reader.IsDBNull(reader.GetOrdinal("User")) ? null : reader.GetString(reader.GetOrdinal("User")),
                        Changes = reader.IsDBNull(reader.GetOrdinal("Changes")) ? null : reader.GetString(reader.GetOrdinal("Changes")),
                        DbKey = reader.IsDBNull(reader.GetOrdinal("DbKey")) ? null : reader.GetString(reader.GetOrdinal("DbKey"))
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("查询审计日志失败: {0}", ex.Message));
            }

            return logs;
        }

        /// <summary>
        /// 初始化审计日志表
        /// </summary>
        /// <param name="dbKey">数据库标识</param>
        public static void InitializeAuditLogTable(string dbKey)
        {
            try
            {
                using var db = new DataContext(dbKey);
                db.cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AuditLog (
                        Id INT PRIMARY KEY AUTO_INCREMENT,
                        Timestamp DATETIME NOT NULL,
                        Action VARCHAR(50) NOT NULL,
                        EntityType VARCHAR(100) NOT NULL,
                        PrimaryKey VARCHAR(200),
                        User VARCHAR(100),
                        Changes TEXT,
                        DbKey VARCHAR(50),
                        INDEX idx_timestamp (Timestamp),
                        INDEX idx_action (Action),
                        INDEX idx_entity (EntityType),
                        INDEX idx_user (User)
                    )";
                db.cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("初始化审计日志表失败: {0}", ex.Message));
            }
        }
    }
}