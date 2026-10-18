using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FastData.Sharding.Strategies
{
    /// <summary>
    /// 哈希分表策略
    /// 适用场景：有业务唯一键但无自增ID的表
    /// </summary>
    public class HashShardingStrategy : IShardingStrategy
    {
        /// <summary>
        /// 获取分片策略的名称
        /// </summary>
        public string Name => "HashSharding";

        /// <summary>
        /// 获取分片策略的类型
        /// </summary>
        public ShardingType Type => ShardingType.Hash;

        /// <summary>
        /// 根据实体获取分表名称
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="entity">实体对象</param>
        /// <returns>表名</returns>
        public string GetTableName(ShardingConfig config, object entity)
        {
            if (config.HashConfig == null)
                throw new InvalidOperationException("哈希分表配置不能为空");

            var hashField = config.HashConfig.HashField;
            var entityType = entity.GetType();
            var property = entityType.GetProperty(hashField);

            if (property == null)
                throw new InvalidOperationException(string.Format("实体类型 {0} 中找不到字段 {1}", entityType.Name, hashField));

            var hashValue = property.GetValue(entity)?.ToString();
            if (string.IsNullOrEmpty(hashValue))
                throw new InvalidOperationException(string.Format("字段 {0} 的值不能为空", hashField));

            return ShardingNameResolver.ResolveHash(config, hashValue);
        }

        /// <summary>
        /// 根据查询条件获取所有可能的分表名称
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="queryParams">查询参数</param>
        /// <returns>表名列表</returns>
        public List<string> GetTableNames(ShardingConfig config, Dictionary<string, object> queryParams)
        {
            var result = new List<string>();

            if (config.HashConfig == null)
                return result;

            var hashField = config.HashConfig.HashField;

            // 如果查询条件包含哈希字段，直接定位到分表
            if (queryParams.ContainsKey(hashField))
            {
                var hashValue = queryParams[hashField]?.ToString();
                if (!string.IsNullOrEmpty(hashValue))
                {
                    result.Add(ShardingNameResolver.ResolveHash(config, hashValue));
                    return result;
                }
            }

            // 没有哈希字段条件，返回所有分表
            return GetAllTableNames(config);
        }

        /// <summary>
        /// 获取所有分表名称
        /// </summary>
        public List<string> GetAllTableNames(ShardingConfig config)
        {
            var result = new List<string>();

            if (config.HashConfig == null)
                return result;

            for (int i = 0; i < config.HashConfig.ShardCount; i++)
            {
                result.Add(string.Format("{0}_{1:D4}", config.BaseTableName, i));
            }

            return result;
        }

        /// <summary>
        /// 创建分表
        /// </summary>
        /// <param name="config">分片配置</param>
        /// <param name="tableName">表名</param>
        /// <returns>是否成功</returns>
        public bool CreateTable(ShardingConfig config, string tableName)
        {
            return true;
        }

        #region 私有方法

        #endregion
    }

    /// <summary>
    /// CRC32 哈希算法实现
    /// </summary>
    internal class Crc32
    {
        private static readonly uint[] Table = new uint[256];
        private const uint Polynomial = 0xEDB88320;

        static Crc32()
        {
            for (uint i = 0; i < 256; i++)
            {
                var crc = i;
                for (int j = 0; j < 8; j++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
                }
                Table[i] = crc;
            }
        }

        public byte[] ComputeHash(byte[] bytes)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var b in bytes)
            {
                crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
            }
            crc ^= 0xFFFFFFFF;
            return BitConverter.GetBytes(crc);
        }
    }
}
