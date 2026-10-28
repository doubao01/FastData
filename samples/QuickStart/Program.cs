using System;
using FastData;
using FastData.Context;
using FastData.Property;
using FastUntility.Page;

// 1. 实体：用特性把它映射到 users 表
[Table(Name = "users")]
public class User
{
    [Column(IsKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public string Name { get; set; }

    public int Age { get; set; }
}

internal static class Program
{
    private const string Key = "Sqlite";

    private static int Main()
    {
        Console.WriteLine("FastData QuickStart\n");

        CreateTable();

        Insert();
        Query();
        PageQuery();
        Update();
        Delete();

        Console.WriteLine("\n全部步骤完成。数据库文件：quickstart.db");
        return 0;
    }

    /// <summary>
    /// 建表。真实项目里表通常已由 DBA 建好，这里自包含是为了让示例能直接跑。
    /// </summary>
    private static void CreateTable()
    {
        using (var db = new DataContext(Key))
        {
            // DataContext 构造后连接是关闭的，直接执行命令会抛
            // "ExecuteNonQuery can only be called when the connection is open."
            db.EnsureConnectionOpen();

            db.cmd.CommandText = @"CREATE TABLE IF NOT EXISTS users (
                Id   INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT,
                Age  INTEGER)";
            db.cmd.ExecuteNonQuery();
        }

        Console.WriteLine("[1] 建表 users 完成");
    }

    private static void Insert()
    {
        var first = FastWrite.Add(new User { Name = "张三", Age = 28 }, key: Key);
        Console.WriteLine("[2] 插入 张三：" + (first.IsSuccess ? "成功" : "失败 - " + first.Message));

        if (!first.IsSuccess)
            throw new InvalidOperationException("插入失败，后续步骤无意义：" + first.Message);

        var second = FastWrite.Add(new User { Name = "李四", Age = 16 }, key: Key);
        Console.WriteLine("    插入 李四：" + (second.IsSuccess ? "成功" : "失败 - " + second.Message));
    }

    /// <summary>
    /// Lambda 查询：强类型，条件写错编译期就能发现。
    /// </summary>
    private static void Query()
    {
        var adults = FastRead.Query<User>(u => u.Age > 18, key: Key)
            .OrderBy<User>(u => u.Id)
            .ToList<User>();

        Console.WriteLine("[3] 查询 Age > 18 的用户：" + adults.Count + " 条");
        foreach (var user in adults)
            Console.WriteLine($"      Id={user.Id} Name={user.Name} Age={user.Age}");
    }

    private static void PageQuery()
    {
        var page = FastRead.Query<User>(u => u.Age > 0, key: Key)
            .OrderBy<User>(u => u.Id)
            .ToPage<User>(new PageModel { PageId = 1, PageSize = 10 });

        Console.WriteLine($"[4] 分页：总计 {page.pModel.TotalRecord} 条，本页 {page.list.Count} 条");
    }

    private static void Update()
    {
        // 只更新 Name 一个字段：不传 fieldSelector 会把未赋值字段一并写回
        var result = FastWrite.Update(
            new User { Name = "张三（已改名）" },
            u => u.Name == "张三",
            u => new { u.Name },
            key: Key);

        Console.WriteLine("[5] 更新 张三 的 Name：" + (result.IsSuccess ? "成功" : "失败 - " + result.Message));
    }

    private static void Delete()
    {
        var result = FastWrite.Delete<User>(u => u.Age > 0, key: Key);
        Console.WriteLine("[6] 删除全部测试数据：" + (result.IsSuccess ? "成功" : "失败 - " + result.Message));
    }
}