using FastData.Config;
// using FastData.DevTools;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.IO;
using System.Reflection;

namespace FastData.Demo.Controllers
{
    /// <summary>
    /// 配置信息 API（只读，密码脱敏）
    /// </summary>
    [ApiController]
    [Route("api/config")]
    public class ConfigController : ControllerBase
    {
        /// <summary>
        /// 获取当前活跃环境名称
        /// </summary>
        [HttpGet("environment")]
        public ActionResult<object> GetEnvironment()
        {
            var active = FastDataConfig.GetActiveEnvironment();
            var envOverride = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FASTDATA_ACTIVE"));
            return Ok(new
            {
                active = active,
                envVarOverride = envOverride
            });
        }

        /// <summary>
        /// 调试：获取配置加载详细信息
        /// </summary>
        [HttpGet("debug")]
        public ActionResult<object> GetDebug()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dbConfig = System.IO.Path.Combine(baseDir, "db.config");
            var dbDevConfig = System.IO.Path.Combine(baseDir, "db.dev.config");
            
            return Ok(new
            {
                baseDirectory = baseDir,
                dbConfigExists = System.IO.File.Exists(dbConfig),
                dbDevConfigExists = System.IO.File.Exists(dbDevConfig),
                currentDirectory = Environment.CurrentDirectory,
                active = FastDataConfig.GetActiveEnvironment()
            });
        }

        /// <summary>
        /// 获取所有数据库连接配置（密码脱敏）
        /// </summary>
        [HttpGet("connections")]
        public ActionResult<object> GetConnections()
        {
            var list = FastDataConfig.GetConnectionSummaries();
            return Ok(list);
        }

        /// <summary>
        /// 获取指定数据库连接配置（密码脱敏）
        /// </summary>
        [HttpGet("connections/{key}")]
        public ActionResult<object> GetConnection(string key)
        {
            var config = FastDataConfig.GetConnectionSummary(key);
            if (config == null)
                return NotFound(new { error = $"Connection key '{key}' not found" });

            return Ok(config);
        }

        /// <summary>
        /// 获取 Redis 配置（密码脱敏）
        /// </summary>
        [HttpGet("redis")]
        public ActionResult<object> GetRedis()
        {
            var redis = FastDataConfig.GetRedisSummary();
            return Ok(redis);
        }

        /// <summary>
        /// 获取完整配置概览
        /// </summary>
        [HttpGet("summary")]
        public ActionResult<object> GetSummary()
        {
            var active = FastDataConfig.GetActiveEnvironment();
            var connections = FastDataConfig.GetConnectionSummaries();
            var redis = FastDataConfig.GetRedisSummary();

            return Ok(new
            {
                environment = new { active },
                database = new
                {
                    count = connections.Count,
                    connections
                },
                redis
            });
        }

        /// <summary>
        /// 数据库诊断（暂时不可用）
        /// </summary>
        [HttpGet("diagnose")]
        public ActionResult<object> DiagnoseDatabase([FromQuery] string key = null)
        {
            // var result = DatabaseDiagnostic.Diagnose(key);
            // return Ok(new
            // {
            //     isHealthy = result.IsHealthy,
            //     metrics = result.Metrics,
            //     issues = result.Issues.Select(i => new
            //     {
            //         severity = i.Severity.ToString(),
            //         category = i.Category,
            //         message = i.Message
            //     })
            // });

            return Ok(new { note = "Database diagnostic not available", key });
        }

        /// <summary>
        /// 缓存管理：获取缓存统计（暂时不可用）
        /// </summary>
        [HttpGet("cache/stats")]
        public ActionResult<object> GetCacheStats()
        {
            // try
            // {
            //     var count = CacheManager.GetAllCacheKeys()?.Count ?? 0;
            //     return Ok(new { cacheItemCount = count });
            // }
            // catch
            // {
                return Ok(new { cacheItemCount = 0, note = "CacheManager not available" });
            // }
        }

        /// <summary>
        /// 缓存管理：清除指定缓存键（暂时不可用）
        /// </summary>
        [HttpDelete("cache/{key}")]
        public ActionResult<object> RemoveCache(string key)
        {
            // try
            // {
            //     CacheManager.Remove(key);
            //     return Ok(new { message = $"Cache key '{key}' removed successfully" });
            // }
            // catch (Exception ex)
            // {
            //     return StatusCode(500, new { error = ex.Message });
            // }

            return Ok(new { note = "Cache management not available", key });
        }

        /// <summary>
        /// 缓存管理：清空所有缓存（暂时不可用）
        /// </summary>
        [HttpDelete("cache")]
        public ActionResult<object> ClearCache()
        {
            // try
            // {
            //     CacheManager.Clear();
            //     return Ok(new { message = "All cache cleared successfully" });
            // }
            // catch (Exception ex)
            // {
            //     return StatusCode(500, new { error = ex.Message });
            // }

            return Ok(new { note = "Cache management not available" });
        }
    }
}
