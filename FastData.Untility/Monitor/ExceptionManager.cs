using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FastUntility.Monitor
{
    public class ExceptionManager
    {
        private static readonly Lazy<ExceptionManager> _lazyInstance = new Lazy<ExceptionManager>(() => 
            throw new InvalidOperationException("ExceptionManager 尚未初始化，请先调用 Initialize 或 InitializeFromConfig 方法"));
        
        private static ExceptionManager _instance;
        private static readonly object _lock = new object();

        private readonly QQBotConfig _botConfig;
        private readonly ExceptionNotifyConfig _notifyConfig;
        private readonly IMessageSender _sender;
        private readonly ExceptionNotifier _notifier;
        private readonly RemoteCommandManager _commandManager;
        private readonly List<ExceptionInfo> _exceptionHistory = new List<ExceptionInfo>();
        private readonly int _maxHistorySize = 1000;

        private ExceptionManager(QQBotConfig botConfig, ExceptionNotifyConfig notifyConfig, IMessageSender sender = null, IConnectionPoolInfoProvider poolInfoProvider = null)
        {
            _botConfig = botConfig ?? throw new ArgumentNullException(nameof(botConfig));
            _notifyConfig = notifyConfig ?? throw new ArgumentNullException(nameof(notifyConfig));
            _sender = sender ?? new QQBotMessageSender(botConfig);
            _notifier = new ExceptionNotifier(_sender, _botConfig, _notifyConfig);
            _commandManager = new RemoteCommandManager(_botConfig, _sender, poolInfoProvider);

            RegisterBuiltinCommands();
        }

        public static ExceptionManager Instance
        {
            get
            {
                if (_instance == null)
                    throw new InvalidOperationException("ExceptionManager 尚未初始化，请先调用 Initialize 或 InitializeFromConfig 方法");
                return _instance;
            }
        }

        public static bool IsInitialized => _instance != null;

        public static ExceptionManager InitializeFromConfig(string configPath = "db.config", IMessageSender sender = null, IConnectionPoolInfoProvider poolInfoProvider = null)
        {
            var (botConfig, notifyConfig) = IMPlatformConfigParser.ParseFromConfig(configPath);

            if (!botConfig.IsEnabled)
                return null;

            return Initialize(botConfig, notifyConfig, sender, poolInfoProvider);
        }

        public static ExceptionManager Initialize(QQBotConfig botConfig, ExceptionNotifyConfig notifyConfig, IMessageSender sender = null, IConnectionPoolInfoProvider poolInfoProvider = null)
        {
            if (botConfig == null)
                throw new ArgumentNullException(nameof(botConfig));
            if (notifyConfig == null)
                throw new ArgumentNullException(nameof(notifyConfig));

            lock (_lock)
            {
                if (_instance != null)
                    throw new InvalidOperationException("ExceptionManager 已经初始化");

                _instance = new ExceptionManager(botConfig, notifyConfig, sender, poolInfoProvider);
                return _instance;
            }
        }

        public static ExceptionManager GetInstance()
        {
            return _instance;
        }

        public static bool TryGetInstance(out ExceptionManager manager)
        {
            manager = _instance;
            return manager != null;
        }

        /// <summary>
        /// 注册内置指令
        /// </summary>
        private void RegisterBuiltinCommands()
        {
            _commandManager.RegisterHandler(new HelpCommandHandler(_commandManager));
            _commandManager.RegisterHandler(new ServerStatusCommandHandler());
            _commandManager.RegisterHandler(new MemoryCommandHandler());
            _commandManager.RegisterHandler(new CpuCommandHandler());
            _commandManager.RegisterHandler(new DiskCommandHandler());
            _commandManager.RegisterHandler(new ProcessCommandHandler());
            _commandManager.RegisterHandler(new DbStatusCommandHandler(_commandManager));
            _commandManager.RegisterHandler(new DbCloseCommandHandler(_commandManager));
            _commandManager.RegisterHandler(new DbRestartCommandHandler(_commandManager));
            _commandManager.RegisterHandler(new GcCommandHandler());
            _commandManager.RegisterHandler(new VersionCommandHandler());
            _commandManager.RegisterHandler(new TimeCommandHandler());
        }

        /// <summary>
        /// 记录并通知异常
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="level">异常级别</param>
        /// <param name="source">来源</param>
        /// <param name="additionalData">附加数据</param>
        public void LogException(Exception ex, ExceptionLevel level = ExceptionLevel.Error, string source = null, Dictionary<string, string> additionalData = null)
        {
            var exceptionInfo = new ExceptionInfo
            {
                Level = level,
                Message = ex.Message,
                StackTrace = ex.StackTrace,
                Source = source ?? ex.Source,
                AdditionalData = additionalData ?? new Dictionary<string, string>()
            };

            LogException(exceptionInfo);
        }

        /// <summary>
        /// 记录并通知异常
        /// </summary>
        public void LogException(ExceptionInfo exceptionInfo)
        {
            // 添加到历史记录
            lock (_exceptionHistory)
            {
                _exceptionHistory.Add(exceptionInfo);

                // 限制历史记录大小
                while (_exceptionHistory.Count > _maxHistorySize)
                {
                    _exceptionHistory.RemoveAt(0);
                }
            }

            // 发送通知
            _notifier.Notify(exceptionInfo);
        }

        /// <summary>
        /// 处理远程消息
        /// </summary>
        public void ProcessMessage(string senderQQ, string groupId, string message)
        {
            _commandManager.ProcessMessage(senderQQ, groupId, message);
        }

        /// <summary>
        /// 执行远程指令
        /// </summary>
        public RemoteCommandResponse ExecuteCommand(string command, string[] args, string senderQQ, string groupId = null)
        {
            var request = new RemoteCommandRequest
            {
                Command = command,
                Args = args,
                SenderQQ = senderQQ,
                GroupId = groupId
            };

            return _commandManager.ExecuteCommand(request);
        }

        /// <summary>
        /// 注册自定义指令处理器
        /// </summary>
        public void RegisterCommandHandler(IRemoteCommandHandler handler)
        {
            _commandManager.RegisterHandler(handler);
        }

        /// <summary>
        /// 获取异常历史
        /// </summary>
        public List<ExceptionInfo> GetExceptionHistory(int count = 100)
        {
            lock (_exceptionHistory)
            {
                var startIndex = Math.Max(0, _exceptionHistory.Count - count);
                return _exceptionHistory.GetRange(startIndex, _exceptionHistory.Count - startIndex);
            }
        }

        /// <summary>
        /// 清空异常历史
        /// </summary>
        public void ClearExceptionHistory()
        {
            lock (_exceptionHistory)
            {
                _exceptionHistory.Clear();
            }
        }

        /// <summary>
        /// 获取指令管理器
        /// </summary>
        public RemoteCommandManager GetCommandManager()
        {
            return _commandManager;
        }

        /// <summary>
        /// 获取消息发送器
        /// </summary>
        public IMessageSender GetMessageSender()
        {
            return _sender;
        }

        /// <summary>
        /// 获取配置
        /// </summary>
        public QQBotConfig GetBotConfig()
        {
            return _botConfig;
        }
    }

    /// <summary>
    /// 全局异常处理器
    /// </summary>
    public static class GlobalExceptionHandler
    {
        /// <summary>
        /// 注册全局异常处理
        /// </summary>
        public static void Register()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        /// <summary>
        /// 注销全局异常处理
        /// </summary>
        public static void Unregister()
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            if (exception != null)
            {
                var manager = ExceptionManager.GetInstance();
                manager?.LogException(exception, ExceptionLevel.Critical, "AppDomain.UnhandledException");
            }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            var exception = e.Exception;
            if (exception != null)
            {
                var manager = ExceptionManager.GetInstance();
                manager?.LogException(exception, ExceptionLevel.Error, "TaskScheduler.UnobservedTaskException");
                e.SetObserved();
            }
        }
    }

    /// <summary>
    /// ORM 异常拦截器
    /// </summary>
    public static class OrmExceptionInterceptor
    {
        /// <summary>
        /// 拦截 ORM 异常
        /// </summary>
        public static void Intercept(Exception ex, string operation, string database = null)
        {
            var manager = ExceptionManager.GetInstance();
            if (manager == null)
                return;

            var additionalData = new Dictionary<string, string>
            {
                { "Operation", operation }
            };

            if (!string.IsNullOrEmpty(database))
            {
                additionalData["Database"] = database;
            }

            manager.LogException(ex, ExceptionLevel.Error, "ORM", additionalData);
        }
    }
}
