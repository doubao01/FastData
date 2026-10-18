using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Timers;
using FastData.Tooling.Sync;
using FastData.SyncTool.WinForms.Components;
using FastData.SyncTool.WinForms.Services;

namespace FastData.SyncTool.WinForms
{
    /// <summary>
    /// FastData 数据同步工具主窗体
    /// 采用 TabControl + UserControl 模块化架构
    /// </summary>
    public class MainForm : Form
    {
        #region 主题颜色常量

        private static readonly Color PrimaryColor = Color.FromArgb(0, 120, 212);
        private static readonly Color PrimaryDarkColor = Color.FromArgb(0, 90, 158);
        private static readonly Color PrimaryLightColor = Color.FromArgb(200, 220, 240);
        private static readonly Color AccentColor = Color.FromArgb(0, 150, 136);
        private static readonly Color WarningColor = Color.FromArgb(255, 152, 0);
        private static readonly Color ErrorColor = Color.FromArgb(244, 67, 54);
        private static readonly Color SuccessColor = Color.FromArgb(76, 175, 80);
        private static readonly Color BgColor = Color.FromArgb(245, 247, 250);
        private static readonly Color CardBgColor = Color.White;
        private static readonly Color TextPrimaryColor = Color.FromArgb(33, 33, 33);
        private static readonly Color TextSecondaryColor = Color.FromArgb(117, 117, 117);
        private static readonly Color BorderColor = Color.FromArgb(224, 224, 224);
        private static readonly Color DividerColor = Color.FromArgb(238, 238, 238);

        #endregion

        #region UI 控件

        private readonly MenuStrip _menuStrip = new MenuStrip();
        private readonly TabControl _tabControl = new TabControl();
        private readonly StatusStrip _statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel _statusLabel = new ToolStripStatusLabel();
        private readonly ToolStripProgressBar _progressBar = new ToolStripProgressBar();
        private readonly ToolStripStatusLabel _progressText = new ToolStripStatusLabel();
        private readonly Panel _headerPanel = new Panel();

        #endregion

        #region 组件

        private DbConnectionPanel _connectionPanel;
        private TableListManager _tableListManager;
        private SyncConfigPanel _syncConfigPanel;
        private TaskManager _taskManager;
        private ReplayControl _replayControl;
        private ShardingTabContainer _shardingContainer;
        private LogViewerControl _logViewer;

        #endregion

        #region 服务

        private readonly SyncConfigManager _configManager;
        private readonly SyncService _syncService;
        private readonly LogService _logService;
        private readonly PrimaryKeyConfigService _pkConfigService;
        private readonly TaskSchedulerService _schedulerService;
        private System.Timers.Timer _syncTimer;

        #endregion

        #region 状态

        private string _currentTaskId;
        private bool _isSyncing;

        #endregion

        /// <summary>
        /// 构造函数：初始化所有服务、UI 组件和事件绑定
        /// </summary>
        public MainForm()
        {
            _configManager = new SyncConfigManager();
            _syncService = new SyncService();
            _logService = new LogService();
            _pkConfigService = new PrimaryKeyConfigService();
            _schedulerService = new TaskSchedulerService();

            InitWindow();
            ApplyTheme();
            InitHeader();
            InitMenu();
            InitTabs();
            InitComponents();
            BindEvents();
            InitTimer();

            _logService.Info("FastData 同步工具已启动");
        }

        #region 初始化

        /// <summary>
        /// 初始化窗体外观
        /// </summary>
        private void InitWindow()
        {
            Text = "FastData 数据同步工具 v2.0";
            Width = 1260;
            Height = 840;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(960, 640);
            BackColor = BgColor;
            Font = new Font("Microsoft YaHei UI", 9F);
            Icon = SystemIcons.Application;
        }

        /// <summary>
        /// 应用全局主题样式
        /// </summary>
        private void ApplyTheme()
        {
            // 菜单栏样式
            _menuStrip.BackColor = PrimaryColor;
            _menuStrip.ForeColor = Color.White;
            _menuStrip.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            _menuStrip.Renderer = new CustomMenuRenderer(PrimaryColor, Color.FromArgb(0, 103, 184));

            // 状态栏样式
            _statusStrip.BackColor = PrimaryColor;
            _statusStrip.ForeColor = Color.White;
            _statusStrip.Font = new Font("Microsoft YaHei UI", 8.5F);
            _statusLabel.ForeColor = Color.FromArgb(240, 240, 240);
            _progressText.ForeColor = Color.FromArgb(240, 240, 240);

            // TabControl 样式
            _tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabControl.DrawItem += DrawTabItem;
            _tabControl.BackColor = BgColor;
            _tabControl.Font = new Font("Microsoft YaHei UI", 9F);
            _tabControl.Padding = new Point(12, 6);
            _tabControl.ItemSize = new Size(140, 36);
        }

        /// <summary>
        /// 自定义 Tab 页绘制（蓝色主题 + 选中态高亮）
        /// </summary>
        private void DrawTabItem(object sender, DrawItemEventArgs e)
        {
            var tabPage = _tabControl.TabPages[e.Index];
            var tabRect = _tabControl.GetTabRect(e.Index);
            var isSelected = _tabControl.SelectedIndex == e.Index;

            // 背景
            var bgBrush = isSelected
                ? new SolidBrush(Color.White)
                : new SolidBrush(Color.FromArgb(240, 243, 248));
            e.Graphics.FillRectangle(bgBrush, tabRect);

            // 选中态底部蓝色指示条
            if (isSelected)
            {
                var indicatorRect = new Rectangle(tabRect.X + 12, tabRect.Bottom - 3, tabRect.Width - 24, 3);
                e.Graphics.FillRectangle(new SolidBrush(PrimaryColor), indicatorRect);
            }

            // 文字
            var textColor = isSelected ? PrimaryColor : TextSecondaryColor;
            var textFont = new Font("Microsoft YaHei UI", 9F, isSelected ? FontStyle.Bold : FontStyle.Regular);
            var textBrush = new SolidBrush(textColor);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            // 图标前缀
            var icon = GetTabIcon(tabPage.Text);
            var textRect = new Rectangle(tabRect.X + 8, tabRect.Y, tabRect.Width - 16, tabRect.Height);
            e.Graphics.DrawString(icon + " " + tabPage.Text, textFont, textBrush, textRect, sf);

            // 底部边框
            e.Graphics.DrawLine(new Pen(DividerColor), tabRect.X, tabRect.Bottom - 1, tabRect.Right, tabRect.Bottom - 1);

            bgBrush.Dispose();
            textBrush.Dispose();
            textFont.Dispose();
            sf.Dispose();
        }

        /// <summary>
        /// 获取 Tab 页图标
        /// </summary>
        private static string GetTabIcon(string tabName)
        {
            switch (tabName)
            {
                case "数据库配置": return "\u2699";   // ⚙
                case "表列表管理": return "\u2630";   // ☰
                case "同步配置": return "\u21C4";      // ⇄
                case "任务管理": return "\u25B6";      // ▶
                case "数据补录": return "\u21BA";      // ↺
                case "分表同步": return "\u2261";      // ≡
                case "运行日志": return "\u2637";      // ☷
                default: return "\u25CF";              // ●
            }
        }

        /// <summary>
        /// 初始化顶部 Header 面板
        /// </summary>
        private void InitHeader()
        {
            _headerPanel.Height = 56;
            _headerPanel.Dock = DockStyle.Top;
            _headerPanel.BackColor = Color.White;
            _headerPanel.Padding = new Padding(20, 0, 20, 0);

            // 左侧标题
            var titleLabel = new Label
            {
                Text = "FastData 数据同步工具",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = PrimaryColor,
                AutoSize = true,
                Location = new Point(20, 12)
            };

            // 右侧状态指示
            var statusDot = new Panel
            {
                Size = new Size(8, 8),
                Location = new Point(_headerPanel.Width - 100, 22),
                BackColor = SuccessColor
            };
            statusDot.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(new SolidBrush(_isSyncing ? WarningColor : SuccessColor), 0, 0, 8, 8);
            };
            var statusText = new Label
            {
                Text = _isSyncing ? "运行中" : "就绪",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = TextSecondaryColor,
                AutoSize = true,
                Location = new Point(_headerPanel.Width - 88, 18)
            };

            _headerPanel.Controls.Add(titleLabel);
            _headerPanel.Controls.Add(statusDot);
            _headerPanel.Controls.Add(statusText);

            // 底部分割线
            var divider = new Panel
            {
                Height = 1,
                Dock = DockStyle.Bottom,
                BackColor = DividerColor
            };
            _headerPanel.Controls.Add(divider);

            _headerPanel.Resize += (s, e) =>
            {
                statusDot.Location = new Point(_headerPanel.Width - 100, 22);
                statusText.Location = new Point(_headerPanel.Width - 88, 18);
            };
        }

        /// <summary>
        /// 初始化菜单栏
        /// </summary>
        private void InitMenu()
        {
            var fileMenu = new ToolStripMenuItem("文件 (&F)");
            fileMenu.DropDownItems.Add("新建任务", null, (s, e) => CreateNewTask());
            fileMenu.DropDownItems.Add("保存任务", null, (s, e) => SaveCurrentTask());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("导出任务配置", null, (s, e) => ExportCurrentTask());
            fileMenu.DropDownItems.Add("导入任务配置", null, (s, e) => ImportTask());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("退出", null, (s, e) => Close());

            var syncMenu = new ToolStripMenuItem("同步 (&S)");
            syncMenu.DropDownItems.Add("开始同步", null, (s, e) => StartSync());
            syncMenu.DropDownItems.Add("停止同步", null, (s, e) => StopSync());
            syncMenu.DropDownItems.Add(new ToolStripSeparator());
            syncMenu.DropDownItems.Add("启用定时同步", null, (s, e) => EnableScheduledSync());
            syncMenu.DropDownItems.Add("禁用定时同步", null, (s, e) => DisableScheduledSync());

            var toolsMenu = new ToolStripMenuItem("工具 (&T)");
            toolsMenu.DropDownItems.Add("主键配置", null, (s, e) => OpenPrimaryKeyConfig());
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add("定时任务管理", null, (s, e) => OpenSchedulerManager());

            var helpMenu = new ToolStripMenuItem("帮助 (&H)");
            helpMenu.DropDownItems.Add("关于", null, (s, e) => ShowAbout());

            _menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, syncMenu, toolsMenu, helpMenu });
            MainMenuStrip = _menuStrip;
            Controls.Add(_menuStrip);
        }

        /// <summary>
        /// 初始化 TabControl 标签页
        /// </summary>
        private void InitTabs()
        {
            _tabControl.Dock = DockStyle.Fill;
            _tabControl.TabPages.Add("数据库配置");
            _tabControl.TabPages.Add("表列表管理");
            _tabControl.TabPages.Add("同步配置");
            _tabControl.TabPages.Add("任务管理");
            _tabControl.TabPages.Add("数据补录");
            _tabControl.TabPages.Add("分表同步");
            _tabControl.TabPages.Add("运行日志");

            // 布局：Header → TabControl → StatusBar
            Controls.Add(_tabControl);
            Controls.Add(_headerPanel);
            _tabControl.BringToFront();
        }

        /// <summary>
        /// 初始化各标签页的 UserControl 组件和状态栏
        /// </summary>
        private void InitComponents()
        {
            // 所有 TabPage 统一样式
            foreach (TabPage page in _tabControl.TabPages)
            {
                page.BackColor = Color.White;
                page.Padding = new Padding(0);
                page.UseVisualStyleBackColor = false;
            }

            // 数据库配置 Tab
            _connectionPanel = new DbConnectionPanel();
            _connectionPanel.BackColor = Color.White;
            _tabControl.TabPages[0].Controls.Add(_connectionPanel);

            // 表列表管理 Tab
            _tableListManager = new TableListManager();
            _tableListManager.BackColor = Color.White;
            _tabControl.TabPages[1].Controls.Add(_tableListManager);

            // 同步配置 Tab
            _syncConfigPanel = new SyncConfigPanel();
            _syncConfigPanel.BackColor = Color.White;
            _tabControl.TabPages[2].Controls.Add(_syncConfigPanel);

            // 任务管理 Tab
            _taskManager = new TaskManager(_configManager);
            _taskManager.BackColor = Color.White;
            _tabControl.TabPages[3].Controls.Add(_taskManager);

            // 数据补录 Tab
            _replayControl = new ReplayControl(_configManager);
            _replayControl.BackColor = Color.White;
            _tabControl.TabPages[4].Controls.Add(_replayControl);

            // 分表同步 Tab（独立 UserControl）
            _shardingContainer = new ShardingTabContainer(_logService);
            _tabControl.TabPages[5].Controls.Add(_shardingContainer);

            // 运行日志 Tab（独立 UserControl）
            _logViewer = new LogViewerControl(_logService);
            _tabControl.TabPages[6].Controls.Add(_logViewer);

            // 状态栏
            InitStatusBar();
        }

        /// </summary>
        private void InitStatusBar()
        {
            _statusLabel.Text = "就绪";
            _statusLabel.Spring = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _progressBar.Width = 200;
            _progressBar.Visible = false;
            _progressText.Text = "";
            _progressText.AutoSize = true;
            _statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel, _progressBar, _progressText });
            Controls.Add(_statusStrip);
        }

        /// <summary>
        /// 初始化定时同步计时器
        /// </summary>
        private void InitTimer()
        {
            _syncTimer = new System.Timers.Timer(60000); // 默认 60 秒
            _syncTimer.Elapsed += OnTimerElapsed;
            _syncTimer.AutoReset = true;
        }

        #endregion

        #region 事件绑定

        /// <summary>
        /// 绑定各组件的事件响应
        /// </summary>
        private void BindEvents()
        {
            // 组件状态变更事件
            _connectionPanel.OnConfigChanged += () => _statusLabel.Text = "配置已修改";
            _tableListManager.OnTablesChanged += () => _statusLabel.Text = "表列表已修改";
            _syncConfigPanel.OnConfigChanged += () => _statusLabel.Text = "同步配置已修改";
            _taskManager.OnTaskSelected += LoadSelectedTask;
            _taskManager.OnTaskDeleted += () => { _currentTaskId = null; _statusLabel.Text = "任务已删除"; };

            // 同步进度事件（需跨线程调用）
            _syncService.ProgressChanged += (s, info) =>
            {
                if (InvokeRequired) { BeginInvoke((Action)(() => OnSyncProgress(info))); return; }
                OnSyncProgress(info);
            };

            // 表同步完成事件（需跨线程调用）
            _syncService.TableCompleted += (s, result) =>
            {
                if (InvokeRequired) { BeginInvoke((Action)(() => OnTableCompleted(result))); return; }
                OnTableCompleted(result);
            };

            // 定时任务事件
            _schedulerService.TaskCompleted += (s, task) =>
                _logService.Info(string.Format("定时任务完成: {0}", task.TaskId));
            _schedulerService.TaskFailed += (s, task) =>
                _logService.Error(string.Format("定时任务失败: {0} - {1}", task.TaskId, task.LastError));

            // Tab 切换时自动加载任务列表
            _tabControl.SelectedIndexChanged += (s, e) =>
            {
                if (_tabControl.SelectedIndex == 3)
                    _taskManager.LoadTaskList();
            };
        }

        /// <summary>
        /// 同步进度更新回调
        /// </summary>
        private void OnSyncProgress(SyncProgressInfo info)
        {
            _progressBar.Value = info.TotalTables > 0
                ? (int)((double)info.CompletedTables / info.TotalTables * 100)
                : 0;
            _progressText.Text = string.Format("{0}/{1}", info.CompletedTables, info.TotalTables);
            _statusLabel.Text = info.Status;
        }

        /// <summary>
        /// 单表同步完成回调
        /// </summary>
        private void OnTableCompleted(SyncExecutionResult result)
        {
            if (result.Success)
                _logService.Info(string.Format("表 {0} -> {1} 完成: 读取 {2}, 写入 {3}",
                    result.SourceTable, result.TargetTable, result.ReadCount, result.WriteCount));
            else
                _logService.Error(string.Format("表 {0} -> {1} 失败: {2}",
                    result.SourceTable, result.TargetTable, result.ErrorMessage));
        }

        /// <summary>
        /// 定时器触发回调
        /// </summary>
        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_isSyncing) return; // 防止重复触发

            if (InvokeRequired)
                BeginInvoke((Action)(() => StartSync()));
            else
                StartSync();
        }

        #endregion

        #region 任务管理

        /// <summary>
        /// 加载选中的任务配置到各组件
        /// </summary>
        private void LoadSelectedTask()
        {
            var taskId = _taskManager.GetSelectedTaskId();
            if (string.IsNullOrEmpty(taskId)) return;

            var config = _configManager.GetTaskConfig(taskId);
            if (config == null) return;

            _currentTaskId = taskId;

            // 将任务配置分发到各组件
            if (!string.IsNullOrEmpty(config.SourceConnection))
                _connectionPanel.SetSource("SqlServer", config.SourceConnection);
            if (!string.IsNullOrEmpty(config.TargetConnection))
                _connectionPanel.SetTarget("SqlServer", config.TargetConnection);
            if (!string.IsNullOrEmpty(config.IntermediateConnection))
                _connectionPanel.SetIntermediate("SqlServer", config.IntermediateConnection);

            _tableListManager.SetTableConfigs(config.TableConfigs);
            _syncConfigPanel.LoadFromTask(config);

            _statusLabel.Text = "已加载任务: " + taskId;
            _logService.Info("加载任务: " + taskId);
        }

        /// <summary>
        /// 创建新任务
        /// </summary>
        private void CreateNewTask()
        {
            using (var form = new Form())
            {
                form.Text = "新建任务";
                form.Size = new Size(400, 150);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var label = new Label { Text = "任务名称:", Location = new Point(20, 20), AutoSize = true };
                var textBox = new TextBox { Location = new Point(100, 17), Width = 250 };
                var btnOk = new Button { Text = "确定", Location = new Point(120, 60), Width = 75, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "取消", Location = new Point(210, 60), Width = 75, DialogResult = DialogResult.Cancel };
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;
                form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });

                if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(textBox.Text))
                {
                    var id = textBox.Text.Trim();
                    var newTask = new SyncTaskConfig
                    {
                        TaskId = id,
                        TaskName = id,
                        IsEnabled = true,
                        RangeDays = 3,
                        CreatedTime = DateTime.Now,
                        ModifiedTime = DateTime.Now
                    };
                    _configManager.SaveTaskConfig(newTask);
                    _currentTaskId = id;
                    _taskManager.LoadTaskList();
                    _taskManager.SelectTask(id);
                    _statusLabel.Text = "已创建任务: " + id;
                    _logService.Info("创建任务: " + id);
                }
            }
        }

        /// <summary>
        /// 保存当前任务配置
        /// </summary>
        private void SaveCurrentTask()
        {
            if (string.IsNullOrEmpty(_currentTaskId))
            {
                ShowWarning("请先创建或加载任务");
                return;
            }

            var config = _configManager.GetTaskConfig(_currentTaskId)
                ?? new SyncTaskConfig { TaskId = _currentTaskId };

            config.TaskName = _currentTaskId;
            config.SourceConnection = _connectionPanel.SourceConnectionString;
            config.TargetConnection = _connectionPanel.TargetConnectionString;
            config.IntermediateConnection = _connectionPanel.IntermediateConnectionString;
            config.TableConfigs = _tableListManager.GetTableConfigs();
            config.ModifiedTime = DateTime.Now;

            if (config.CreatedTime == default) config.CreatedTime = DateTime.Now;

            _configManager.SaveTaskConfig(config);
            _taskManager.LoadTaskList();
            _taskManager.SelectTask(_currentTaskId);
            _statusLabel.Text = "任务已保存: " + _currentTaskId;
            _logService.Info("保存任务: " + _currentTaskId);
            ShowInfo("任务配置已保存");
        }

        /// <summary>
        /// 导出当前任务配置为 JSON 文件
        /// </summary>
        private void ExportCurrentTask()
        {
            if (string.IsNullOrEmpty(_currentTaskId))
            {
                ShowWarning("请先创建或加载任务");
                return;
            }

            var config = _configManager.GetTaskConfig(_currentTaskId);
            if (config == null)
            {
                ShowWarning("任务配置不存在");
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                Filter = "JSON 文件|*.json",
                FileName = string.Format("{0}_config.json", _currentTaskId)
            })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
                    _logService.Info("任务配置已导出: " + dialog.FileName);
                    ShowInfo("导出成功");
                }
            }
        }

        /// <summary>
        /// 从 JSON 文件导入任务配置
        /// </summary>
        private void ImportTask()
        {
            using (var dialog = new OpenFileDialog { Filter = "JSON 文件|*.json" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var config = JsonSerializer.Deserialize<SyncTaskConfig>(json);

                    if (config != null && !string.IsNullOrEmpty(config.TaskId))
                    {
                        _configManager.SaveTaskConfig(config);
                        _taskManager.LoadTaskList();
                        _logService.Info("任务配置已导入: " + dialog.FileName);
                        ShowInfo("导入成功");
                    }
                    else
                    {
                        ShowWarning("配置文件格式错误");
                    }
                }
                catch (Exception ex)
                {
                    _logService.Error("导入失败: " + ex.Message);
                    ShowError("导入失败：" + ex.Message);
                }
            }
        }

        #endregion

        #region 同步操作

        /// <summary>
        /// 开始执行同步任务
        /// </summary>
        private void StartSync()
        {
            if (_isSyncing) { ShowWarning("同步正在进行中"); return; }
            if (string.IsNullOrEmpty(_currentTaskId)) { ShowWarning("请先创建或加载任务"); return; }

            var config = _configManager.GetTaskConfig(_currentTaskId);
            if (config == null) { ShowWarning("任务配置不存在"); return; }

            var tableConfigs = _tableListManager.GetTableConfigs();
            if (tableConfigs.Count == 0) { ShowWarning("没有配置同步表"); return; }

            _isSyncing = true;
            _progressBar.Visible = true;
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 0;
            _progressText.Text = "0/" + tableConfigs.Count;
            _statusLabel.Text = "正在同步...";
            _logService.Info("开始同步任务: " + _currentTaskId);

            var baseOptions = new DataSyncOptions
            {
                SourceProvider = _connectionPanel.SourceProvider,
                SourceConnectionString = _connectionPanel.SourceConnectionString,
                TargetProvider = _connectionPanel.TargetProvider,
                TargetConnectionString = _connectionPanel.TargetConnectionString,
                IntermediateProvider = _connectionPanel.IntermediateProvider,
                IntermediateConnectionString = _connectionPanel.IntermediateConnectionString,
                TaskId = _currentTaskId,
                ConfigManager = _configManager,
                PrimaryKeyConfigService = _pkConfigService,
            };
            _syncConfigPanel.ApplyToOptions(baseOptions);

            var batchResult = _syncService.ExecuteBatchSync(tableConfigs, baseOptions, (opts, tc) =>
            {
                return new DataSyncOptions
                {
                    SourceProvider = opts.SourceProvider,
                    SourceConnectionString = opts.SourceConnectionString,
                    TargetProvider = opts.TargetProvider,
                    TargetConnectionString = opts.TargetConnectionString,
                    IntermediateProvider = opts.IntermediateProvider,
                    IntermediateConnectionString = opts.IntermediateConnectionString,
                    TaskId = opts.TaskId,
                    SourceTable = tc.TableName,
                    TargetTable = string.IsNullOrEmpty(tc.TargetTableName) ? tc.TableName : tc.TargetTableName,
                    PrimaryKeyColumns = tc.PrimaryKeyColumns,
                    IsAutoIncrementKey = tc.IsAutoIncrementKey,
                    TimeColumn = tc.TimeColumn,
                    EnableTimeRange = tc.EnableTimeRange,
                    RangeDays = tc.RangeDays,
                    SyncColumns = tc.SyncColumns,
                    AlwaysDeduplicate = tc.AlwaysDeduplicate,
                    EnableGlobalConfig = tc.EnableGlobalConfig,
                    GlobalRangeDays = tc.GlobalRangeDays,
                    GlobalAlwaysDeduplicate = opts.AlwaysDeduplicate,
                    ConfigManager = opts.ConfigManager,
                    PrimaryKeyConfigService = opts.PrimaryKeyConfigService,
                    IsFullSyncForFirstTime = opts.IsFullSyncForFirstTime,
                    BatchSize = opts.BatchSize,
                    RetryCount = opts.RetryCount,
                    CleanIntermediateData = opts.CleanIntermediateData,
                    AutoCreateIntermediateSchema = opts.AutoCreateIntermediateSchema,
                    ResumeFailedRecords = opts.ResumeFailedRecords,
                };
            });

            _isSyncing = false;
            _progressBar.Visible = false;
            _progressText.Text = "";

            if (batchResult.Success)
            {
                _statusLabel.Text = string.Format("同步完成: {0} 表, 读取 {1}, 写入 {2}",
                    batchResult.SuccessTables, batchResult.TotalRead, batchResult.TotalWrite);
                _logService.Info(string.Format("同步完成: 成功 {0} 表, 读取 {1}, 写入 {2}, 耗时 {3}",
                    batchResult.SuccessTables, batchResult.TotalRead, batchResult.TotalWrite, batchResult.TotalDuration));
            }
            else
            {
                _statusLabel.Text = string.Format("同步完成: 成功 {0}, 失败 {1}, 跳过 {2}",
                    batchResult.SuccessTables, batchResult.FailedTables, batchResult.SkippedTables);
                _logService.Error(string.Format("同步部分失败: 成功 {0}, 失败 {1}, 跳过 {2}",
                    batchResult.SuccessTables, batchResult.FailedTables, batchResult.SkippedTables));
            }

            ShowSyncResult(batchResult);
        }

        /// <summary>
        /// 停止正在进行的同步
        /// </summary>
        private void StopSync()
        {
            if (!_isSyncing) return;
            _syncService.CancelBatchSync();
            _isSyncing = false;
            _progressBar.Visible = false;
            _progressText.Text = "";
            _statusLabel.Text = "同步已停止";
            _logService.Warn("同步被用户停止");
        }

        /// <summary>
        /// 显示同步结果对话框
        /// </summary>
        private void ShowSyncResult(BatchSyncResult result)
        {
            var msg = string.Format(
                "同步完成!\n\n成功表数: {0}\n失败表数: {1}\n跳过表数: {2}\n\n总读取: {3}\n总写入: {4}\n总失败: {5}\n\n耗时: {6}",
                result.SuccessTables, result.FailedTables, result.SkippedTables,
                result.TotalRead, result.TotalWrite, result.TotalFailed,
                result.TotalDuration);

            if (result.FailedTables > 0 || result.SkippedTables > 0)
            {
                msg += "\n\n失败详情:";
                foreach (var tableResult in result.TableResults)
                {
                    if (!tableResult.Success)
                        msg += string.Format("\n  {0} -> {1}: {2}",
                            tableResult.SourceTable, tableResult.TargetTable,
                            tableResult.ErrorMessage ?? "未知错误");
                }
            }

            var icon = result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
            MessageBox.Show(msg, "同步结果", MessageBoxButtons.OK, icon);
        }

        #endregion

        #region 定时同步

        /// <summary>
        /// 启用定时同步
        /// </summary>
        private void EnableScheduledSync()
        {
            using (var form = new Form())
            {
                form.Text = "定时同步设置";
                form.Size = new Size(300, 130);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;

                var label = new Label { Text = "同步间隔（秒）:", Location = new Point(20, 20), AutoSize = true };
                var intervalBox = new NumericUpDown
                {
                    Location = new Point(150, 18),
                    Width = 80,
                    Minimum = 10,
                    Maximum = 3600,
                    Value = 60
                };
                var btnOk = new Button { Text = "确定", Location = new Point(80, 60), Width = 75, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "取消", Location = new Point(170, 60), Width = 75, DialogResult = DialogResult.Cancel };

                form.Controls.AddRange(new Control[] { label, intervalBox, btnOk, btnCancel });
                form.AcceptButton = btnOk;

                if (form.ShowDialog() == DialogResult.OK)
                {
                    _syncTimer.Interval = (int)intervalBox.Value * 1000;
                    _syncTimer.Start();
                    _statusLabel.Text = string.Format("定时同步已启用，间隔 {0} 秒", (int)intervalBox.Value);
                    _logService.Info(string.Format("启用定时同步，间隔 {0} 秒", (int)intervalBox.Value));
                }
            }
        }

        /// <summary>
        /// 禁用定时同步
        /// </summary>
        private void DisableScheduledSync()
        {
            _syncTimer.Stop();
            _statusLabel.Text = "定时同步已禁用";
            _logService.Info("定时同步已禁用");
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 打开主键配置对话框
        /// </summary>
        private void OpenPrimaryKeyConfig()
        {
            var dialog = new PrimaryKeyConfigDialog(_pkConfigService);
            dialog.ShowDialog();
        }

        /// <summary>
        /// 打开定时任务管理器
        /// </summary>
        private void OpenSchedulerManager()
        {
            var tasks = _configManager.GetAllConfigs();
            if (tasks.Count == 0)
            {
                ShowInfo("没有已配置的任务，请先创建任务");
                return;
            }

            using (var form = new Form())
            {
                form.Text = "定时任务管理";
                form.Size = new Size(600, 400);
                form.StartPosition = FormStartPosition.CenterParent;

                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    ReadOnly = true,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect
                };
                grid.Columns.AddRange(new DataGridViewColumn[]
                {
                    new DataGridViewTextBoxColumn { Name = "TaskId", HeaderText = "任务ID", Width = 120 },
                    new DataGridViewTextBoxColumn { Name = "Enabled", HeaderText = "启用", Width = 50 },
                    new DataGridViewTextBoxColumn { Name = "Interval", HeaderText = "间隔", Width = 80 },
                    new DataGridViewTextBoxColumn { Name = "RunCount", HeaderText = "执行次数", Width = 80 },
                    new DataGridViewTextBoxColumn { Name = "LastRun", HeaderText = "上次执行", Width = 150 },
                    new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "状态", Width = 80 },
                });

                foreach (var task in _schedulerService.GetAllTasks())
                {
                    grid.Rows.Add(task.TaskId, task.Enabled, task.Interval.ToString(), task.RunCount,
                        task.LastRunTime.HasValue ? task.LastRunTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                        task.LastStatus ?? "");
                }

                var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 };
                var btnStart = new Button { Text = "启动调度", Width = 80 };
                var btnStop = new Button { Text = "停止调度", Width = 80 };
                var btnClose = new Button { Text = "关闭", Width = 80 };
                btnPanel.Controls.AddRange(new Control[] { btnStart, btnStop, btnClose });
                form.Controls.Add(grid);
                form.Controls.Add(btnPanel);

                btnStart.Click += (s, e) => { _schedulerService.StartAll(); ShowInfo("定时调度已启动"); };
                btnStop.Click += (s, e) => { _schedulerService.StopAll(); ShowInfo("定时调度已停止"); };
                btnClose.Click += (s, e) => form.Close();

                form.ShowDialog();
            }
        }

        /// <summary>
        /// 关于对话框
        /// </summary>
        private void ShowAbout()
        {
            MessageBox.Show(
                "FastData 数据同步工具 v2.0\n\n" +
                "支持 SQL Server / MySQL / PostgreSQL\n" +
                "异构成对 + 去重 + 增量同步\n" +
                "定时任务调度\n" +
                "日志过滤与导出\n" +
                "数据补录与分表同步",
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowWarning(string message) { MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void ShowInfo(string message) { MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        private void ShowError(string message) { MessageBox.Show(message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }

        #endregion

        #region 窗体生命周期

        /// <summary>
        /// 窗体关闭时的清理逻辑
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_isSyncing)
            {
                if (MessageBox.Show("同步正在进行中，确定退出吗？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                _syncService.CancelBatchSync();
            }

            _syncTimer?.Stop();
            _syncTimer?.Dispose();
            _schedulerService.Dispose();
            _syncService.Dispose();
            _logService.Dispose();

            base.OnFormClosing(e);
        }

        #endregion
    }

    /// <summary>
    /// 自定义菜单渲染器，提供蓝色主题样式
    /// </summary>
    internal class CustomMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Color _backColor;
        private readonly Color _hoverColor;

        public CustomMenuRenderer(Color backColor, Color hoverColor)
            : base(new ProfessionalColorTable())
        {
            _backColor = backColor;
            _hoverColor = hoverColor;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
            {
                // 正常状态下透明
                return;
            }

            // 悬停态使用浅色背景
            var rc = new Rectangle(Point.Empty, e.Item.Size);
            using (var brush = new SolidBrush(_hoverColor))
            {
                e.Graphics.FillRectangle(brush, rc);
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(_backColor))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Color.FromArgb(100, 255, 255, 255)))
            {
                var y = e.Item.ContentRectangle.Top + e.Item.ContentRectangle.Height / 2;
                e.Graphics.DrawLine(pen, e.Item.ContentRectangle.Left + 8, y, e.Item.ContentRectangle.Right - 8, y);
            }
        }
    }
}