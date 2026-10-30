#if (NET8_0_OR_GREATER || NETCOREAPP)
using FastData;
using FastData.ConnectionPool;
using FastUntility.Monitor;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// QQ 机器人指令注册与 dbrestart 语义测试。
    /// 覆盖两件事：
    /// 1. FastIM 门面必须注册全部内置指令（此前只注册 4 个，其余 8 个报「未知指令」）；
    /// 2. dbrestart 只有在提供者支持重建时才报告「已重启」，否则必须如实说明仅关闭、不预热。
    /// </summary>
    public class FastIMCommandRegistrationTests
    {
        [Fact]
        public void Configure_ShouldRegisterAllBuiltInCommands()
        {
            FastIM.Configure(CreateBotConfig(), new ExceptionNotifyConfig(), new FakeMessageSender(), null);

            var registered = FastIM.Commands.GetHandlers().Keys
                .Select(x => x.ToLowerInvariant())
                .OrderBy(x => x)
                .ToArray();

            var expected = new[]
            {
                "cpu", "dbclose", "dbrestart", "dbstatus", "disk",
                "gc", "help", "memory", "process", "status", "time", "version"
            }.OrderBy(x => x).ToArray();

            Assert.Equal(expected, registered);
        }

        [Fact]
        public void DbRestart_ShouldReopenWhenProviderSupportsIt()
        {
            var provider = new ReopenablePoolProvider();
            var sender = new FakeMessageSender();
            var manager = new RemoteCommandManager(CreateBotConfig(), sender, provider);
            manager.RegisterHandler(new DbRestartCommandHandler(manager));

            var response = manager.ExecuteCommand(new RemoteCommandRequest
            {
                Command = "dbrestart",
                Args = new[] { "main" },
                SenderQQ = "10001"
            });

            Assert.True(response.Success, response.Message);
            Assert.Equal("main", provider.ReopenedPool);
            Assert.Contains("已重启", response.Message);
        }

        [Fact]
        public void DbRestart_ShouldNotClaimRestartWhenProviderCannotReopen()
        {
            var provider = new CloseOnlyPoolProvider();
            var sender = new FakeMessageSender();
            var manager = new RemoteCommandManager(CreateBotConfig(), sender, provider);
            manager.RegisterHandler(new DbRestartCommandHandler(manager));

            var response = manager.ExecuteCommand(new RemoteCommandRequest
            {
                Command = "dbrestart",
                Args = new[] { "main" },
                SenderQQ = "10001"
            });

            Assert.True(response.Success);
            Assert.Equal("main", provider.ClosedPool);
            // 关键断言：不能把「关闭」说成「已重启」
            Assert.DoesNotContain("已重启", response.Message);
            Assert.Contains("按需重建", response.Message);
        }

        [Fact]
        public void ConnectionPoolFactory_ShouldExposePoolInfoAndReopenInterface()
        {
            var factory = ConnectionPoolFactory.Instance;

            Assert.IsAssignableFrom<IConnectionPoolInfoProvider>(factory);
            Assert.IsAssignableFrom<IConnectionPoolReopenProvider>(factory);

            // 无池时应返回空字典而不是抛异常
            Assert.NotNull(factory.GetAllPoolInfo());
        }

        [Fact]
        public void ReopenUnknownPool_ShouldFailWithReason()
        {
            var factory = ConnectionPoolFactory.Instance;

            string message;
            var ok = factory.TryReopenPool("pool-that-does-not-exist", out message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        private static QQBotConfig CreateBotConfig()
        {
            return new QQBotConfig
            {
                IsEnabled = true,
                BotId = "bot-test",
                BotToken = "token-test",
                ApiUrl = "http://127.0.0.1:5700",
                CommandPrefix = "#",
                RequireAdminForCommands = true,
                AdminQQNumbers = new List<string> { "10001" }
            };
        }

        private class FakeMessageSender : IMessageSender
        {
            public void SendPrivateMessage(string qqNumber, string message) { }
            public void SendGroupMessage(string groupId, string message) { }
        }

        /// <summary>
        /// 只支持关闭，不支持重建 —— 模拟旧实现或第三方提供者。
        /// </summary>
        private class CloseOnlyPoolProvider : IConnectionPoolInfoProvider
        {
            public string ClosedPool { get; private set; }

            public Dictionary<string, ConnectionPoolInfo> GetAllPoolInfo()
            {
                return new Dictionary<string, ConnectionPoolInfo>();
            }

            public void ClosePool(string poolName)
            {
                ClosedPool = poolName;
            }
        }

        /// <summary>
        /// 支持真正的关闭 + 重建 + 预热。
        /// </summary>
        private class ReopenablePoolProvider : IConnectionPoolInfoProvider, IConnectionPoolReopenProvider
        {
            public string ReopenedPool { get; private set; }

            public Dictionary<string, ConnectionPoolInfo> GetAllPoolInfo()
            {
                return new Dictionary<string, ConnectionPoolInfo>();
            }

            public void ClosePool(string poolName) { }

            public bool TryReopenPool(string poolName, out string message)
            {
                ReopenedPool = poolName;
                message = string.Format("已重启连接池 {0}，预热 2 个连接", poolName);
                return true;
            }
        }
    }
}
#endif