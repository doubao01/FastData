using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FastData.Sharding;
using FastData.Sharding.Strategies;
using FastData.SyncTool.WinForms.Services;

namespace FastData.SyncTool.WinForms.Components
{
    /// <summary>
    /// 分表数据同步控件
    /// 支持从源表同步数据到分表，集成任务管理
    /// </summary>
    public class ShardingSyncControl : UserControl
    {
        private readonly ShardingTaskService _taskService;
        private readonly LogService _logService;

        // 源数据库配置
        private readonly TextBox sourceConnectionStringBox = new TextBox();
        private readonly ComboBox sourceProviderBox = new ComboBox();
        private readonly ComboBox sourceTableCombo = new ComboBox();
        private readonly Button loadSourceTablesButton = new Button();

        // 分表配置
        private readonly ComboBox shardingTypeBox = new ComboBox();
        private readonly TabControl configTabControl = new TabControl();

        // 时间分表配置
        private TabPage _timeConfigTab;
        private readonly TextBox timeFieldBox = new TextBox();
        private readonly ComboBox granularityBox = new ComboBox();
        private readonly DateTimePicker startTimePicker = new DateTimePicker();

        // 哈希分表配置
        private TabPage _hashConfigTab;
        private readonly TextBox hashFieldBox = new TextBox();
        private readonly NumericUpDown shardCountBox = new NumericUpDown();
        private readonly ComboBox hashAlgorithmBox = new ComboBox();

        // 列表分表配置
        private TabPage _listConfigTab;
        private readonly TextBox listFieldBox = new TextBox();
        private readonly DataGridView listMappingGrid = new DataGridView();

        // 组合键分表配置
        private TabPage _compositeConfigTab;
        private readonly TextBox compositeFieldsBox = new TextBox();
        private readonly NumericUpDown compositeShardCountBox = new NumericUpDown();

        // 查询频率分表配置
        private TabPage _frequencyConfigTab;
        private readonly TextBox frequencyFieldBox = new TextBox();
        private readonly NumericUpDown hotThresholdBox = new NumericUpDown();
        private readonly TextBox hotSuffixBox = new TextBox();
        private readonly TextBox coldSuffixBox = new TextBox();

        // 操作按钮
        private readonly Button previewButton = new Button();
        private readonly Button startTaskButton = new Button();
        private readonly Button createTablesButton = new Button();

        // 预览和日志
        private readonly DataGridView previewGrid = new DataGridView();
        private readonly TextBox logBox = new TextBox();
        private readonly Label statusLabel = new Label();

        // 任务管理
        private readonly ShardingTaskControl _taskControl;

        private ShardingConfig _currentConfig;
        private string _currentConnectionString;

        public ShardingSyncControl(ShardingTaskService taskService, LogService logService)
        {
            _taskService = taskService;
            _logService = logService;
            _taskControl = new ShardingTaskControl(taskService, logService);

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(1000, 800);

            // 主布局 - 使用 SplitContainer
            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 450
            };

            // 上半部分：配置区域
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            // 左侧：源数据库和分表配置
            var leftPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // 源数据库配置
            var sourceGroup = new GroupBox
            {
                Text = "源数据库配置",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var sourceLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4
            };

            sourceLayout.Controls.Add(new Label { Text = "数据库类型:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            sourceProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceProviderBox.Items.AddRange(new object[] { "SqlServer", "MySql", "SQLite" });
            sourceProviderBox.SelectedIndex = 0;
            sourceLayout.Controls.Add(sourceProviderBox, 1, 0);

            sourceLayout.Controls.Add(new Label { Text = "连接字符串:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            sourceConnectionStringBox.Text = string.Empty;
            sourceLayout.Controls.Add(sourceConnectionStringBox, 1, 1);

            sourceLayout.Controls.Add(new Label { Text = "源表名:", TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            sourceTableCombo.DropDownStyle = ComboBoxStyle.DropDown;
            sourceLayout.Controls.Add(sourceTableCombo, 1, 2);

            loadSourceTablesButton.Text = "加载表";
            loadSourceTablesButton.Click += LoadSourceTablesButton_Click;
            sourceLayout.Controls.Add(loadSourceTablesButton, 1, 3);

            sourceGroup.Controls.Add(sourceLayout);
            leftPanel.Controls.Add(sourceGroup, 0, 0);

            // 分表配置
            var shardingGroup = new GroupBox
            {
                Text = "分表策略配置",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var shardingLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            shardingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            shardingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // 分表类型选择
            var typePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
            typePanel.Controls.Add(new Label { Text = "分表类型:", Margin = new Padding(0, 5, 0, 0) });
            shardingTypeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            shardingTypeBox.Items.AddRange(new object[] { "时间分表", "哈希分表", "列表分表", "组合键分表", "查询频率分表" });
            shardingTypeBox.SelectedIndex = 0;
            shardingTypeBox.SelectedIndexChanged += ShardingTypeBox_SelectedIndexChanged;
            shardingTypeBox.Width = 150;
            typePanel.Controls.Add(shardingTypeBox);
            shardingLayout.Controls.Add(typePanel, 0, 0);

            // 配置选项卡
            InitializeConfigTabs();
            shardingLayout.Controls.Add(configTabControl, 0, 1);

            shardingGroup.Controls.Add(shardingLayout);
            leftPanel.Controls.Add(shardingGroup, 0, 1);

            topPanel.Controls.Add(leftPanel, 0, 0);

            // 右侧：预览和操作
            var rightPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

            // 操作按钮
            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };

            previewButton.Text = "预览分表";
            previewButton.Width = 80;
            previewButton.Height = 35;
            previewButton.Click += PreviewButton_Click;
            buttonPanel.Controls.Add(previewButton);

            createTablesButton.Text = "创建分表";
            createTablesButton.Width = 80;
            createTablesButton.Height = 35;
            createTablesButton.Click += CreateTablesButton_Click;
            buttonPanel.Controls.Add(createTablesButton);

            startTaskButton.Text = "启动分表任务";
            startTaskButton.Width = 100;
            startTaskButton.Height = 35;
            startTaskButton.BackColor = Color.FromArgb(76, 175, 80);
            startTaskButton.ForeColor = Color.White;
            startTaskButton.Click += StartTaskButton_Click;
            buttonPanel.Controls.Add(startTaskButton);

            rightPanel.Controls.Add(buttonPanel, 0, 0);

            // 预览网格
            var previewGroup = new GroupBox
            {
                Text = "分表预览",
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };
            previewGrid.Dock = DockStyle.Fill;
            previewGrid.ReadOnly = true;
            previewGrid.AllowUserToAddRows = false;
            previewGroup.Controls.Add(previewGrid);
            rightPanel.Controls.Add(previewGroup, 0, 1);

            // 日志
            var logGroup = new GroupBox
            {
                Text = "操作日志",
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };
            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.ReadOnly = true;
            logGroup.Controls.Add(logBox);
            rightPanel.Controls.Add(logGroup, 0, 2);

            topPanel.Controls.Add(rightPanel, 1, 0);
            mainSplit.Panel1.Controls.Add(topPanel);

            // 下半部分：任务列表
            var taskTab = new TabControl { Dock = DockStyle.Fill };
            var taskPage = new TabPage("分表任务");
            _taskControl.Dock = DockStyle.Fill;
            taskPage.Controls.Add(_taskControl);
            taskTab.TabPages.Add(taskPage);
            mainSplit.Panel2.Controls.Add(taskTab);

            // 状态栏
            statusLabel.Text = "就绪";
            statusLabel.Dock = DockStyle.Bottom;

            this.Controls.Add(mainSplit);
            this.Controls.Add(statusLabel);
        }

        private void InitializeConfigTabs()
        {
            // 时间分表配置
            _timeConfigTab = new TabPage("时间分表");
            var timeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(10) };
            timeLayout.Controls.Add(new Label { Text = "时间字段:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            timeFieldBox.Text = "CreateTime";
            timeFieldBox.Dock = DockStyle.Fill;
            timeLayout.Controls.Add(timeFieldBox, 1, 0);
            timeLayout.Controls.Add(new Label { Text = "时间粒度:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            granularityBox.DropDownStyle = ComboBoxStyle.DropDownList;
            granularityBox.Items.AddRange(new object[] { "Day", "Week", "Month", "Quarter", "Year" });
            granularityBox.SelectedIndex = 2;
            granularityBox.Dock = DockStyle.Fill;
            timeLayout.Controls.Add(granularityBox, 1, 1);
            timeLayout.Controls.Add(new Label { Text = "起始时间:", TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            startTimePicker.Dock = DockStyle.Fill;
            startTimePicker.Format = DateTimePickerFormat.Short;
            startTimePicker.Value = new DateTime(2025, 1, 1);
            timeLayout.Controls.Add(startTimePicker, 1, 2);
            _timeConfigTab.Controls.Add(timeLayout);
            configTabControl.TabPages.Add(_timeConfigTab);

            // 哈希分表配置
            _hashConfigTab = new TabPage("哈希分表");
            var hashLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(10) };
            hashLayout.Controls.Add(new Label { Text = "哈希字段:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            hashFieldBox.Text = "UserId";
            hashFieldBox.Dock = DockStyle.Fill;
            hashLayout.Controls.Add(hashFieldBox, 1, 0);
            hashLayout.Controls.Add(new Label { Text = "分表数量:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            shardCountBox.Minimum = 1;
            shardCountBox.Maximum = 100;
            shardCountBox.Value = 4;
            shardCountBox.Dock = DockStyle.Fill;
            hashLayout.Controls.Add(shardCountBox, 1, 1);
            hashLayout.Controls.Add(new Label { Text = "哈希算法:", TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            hashAlgorithmBox.DropDownStyle = ComboBoxStyle.DropDownList;
            hashAlgorithmBox.Items.AddRange(new object[] { "Modulo", "Consistent", "Crc32" });
            hashAlgorithmBox.SelectedIndex = 0;
            hashAlgorithmBox.Dock = DockStyle.Fill;
            hashLayout.Controls.Add(hashAlgorithmBox, 1, 2);
            _hashConfigTab.Controls.Add(hashLayout);
            configTabControl.TabPages.Add(_hashConfigTab);

            // 列表分表配置
            _listConfigTab = new TabPage("列表分表");
            var listLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10) };
            listLayout.Controls.Add(new Label { Text = "列表字段:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            listFieldBox.Text = "Status";
            listFieldBox.Dock = DockStyle.Fill;
            listLayout.Controls.Add(listFieldBox, 1, 0);
            listLayout.Controls.Add(new Label { Text = "值映射:", TextAlign = ContentAlignment.TopRight }, 0, 1);
            listMappingGrid.Dock = DockStyle.Fill;
            listMappingGrid.AllowUserToAddRows = true;
            listMappingGrid.AllowUserToDeleteRows = true;
            listMappingGrid.ColumnCount = 2;
            listMappingGrid.Columns[0].HeaderText = "值";
            listMappingGrid.Columns[1].HeaderText = "表后缀";
            listMappingGrid.Rows.Add("Pending", "pending");
            listMappingGrid.Rows.Add("Processing", "processing");
            listMappingGrid.Rows.Add("Completed", "completed");
            listMappingGrid.Rows.Add("Cancelled", "cancelled");
            listLayout.Controls.Add(listMappingGrid, 1, 1);
            _listConfigTab.Controls.Add(listLayout);
            configTabControl.TabPages.Add(_listConfigTab);

            // 组合键分表配置
            _compositeConfigTab = new TabPage("组合键分表");
            var compositeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10) };
            compositeLayout.Controls.Add(new Label { Text = "组合字段:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            compositeFieldsBox.Text = "Region,CustomerType";
            compositeFieldsBox.Dock = DockStyle.Fill;
            compositeLayout.Controls.Add(compositeFieldsBox, 1, 0);
            compositeLayout.Controls.Add(new Label { Text = "分表数量:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            compositeShardCountBox.Minimum = 1;
            compositeShardCountBox.Maximum = 100;
            compositeShardCountBox.Value = 16;
            compositeShardCountBox.Dock = DockStyle.Fill;
            compositeLayout.Controls.Add(compositeShardCountBox, 1, 1);
            _compositeConfigTab.Controls.Add(compositeLayout);
            configTabControl.TabPages.Add(_compositeConfigTab);

            // 查询频率分表配置
            _frequencyConfigTab = new TabPage("查询频率分表");
            var frequencyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(10) };
            frequencyLayout.Controls.Add(new Label { Text = "频率字段:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            frequencyFieldBox.Text = "UserId";
            frequencyFieldBox.Dock = DockStyle.Fill;
            frequencyLayout.Controls.Add(frequencyFieldBox, 1, 0);
            frequencyLayout.Controls.Add(new Label { Text = "热数据阈值:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            hotThresholdBox.Minimum = 1;
            hotThresholdBox.Maximum = 1000000;
            hotThresholdBox.Value = 50;
            hotThresholdBox.Dock = DockStyle.Fill;
            frequencyLayout.Controls.Add(hotThresholdBox, 1, 1);
            frequencyLayout.Controls.Add(new Label { Text = "热数据后缀:", TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            hotSuffixBox.Text = "_hot";
            hotSuffixBox.Dock = DockStyle.Fill;
            frequencyLayout.Controls.Add(hotSuffixBox, 1, 2);
            frequencyLayout.Controls.Add(new Label { Text = "冷数据后缀:", TextAlign = ContentAlignment.MiddleRight }, 0, 3);
            coldSuffixBox.Text = "_cold";
            coldSuffixBox.Dock = DockStyle.Fill;
            frequencyLayout.Controls.Add(coldSuffixBox, 1, 3);
            _frequencyConfigTab.Controls.Add(frequencyLayout);
            configTabControl.TabPages.Add(_frequencyConfigTab);
        }

        private void ShardingTypeBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (shardingTypeBox.SelectedIndex)
            {
                case 0: configTabControl.SelectedTab = _timeConfigTab; break;
                case 1: configTabControl.SelectedTab = _hashConfigTab; break;
                case 2: configTabControl.SelectedTab = _listConfigTab; break;
                case 3: configTabControl.SelectedTab = _compositeConfigTab; break;
                case 4: configTabControl.SelectedTab = _frequencyConfigTab; break;
            }
        }

        private void LoadSourceTablesButton_Click(object sender, EventArgs e)
        {
            try
            {
                var connectionString = sourceConnectionStringBox.Text;

                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var tables = new List<string>();

                    var sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
                    using (var cmd = new SqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tables.Add(reader.GetString(0));
                        }
                    }

                    sourceTableCombo.Items.Clear();
                    sourceTableCombo.Items.AddRange(tables.ToArray());

                    if (tables.Count > 0)
                    {
                        sourceTableCombo.SelectedIndex = 0;
                    }

                    Log($"已加载 {tables.Count} 个表");
                }
            }
            catch (Exception ex)
            {
                Log($"加载表失败: {ex.Message}");
                MessageBox.Show($"加载表失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private ShardingConfig BuildShardingConfig()
        {
            var config = new ShardingConfig
            {
                BaseTableName = sourceTableCombo.Text
            };

            switch (shardingTypeBox.SelectedIndex)
            {
                case 0: // 时间分表
                    config.ShardingType = ShardingType.Time;
                    config.TimeConfig = new TimeShardingConfig
                    {
                        TimeField = timeFieldBox.Text,
                        Granularity = Enum.Parse<TimeGranularity>(granularityBox.SelectedItem.ToString()),
                        StartTime = startTimePicker.Value
                    };
                    break;

                case 1: // 哈希分表
                    config.ShardingType = ShardingType.Hash;
                    config.HashConfig = new HashShardingConfig
                    {
                        HashField = hashFieldBox.Text,
                        ShardCount = (int)shardCountBox.Value,
                        Algorithm = Enum.Parse<HashAlgorithm>(hashAlgorithmBox.SelectedItem.ToString())
                    };
                    break;

                case 2: // 列表分表
                    config.ShardingType = ShardingType.List;
                    var valueMapping = new Dictionary<string, string>();
                    foreach (DataGridViewRow row in listMappingGrid.Rows)
                    {
                        if (row.Cells[0].Value != null && row.Cells[1].Value != null)
                        {
                            valueMapping[row.Cells[0].Value.ToString()] = row.Cells[1].Value.ToString();
                        }
                    }
                    config.ListConfig = new ListShardingConfig
                    {
                        ListField = listFieldBox.Text,
                        ValueMapping = valueMapping
                    };
                    break;

                case 3: // 组合键分表
                    config.ShardingType = ShardingType.Composite;
                    config.CompositeConfig = new CompositeShardingConfig
                    {
                        CompositeFields = compositeFieldsBox.Text.Split(',').Select(f => f.Trim()).ToList(),
                        UseHash = true,
                        ShardCount = (int)compositeShardCountBox.Value
                    };
                    break;

                case 4: // 查询频率分表
                    config.ShardingType = ShardingType.QueryFrequency;
                    config.FrequencyConfig = new QueryFrequencyShardingConfig
                    {
                        Field = frequencyFieldBox.Text,
                        HotThreshold = (long)hotThresholdBox.Value,
                        HotSuffix = hotSuffixBox.Text,
                        ColdSuffix = coldSuffixBox.Text,
                        ColdShardingType = ColdShardingType.ByHash,
                        ColdShardCount = 4
                    };
                    break;
            }

            return config;
        }

        private void PreviewButton_Click(object sender, EventArgs e)
        {
            try
            {
                _currentConfig = BuildShardingConfig();
                ShardingManager.Configure<object>(_currentConfig);

                var tableNames = ShardingManager.GetAllTableNames<object>();

                previewGrid.DataSource = null;
                previewGrid.Columns.Clear();
                previewGrid.Columns.Add("TableName", "分表名称");
                previewGrid.Columns.Add("Type", "分表类型");
                previewGrid.Columns.Add("Status", "状态");

                foreach (var tableName in tableNames)
                {
                    previewGrid.Rows.Add(tableName, _currentConfig.ShardingType.ToString(), "待创建");
                }

                Log($"预览: 将创建 {tableNames.Count} 个分表");
                statusLabel.Text = $"预览: {tableNames.Count} 个分表";
            }
            catch (Exception ex)
            {
                Log($"预览失败: {ex.Message}");
                MessageBox.Show($"预览失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateTablesButton_Click(object sender, EventArgs e)
        {
            if (_currentConfig == null)
            {
                MessageBox.Show("请先预览分表配置", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var tableNames = ShardingManager.GetAllTableNames<object>();
                var connectionString = sourceConnectionStringBox.Text;

                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // 获取源表结构
                    var schemaSql = $@"
                        SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
                        FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_NAME = '{_currentConfig.BaseTableName}'
                        ORDER BY ORDINAL_POSITION";

                    var columns = new List<string>();
                    using (var cmd = new SqlCommand(schemaSql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var colName = reader.GetString(0);
                            var dataType = reader.GetString(1);
                            var maxLength = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                            var isNullable = reader.GetString(3) == "YES";

                            var columnDef = $"[{colName}] {dataType}";
                            if (maxLength.HasValue && maxLength.Value > 0)
                            {
                                columnDef += $"({maxLength.Value})";
                            }
                            if (!isNullable)
                            {
                                columnDef += " NOT NULL";
                            }

                            columns.Add(columnDef);
                        }
                    }

                    foreach (var tableName in tableNames)
                    {
                        var createSql = $@"
                            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='{tableName}' AND xtype='U')
                            CREATE TABLE [{tableName}] (
                                {string.Join(",\n                                ", columns)}
                            );";

                        using (var cmd = new SqlCommand(createSql, conn))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        Log($"创建分表: {tableName}");
                    }
                }

                Log($"分表创建完成: {tableNames.Count} 个表");
                statusLabel.Text = $"分表创建完成: {tableNames.Count} 个表";

                // 更新预览状态
                foreach (DataGridViewRow row in previewGrid.Rows)
                {
                    row.Cells["Status"].Value = "已创建";
                }
            }
            catch (Exception ex)
            {
                Log($"创建分表失败: {ex.Message}");
                MessageBox.Show($"创建分表失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartTaskButton_Click(object sender, EventArgs e)
        {
            try
            {
                _currentConfig = BuildShardingConfig();
                _currentConnectionString = sourceConnectionStringBox.Text;

                ShardingManager.Configure<object>(_currentConfig);

                var sourceTable = sourceTableCombo.Text;
                var taskName = $"分表任务: {sourceTable} -> {_currentConfig.ShardingType}";

                var task = _taskService.StartTask(
                    taskName,
                    sourceTable,
                    _currentConnectionString,
                    _currentConfig);

                Log($"分表任务已启动: {taskName} (ID: {task.TaskId})");
                statusLabel.Text = $"任务已启动: {taskName}";

                MessageBox.Show($"分表任务已启动!\n\n任务名称: {taskName}\n任务ID: {task.TaskId}\n\n请在任务列表中查看进度。",
                    "任务启动成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log($"启动任务失败: {ex.Message}");
                MessageBox.Show($"启动任务失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            logBox.AppendText($"[{timestamp}] {message}\r\n");
        }
    }
}
