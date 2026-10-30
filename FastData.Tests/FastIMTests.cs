#if (NET8_0_OR_GREATER || NETCOREAPP)
using FastData;
using FastUntility.Monitor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace FastData.Tests
{
    /// <summary>
    /// FastIM QQ 机器人门面测试。
    /// </summary>
    [Collection(FastIMCollectionNames.StaticState)]
    public class FastIMTests
    {
        [Fact]
        public void Configure_ShouldSendPrivateAndGroupMessages()
        {
            var sender = new FakeMessageSender();
            var config = CreateBotConfig();

            FastIM.Configure(config, CreateNotifyConfig(), sender);

            FastIM.SendPrivateMessage("10001", "hello-private");
            FastIM.SendGroupMessage("20001", "hello-group");

            Assert.Contains(sender.PrivateMessages, x => x.Target == "10001" && x.Message == "hello-private");
            Assert.Contains(sender.GroupMessages, x => x.Target == "20001" && x.Message == "hello-group");
            Assert.Same(config, FastIM.BotConfig);
            Assert.NotNull(FastIM.Commands);
        }

        [Fact]
        public void NotifyException_ShouldSendToConfiguredTargets()
        {
            var sender = new FakeMessageSender();
            FastIM.Configure(CreateBotConfig(), CreateNotifyConfig(), sender);

            FastIM.NotifyException(new ExceptionInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                Level = ExceptionLevel.Error,
                Message = "database failed",
                Source = "unit-test"
            });

            Assert.Contains(sender.GroupMessages, x => x.Target == "20001" && x.Message.Contains("database failed"));
            Assert.Contains(sender.PrivateMessages, x => x.Target == "10001" && x.Message.Contains("database failed"));
            Assert.Contains(sender.PrivateMessages, x => x.Target == "10002" && x.Message.Contains("database failed"));
        }

        [Fact]
        public void ProcessMessage_ShouldExecuteBuiltInCommand()
        {
            var sender = new FakeMessageSender();
            FastIM.Configure(CreateBotConfig(), CreateNotifyConfig(), sender);

            FastIM.ProcessMessage("10001", "20001", "#help");

            Assert.Contains(sender.GroupMessages, x => x.Target == "20001" && x.Message.Contains("help"));
        }

        [Fact]
        public void Load_ShouldParseQQBotConfigFile()
        {
            var sender = new FakeMessageSender();
            var configPath = Path.Combine(Path.GetTempPath(), "fastdata-im-" + Guid.NewGuid().ToString("N") + ".config");

            try
            {
                File.WriteAllText(configPath,
@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Config>
  <IMPlatform>
    <QQBot IsEnabled=""true"" BotId=""bot-1"" BotToken=""token-1"" ApiUrl=""http://127.0.0.1:5700"" CommandPrefix=""!"" RequireAdminForCommands=""true"">
      <AdminQQNumbers>
        <Add Value=""30001"" />
      </AdminQQNumbers>
      <NotifyGroups>
        <Add Value=""40001"" />
      </NotifyGroups>
      <NotifyUsers>
        <Add Value=""50001"" />
      </NotifyUsers>
    </QQBot>
    <ExceptionNotify IsEnabled=""true"" MinNotifyIntervalSeconds=""0"" SendStackTrace=""false"" MaxStackTraceLength=""100"" MinLevel=""Info"" />
  </IMPlatform>
</Config>");

                var config = FastIM.Load(configPath, sender);

                Assert.True(config.IsEnabled);
                Assert.Equal("bot-1", config.BotId);
                Assert.Equal("!", config.CommandPrefix);
                Assert.Equal("30001", config.AdminQQNumbers.Single());
                Assert.Equal("40001", config.NotifyGroups.Single());
                Assert.Equal("50001", config.NotifyUsers.Single());

                FastIM.NotifyText("loaded-message", ExceptionLevel.Info, "load-test");

                Assert.Contains(sender.GroupMessages, x => x.Target == "40001" && x.Message.Contains("loaded-message"));
                Assert.Contains(sender.PrivateMessages, x => x.Target == "30001" && x.Message.Contains("loaded-message"));
                Assert.Contains(sender.PrivateMessages, x => x.Target == "50001" && x.Message.Contains("loaded-message"));
            }
            finally
            {
                if (File.Exists(configPath))
                    File.Delete(configPath);
            }
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
                AdminQQNumbers = new List<string> { "10001" },
                NotifyGroups = new List<string> { "20001" },
                NotifyUsers = new List<string> { "10002" }
            };
        }

        private static ExceptionNotifyConfig CreateNotifyConfig()
        {
            return new ExceptionNotifyConfig
            {
                IsEnabled = true,
                MinNotifyIntervalSeconds = 0,
                SendStackTrace = true,
                MaxStackTraceLength = 200,
                MinLevel = ExceptionLevel.Info
            };
        }

        private class FakeMessageSender : IMessageSender
        {
            public List<(string Target, string Message)> PrivateMessages { get; } = new List<(string Target, string Message)>();
            public List<(string Target, string Message)> GroupMessages { get; } = new List<(string Target, string Message)>();

            public void SendPrivateMessage(string qqNumber, string message)
            {
                PrivateMessages.Add((qqNumber, message));
            }

            public void SendGroupMessage(string groupId, string message)
            {
                GroupMessages.Add((groupId, message));
            }
        }
    }
}
#endif
