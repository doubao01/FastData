// 逐字照抄 FastData/README.md 的代码示例，只补 using、实体定义与命名空间包装。
// 目的：证明“照着文档写能通过编译”。任何一行改写的 API 都要在这里同步。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FastData;
using FastData.Aop;
using FastData.Base;
using FastData.Model;
using FastData.Property;
using FastData.Queue;
using FastData.Sharding;
using FastRedis;
using FastUntility.Page;
using Microsoft.Data.Sqlite;

namespace DocSamples
{
    // ===== BLOCK 10：实体定义（README 原样） =====
    [Table(Name = "Users")]
    [Cache(IsEnable = true, ExpireTime = 300, Key = "user:{Id}", CacheType = "Redis")]
    public class User
    {
        [Column(IsIdentity = true), Primary]
        public long Id { get; set; }

        [Column(Length = 100, IsNull = false, Comments = "用户名")]
        public string UserName { get; set; }

        public int Age { get; set; }
        public bool IsActive { get; set; }
        public int Status { get; set; }
        public DateTime CreateTime { get; set; }
    }

    public class Order
    {
        public long Id { get; set; }
        public string OrderNo { get; set; }
        public string Status { get; set; }
        public DateTime CreateTime { get; set; }
    }

    public class UserLog
    {
        public long Id { get; set; }
        public DateTime CreateTime { get; set; }
    }

    public static class Samples
    {
        public static async Task Block0_Init()
        {
            FastMap.InstanceMap();                                    // 加载数据源与 Map
            FastMap.InstanceProperties(nameSpace: "YourApp.Models");   // 扫描实体特性
        }

        public static void Block1_Crud()
        {
            var db = new FastDataClient("DefaultDb");

            var users = db.Query<User>(u => u.IsActive)
                          .Where(u => u.Age > 18)
                          .OrderBy(u => u.CreateTime)        // 升序
                          .Take(10)
                          .ToList<User>();

            db.Add(new User { UserName = "张三", Age = 30 });
            db.Update(new User { UserName = "李四" }, u => u.Id == 1);
            db.Delete<User>(u => u.Id == 1);
        }

        public static async Task Block2_CodeFirst()
        {
            FastWrite.CodeFirst<User>(key: "DefaultDb", isDropExists: false);
            await FastWrite.CodeFirstAsync<User>(key: "DefaultDb", isDropExists: false);
        }

        public static void Block3_Paging()
        {
            var db = new FastDataClient("DefaultDb");

            var page = db.Query<User>(u => u.IsActive)
                         .OrderBy(u => u.Id)
                         .ToPage<User>(new PageModel { PageId = 1, PageSize = 10 });

            var count = db.Query<User>(u => u.Age > 18).ToCount();
            var top20 = db.Query<User>(u => u.IsActive).Take(20).ToList<User>();
        }

        public static void Block4_Condition()
        {
            var config = ConfigModel();
            var sql = new ConditionBuilder(config)
                .Equal<User>(u => u.Age, 18).And()
                .Contains<User>(u => u.UserName, "张").Or()
                .In<User>(u => u.Status, new object[] { 1, 2, 3 })
                .Build(out var parameters);
        }

        public static void Block5_MapQuery()
        {
            var db = new FastDataClient("DefaultDb");
            FastMap.InstanceMap(dbKey: "DefaultDb", mapFile: "SqlMap.config");

            var users = db.MapQuery<User>("GetActiveUsers", new System.Data.Common.DbParameter[]
            {
                new SqliteParameter("@IsActive", true),
                new SqliteParameter("@MinAge", 18)
            });
        }

        public static void Block6_Sharding()
        {
            ShardingManager.Configure<UserLog>(new ShardingConfig
            {
                BaseTableName = "UserLog",
                ShardingType = ShardingType.Time,
                TimeConfig = new TimeShardingConfig
                {
                    TimeField = "CreateTime",
                    Granularity = TimeGranularity.Month
                }
            });

            var db = new FastDataClient("DefaultDb");
            var order = new Order();

            var recent = db.ShardQuery<Order>(
                o => o.CreateTime > DateTime.Now.AddMonths(-3),
                queryParams: new Dictionary<string, object> { { "CreateTime", DateTime.Now.AddMonths(-3) } });

            db.ShardAdd(order);
            db.ShardUpdate(new Order { OrderNo = order.OrderNo, Status = "paid" },
                o => o.OrderNo == order.OrderNo,
                o => new { o.Status });
        }

        public static void Block7_Aop()
        {
            FastMap.InstanceMap(aop: new LoggingAop());
        }

        public static void Block8_Redis(User user)
        {
            RedisInfo.Set("user:1", user, hours: 300);   // hours 参数，默认 12 个月
            var u = RedisInfo.Get<User>("user:1");
        }

        public static void Block9_Queue(Order order1, Order order2)
        {
            FastWrite.ConfigureQueue<Order>(new WriteBehindConfig
            {
                QueueType = WriteBehindQueueType.ReliableQueue,
                Topic = "orders",
                EnableFallback = true,
                EnableAutoRecovery = true
            });

            var db = new FastDataClient("DefaultDb");
            var result = db.WriteQueue().Add(order1).Add(order2).Execute();   // 返回 WriteBehindResult
        }

        // 只为了让 Block4 编译；真实用法从配置文件读
        private static ConfigModel ConfigModel() => null;
    }

    // ===== BLOCK 7：AOP 实现（README 原样） =====
    public class LoggingAop : IFastAop
    {
        public void Before(BeforeContext context)         => Console.WriteLine($"执行: {context.sql}");
        public void After(AfterContext context)           => Console.WriteLine($"完成: {context.sql}");
        public void Exception(ExceptionContext context)   => Console.WriteLine($"异常: {context.ex.Message}");
        public void MapBefore(MapBeforeContext context)   { }
        public void MapAfter(MapAfterContext context)     { }
    }
}