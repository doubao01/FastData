using System;
using FastData.ConnectionPool;
using FastUntility.Monitor;

namespace FastData
{
    /// <summary>
    /// IM 平台快捷入口。
    /// 当前内置 QQ 机器人通知能力，适配 OneBot/go-cqhttp 风格 HTTP API。
    /// </summary>
    public static class FastIM
    {
        private static readonly object LockObj = new object();
        private static FastIMState _state;

        /// <summary>
        /// 当前 QQ 机器人配置。
        /// </summary>
        public static QQBotConfig BotConfig => _state?.BotConfig;

        /// <summary>
        /// 当前异常通知配置。
        /// </summary>
        public static ExceptionNotifyConfig NotifyConfig => _state?.NotifyConfig;

        /// <summary>
        /// 当前远程指令管理器。
        /// </summary>
        public static RemoteCommandManager Commands => _state?.CommandManager;

        /// <summary>
        /// 当前 IM 消息发送器。
        /// </summary>
        public static IMessageSender Sender => _state?.Sender;

        /// <summary>
        /// 从配置文件加载 IM 平台配置并初始化 QQ 机器人发送器。
        /// </summary>
        /// <param name="configPath">配置文件路径，默认 db.config。</param>
        /// <param name="sender">可选自定义发送器，测试或自定义平台适配时使用。</param>
        /// <returns>当前 QQ 机器人配置。</returns>
        public static QQBotConfig Load(string configPath = "db.config", IMessageSender sender = null)
        {
            var parsed = IMPlatformConfigParser.ParseFromConfig(configPath);
            return Configure(parsed.BotConfig, parsed.NotifyConfig, sender);
        }

        /// <summary>
        /// 使用指定配置初始化 IM 平台。
        /// </summary>
        /// <param name="botConfig">QQ 机器人配置。</param>
        /// <param name="notifyConfig">异常通知配置。</param>
        /// <param name="sender">可选自定义发送器。</param>
        /// <returns>当前 QQ 机器人配置。</returns>
        public static QQBotConfig Configure(QQBotConfig botConfig, ExceptionNotifyConfig notifyConfig = null, IMessageSender sender = null)
        {
            return Configure(botConfig, notifyConfig, sender, null);
        }

        /// <summary>
        /// 使用指定配置初始化 IM 平台，并指定连接池信息源。
        /// </summary>
        /// <param name="botConfig">QQ 机器人配置。</param>
        /// <param name="notifyConfig">异常通知配置。</param>
        /// <param name="sender">可选自定义发送器，测试或自定义平台适配时使用。</param>
        /// <param name="poolInfoProvider">
        /// 连接池信息源，供 dbstatus / dbclose / dbrestart 指令使用。
        /// 传 null 时自动使用 <see cref="ConnectionPoolFactory.Instance"/>；传入实现
        /// <see cref="IConnectionPoolReopenProvider"/> 的提供者即可让 dbrestart 真正重建并预热连接池。
        /// </param>
        /// <returns>当前 QQ 机器人配置。</returns>
        public static QQBotConfig Configure(QQBotConfig botConfig, ExceptionNotifyConfig notifyConfig,
            IMessageSender sender, IConnectionPoolInfoProvider poolInfoProvider)
        {
            if (botConfig == null)
                throw new ArgumentNullException(nameof(botConfig));

            lock (LockObj)
            {
                var currentSender = sender ?? new QQBotMessageSender(botConfig);
                var currentNotifyConfig = notifyConfig ?? new ExceptionNotifyConfig();
                var currentPoolInfoProvider = poolInfoProvider ?? ConnectionPoolFactory.Instance;
                var commandManager = new RemoteCommandManager(botConfig, currentSender, currentPoolInfoProvider);

                RegisterDefaultCommands(commandManager);
                _state = new FastIMState(
                    botConfig,
                    currentNotifyConfig,
                    currentSender,
                    new ExceptionNotifier(currentSender, botConfig, currentNotifyConfig),
                    commandManager);

                return _state.BotConfig;
            }
        }

        /// <summary>
        /// 发送 QQ 私聊消息。
        /// </summary>
        public static void SendPrivateMessage(string qqNumber, string message)
        {
            EnsureLoaded().Sender.SendPrivateMessage(qqNumber, message);
        }

        /// <summary>
        /// 发送 QQ 群消息。
        /// </summary>
        public static void SendGroupMessage(string groupId, string message)
        {
            EnsureLoaded().Sender.SendGroupMessage(groupId, message);
        }

        /// <summary>
        /// 向配置中的通知群、管理员和通知用户发送文本消息。
        /// </summary>
        public static void NotifyText(string message, ExceptionLevel level = ExceptionLevel.Info, string source = null)
        {
            NotifyException(new ExceptionInfo
            {
                Level = level,
                Message = message,
                Source = source ?? "FastData"
            });
        }

        /// <summary>
        /// 发送异常通知。
        /// </summary>
        public static void NotifyException(Exception exception, ExceptionLevel level = ExceptionLevel.Error, string source = null)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            NotifyException(new ExceptionInfo
            {
                Level = level,
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                Source = source ?? exception.Source ?? "FastData"
            });
        }

        /// <summary>
        /// 发送异常通知。
        /// </summary>
        public static void NotifyException(ExceptionInfo exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            EnsureLoaded().Notifier.Notify(exception);
        }

        /// <summary>
        /// 处理 QQ 机器人收到的消息。由 OneBot 反向 HTTP/WebSocket 接入层调用。
        /// </summary>
        public static void ProcessMessage(string senderQQ, string groupId, string message)
        {
            EnsureLoaded().CommandManager.ProcessMessage(senderQQ, groupId, message);
        }

        private static FastIMState EnsureLoaded()
        {
            if (_state != null)
                return _state;

            lock (LockObj)
            {
                if (_state == null)
                    Load();

                return _state;
            }
        }

        private static void RegisterDefaultCommands(RemoteCommandManager manager)
        {
            manager.RegisterHandler(new HelpCommandHandler(manager));
            manager.RegisterHandler(new ServerStatusCommandHandler());
            manager.RegisterHandler(new MemoryCommandHandler());
            manager.RegisterHandler(new CpuCommandHandler());
            manager.RegisterHandler(new DiskCommandHandler());
            manager.RegisterHandler(new ProcessCommandHandler());
            manager.RegisterHandler(new DbStatusCommandHandler(manager));
            manager.RegisterHandler(new DbCloseCommandHandler(manager));
            manager.RegisterHandler(new DbRestartCommandHandler(manager));
            manager.RegisterHandler(new GcCommandHandler());
            manager.RegisterHandler(new VersionCommandHandler());
            manager.RegisterHandler(new TimeCommandHandler());
        }

        private sealed class FastIMState
        {
            public FastIMState(QQBotConfig botConfig, ExceptionNotifyConfig notifyConfig, IMessageSender sender,
                ExceptionNotifier notifier, RemoteCommandManager commandManager)
            {
                BotConfig = botConfig;
                NotifyConfig = notifyConfig;
                Sender = sender;
                Notifier = notifier;
                CommandManager = commandManager;
            }

            public QQBotConfig BotConfig { get; }
            public ExceptionNotifyConfig NotifyConfig { get; }
            public IMessageSender Sender { get; }
            public ExceptionNotifier Notifier { get; }
            public RemoteCommandManager CommandManager { get; }
        }
    }
}
