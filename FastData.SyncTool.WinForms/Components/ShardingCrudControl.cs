using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FastData.Sharding;
using FastData.SyncTool.WinForms.Services;

namespace FastData.SyncTool.WinForms.Components
{
    /// <summary>
    /// 分表管理组件
    /// 提供分表的配置、查询、写入、更新、删除功能
    /// </summary>
    public class ShardingCrudControl : UserControl
    {
        private readonly DatabaseService _dbService;
        private readonly LogService _logService;

        // 连接字符串（运行时由用户在界面填写，禁止在源码中硬编码凭据）
        private string _connectionString = string.Empty;

        // 配置区域
        private GroupBox _configGroup;
        private ComboBox _entityTypeCombo;
        private ComboBox _shardingTypeCombo;
        private TextBox _baseTableNameText;
        private TextBox _connectionStringText;
        private TabControl _shardingConfigTabs;

        // 时间分表配置
        private TabPage _timeTab;
        private TextBox _timeFieldText;
        private ComboBox _timeGranularityCombo;
        private DateTimePicker _startTimePicker;

        // 哈希分表配置
        private TabPage _hashTab;
        private TextBox _hashFieldText;
        private NumericUpDown _shardCountNumeric;
        private ComboBox _hashAlgorithmCombo;

        // 列表分表配置
        private TabPage _listTab;
        private TextBox _listFieldText;
        private DataGridView _listMappingGrid;

        // 组合键分表配置
        private TabPage _compositeTab;
        private TextBox _compositeFieldsText;
        private NumericUpDown _compositeShardCountNumeric;

        // 查询频率分表配置
        private TabPage _frequencyTab;
        private TextBox _frequencyFieldText;
        private NumericUpDown _hotThresholdNumeric;
        private TextBox _hotSuffixText;
        private TextBox _coldSuffixText;

        // 操作按钮
        private Button _configButton;
        private Button _clearButton;
        private Button _refreshButton;

        // 查询区域
        private GroupBox _queryGroup;
        private DataGridView _tableListGrid;
        private Button _queryAllButton;
        private Button _queryByConditionButton;

        // 数据操作区域
        private GroupBox _dataGroup;
        private DataGridView _dataGrid;
        private TabControl _operationTabs;

        // 查询 Tab
        private TabPage _queryTab;
        private TextBox _sqlText;
        private Button _executeQueryButton;

        // 插入 Tab
        private TabPage _insertTab;
        private TextBox _insertTableNameText;
        private DataGridView _insertDataGrid;
        private Button _insertButton;

        // 更新 Tab
        private TabPage _updateTab;
        private TextBox _updateTableNameText;
        private TextBox _updateSetClauseText;
        private TextBox _updateWhereClauseText;
        private Button _updateButton;

        // 删除 Tab
        private TabPage _deleteTab;
        private TextBox _deleteTableNameText;
        private TextBox _deleteWhereClauseText;
        private Button _deleteButton;

        // 状态栏
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private ToolStripStatusLabel _tableCountLabel;

        public ShardingCrudControl(DatabaseService dbService, LogService logService)
        {
            _dbService = dbService;
            _logService = logService;
            InitializeComponent();
            LoadEntityTypes();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            // 主布局
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(10)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            // 配置区域
            _configGroup = new GroupBox
            {
                Text = "分表配置",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var configLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 9
            };

            // 连接字符串
            configLayout.Controls.Add(new Label { Text = "连接字符串:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            _connectionStringText = new TextBox { Dock = DockStyle.Fill, Text = _connectionString };
            configLayout.Controls.Add(_connectionStringText, 1, 0);

            // 实体类型
            configLayout.Controls.Add(new Label { Text = "实体类型:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            _entityTypeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _entityTypeCombo.SelectedIndexChanged += EntityTypeChanged;
            configLayout.Controls.Add(_entityTypeCombo, 1, 1);

            // 分表类型
            configLayout.Controls.Add(new Label { Text = "分表类型:", TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            _shardingTypeCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DataSource = Enum.GetValues(typeof(ShardingType))
            };
            _shardingTypeCombo.SelectedIndexChanged += ShardingTypeChanged;
            configLayout.Controls.Add(_shardingTypeCombo, 1, 2);

            // 基础表名
            configLayout.Controls.Add(new Label { Text = "基础表名:", TextAlign = ContentAlignment.MiddleRight }, 0, 3);
            _baseTableNameText = new TextBox { Dock = DockStyle.Fill };
            configLayout.Controls.Add(_baseTableNameText, 1, 3);

            // 分表配置选项卡
            _shardingConfigTabs = new TabControl { Dock = DockStyle.Fill };
            configLayout.Controls.Add(_shardingConfigTabs, 0, 4);
            configLayout.SetColumnSpan(_shardingConfigTabs, 2);
            configLayout.SetRowSpan(_shardingConfigTabs, 3);

            // 时间分表配置
            _timeTab = new TabPage("时间分表");
            var timeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            timeLayout.Controls.Add(new Label { Text = "时间字段:" }, 0, 0);
            _timeFieldText = new TextBox { Dock = DockStyle.Fill };
            timeLayout.Controls.Add(_timeFieldText, 1, 0);
            timeLayout.Controls.Add(new Label { Text = "时间粒度:" }, 0, 1);
            _timeGranularityCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DataSource = Enum.GetValues(typeof(TimeGranularity))
            };
            timeLayout.Controls.Add(_timeGranularityCombo, 1, 1);
            timeLayout.Controls.Add(new Label { Text = "起始时间:" }, 0, 2);
            _startTimePicker = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short };
            timeLayout.Controls.Add(_startTimePicker, 1, 2);
            _timeTab.Controls.Add(timeLayout);
            _shardingConfigTabs.TabPages.Add(_timeTab);

            // 哈希分表配置
            _hashTab = new TabPage("哈希分表");
            var hashLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            hashLayout.Controls.Add(new Label { Text = "哈希字段:" }, 0, 0);
            _hashFieldText = new TextBox { Dock = DockStyle.Fill };
            hashLayout.Controls.Add(_hashFieldText, 1, 0);
            hashLayout.Controls.Add(new Label { Text = "分表数量:" }, 0, 1);
            _shardCountNumeric = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 1000, Value = 8 };
            hashLayout.Controls.Add(_shardCountNumeric, 1, 1);
            hashLayout.Controls.Add(new Label { Text = "哈希算法:" }, 0, 2);
            _hashAlgorithmCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DataSource = Enum.GetValues(typeof(HashAlgorithm))
            };
            hashLayout.Controls.Add(_hashAlgorithmCombo, 1, 2);
            _hashTab.Controls.Add(hashLayout);
            _shardingConfigTabs.TabPages.Add(_hashTab);

            // 列表分表配置
            _listTab = new TabPage("列表分表");
            var listLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            listLayout.Controls.Add(new Label { Text = "列表字段:" }, 0, 0);
            _listFieldText = new TextBox { Dock = DockStyle.Fill };
            listLayout.Controls.Add(_listFieldText, 1, 0);
            listLayout.Controls.Add(new Label { Text = "值映射:" }, 0, 1);
            _listMappingGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                ColumnCount = 2,
                Columns =
                {
                    new DataGridViewTextBoxColumn { HeaderText = "值", Name = "Value" },
                    new DataGridViewTextBoxColumn { HeaderText = "表后缀", Name = "Suffix" }
                }
            };
            listLayout.Controls.Add(_listMappingGrid, 1, 1);
            _listTab.Controls.Add(listLayout);
            _shardingConfigTabs.TabPages.Add(_listTab);

            // 组合键分表配置
            _compositeTab = new TabPage("组合键分表");
            var compositeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            compositeLayout.Controls.Add(new Label { Text = "组合字段:" }, 0, 0);
            _compositeFieldsText = new TextBox { Dock = DockStyle.Fill };
            compositeLayout.Controls.Add(_compositeFieldsText, 1, 0);
            compositeLayout.Controls.Add(new Label { Text = "分表数量:" }, 0, 1);
            _compositeShardCountNumeric = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 1000, Value = 16 };
            compositeLayout.Controls.Add(_compositeShardCountNumeric, 1, 1);
            _compositeTab.Controls.Add(compositeLayout);
            _shardingConfigTabs.TabPages.Add(_compositeTab);

            // 查询频率分表配置
            _frequencyTab = new TabPage("查询频率分表");
            var frequencyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
            frequencyLayout.Controls.Add(new Label { Text = "频率字段:" }, 0, 0);
            _frequencyFieldText = new TextBox { Dock = DockStyle.Fill };
            frequencyLayout.Controls.Add(_frequencyFieldText, 1, 0);
            frequencyLayout.Controls.Add(new Label { Text = "热数据阈值:" }, 0, 1);
            _hotThresholdNumeric = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 1000000, Value = 100 };
            frequencyLayout.Controls.Add(_hotThresholdNumeric, 1, 1);
            frequencyLayout.Controls.Add(new Label { Text = "热数据后缀:" }, 0, 2);
            _hotSuffixText = new TextBox { Dock = DockStyle.Fill, Text = "_hot" };
            frequencyLayout.Controls.Add(_hotSuffixText, 1, 2);
            frequencyLayout.Controls.Add(new Label { Text = "冷数据后缀:" }, 0, 3);
            _coldSuffixText = new TextBox { Dock = DockStyle.Fill, Text = "_cold" };
            frequencyLayout.Controls.Add(_coldSuffixText, 1, 3);
            _frequencyTab.Controls.Add(frequencyLayout);
            _shardingConfigTabs.TabPages.Add(_frequencyTab);

            // 操作按钮
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight
            };
            _configButton = new Button { Text = "应用配置", Width = 80, Height = 30 };
            _configButton.Click += ApplyConfig;
            buttonPanel.Controls.Add(_configButton);

            _clearButton = new Button { Text = "清除配置", Width = 80, Height = 30 };
            _clearButton.Click += ClearConfig;
            buttonPanel.Controls.Add(_clearButton);

            _refreshButton = new Button { Text = "刷新", Width = 60, Height = 30 };
            _refreshButton.Click += (s, e) => RefreshTableList();
            buttonPanel.Controls.Add(_refreshButton);

            configLayout.Controls.Add(buttonPanel, 0, 7);
            configLayout.SetColumnSpan(buttonPanel, 2);

            _configGroup.Controls.Add(configLayout);
            mainLayout.Controls.Add(_configGroup, 0, 0);

            // 查询区域
            _queryGroup = new GroupBox
            {
                Text = "分表列表",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var queryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };

            var queryButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _queryAllButton = new Button { Text = "查询所有表", Width = 100, Height = 30 };
            _queryAllButton.Click += QueryAllTables;
            queryButtonPanel.Controls.Add(_queryAllButton);

            _queryByConditionButton = new Button { Text = "按条件查询", Width = 100, Height = 30 };
            _queryByConditionButton.Click += QueryByCondition;
            queryButtonPanel.Controls.Add(_queryByConditionButton);

            queryLayout.Controls.Add(queryButtonPanel, 0, 0);

            _tableListGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Columns =
                {
                    new DataGridViewTextBoxColumn { HeaderText = "表名", Name = "TableName", Width = 200 },
                    new DataGridViewTextBoxColumn { HeaderText = "类型", Name = "Type", Width = 100 },
                    new DataGridViewTextBoxColumn { HeaderText = "状态", Name = "Status", Width = 100 }
                }
            };
            _tableListGrid.CellDoubleClick += TableListGrid_CellDoubleClick;
            queryLayout.Controls.Add(_tableListGrid, 0, 1);

            _queryGroup.Controls.Add(queryLayout);
            mainLayout.Controls.Add(_queryGroup, 1, 0);

            // 数据操作区域
            _dataGroup = new GroupBox
            {
                Text = "数据操作（增删改查）",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var dataLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };

            // 操作选项卡
            _operationTabs = new TabControl { Dock = DockStyle.Fill };

            // 查询 Tab
            _queryTab = new TabPage("查询");
            var queryTabLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            _sqlText = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                Height = 60,
                Text = "SELECT * FROM "
            };
            queryTabLayout.Controls.Add(_sqlText, 0, 0);
            _executeQueryButton = new Button { Text = "执行查询", Width = 100, Height = 30 };
            _executeQueryButton.Click += ExecuteQuery;
            queryTabLayout.Controls.Add(_executeQueryButton, 0, 1);
            var queryResultGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
            queryTabLayout.Controls.Add(queryResultGrid, 0, 2);
            _queryTab.Controls.Add(queryTabLayout);
            _operationTabs.TabPages.Add(_queryTab);

            // 插入 Tab
            _insertTab = new TabPage("插入");
            var insertTabLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
            insertTabLayout.Controls.Add(new Label { Text = "目标表名:" }, 0, 0);
            _insertTableNameText = new TextBox { Dock = DockStyle.Fill };
            insertTabLayout.Controls.Add(_insertTableNameText, 1, 0);
            insertTabLayout.Controls.Add(new Label { Text = "数据（列=值，每行一条）:" }, 0, 1);
            insertTabLayout.SetColumnSpan(insertTabLayout.GetControlFromPosition(0, 1), 2);
            _insertDataGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                ColumnCount = 1,
                Columns = { new DataGridViewTextBoxColumn { HeaderText = "列名=值", Name = "ColumnValue" } }
            };
            insertTabLayout.Controls.Add(_insertDataGrid, 0, 2);
            insertTabLayout.SetColumnSpan(_insertDataGrid, 2);
            _insertButton = new Button { Text = "执行插入", Width = 100, Height = 30 };
            _insertButton.Click += ExecuteInsert;
            insertTabLayout.Controls.Add(_insertButton, 1, 3);
            _insertTab.Controls.Add(insertTabLayout);
            _operationTabs.TabPages.Add(_insertTab);

            // 更新 Tab
            _updateTab = new TabPage("更新");
            var updateTabLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
            updateTabLayout.Controls.Add(new Label { Text = "目标表名:" }, 0, 0);
            _updateTableNameText = new TextBox { Dock = DockStyle.Fill };
            updateTabLayout.Controls.Add(_updateTableNameText, 1, 0);
            updateTabLayout.Controls.Add(new Label { Text = "SET 子句:" }, 0, 1);
            _updateSetClauseText = new TextBox { Dock = DockStyle.Fill, Text = "Column1=Value1" };
            updateTabLayout.Controls.Add(_updateSetClauseText, 1, 1);
            updateTabLayout.Controls.Add(new Label { Text = "WHERE 子句:" }, 0, 2);
            _updateWhereClauseText = new TextBox { Dock = DockStyle.Fill, Text = "Id=1" };
            updateTabLayout.Controls.Add(_updateWhereClauseText, 1, 2);
            _updateButton = new Button { Text = "执行更新", Width = 100, Height = 30 };
            _updateButton.Click += ExecuteUpdate;
            updateTabLayout.Controls.Add(_updateButton, 1, 3);
            _updateTab.Controls.Add(updateTabLayout);
            _operationTabs.TabPages.Add(_updateTab);

            // 删除 Tab
            _deleteTab = new TabPage("删除");
            var deleteTabLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            deleteTabLayout.Controls.Add(new Label { Text = "目标表名:" }, 0, 0);
            _deleteTableNameText = new TextBox { Dock = DockStyle.Fill };
            deleteTabLayout.Controls.Add(_deleteTableNameText, 1, 0);
            deleteTabLayout.Controls.Add(new Label { Text = "WHERE 子句:" }, 0, 1);
            _deleteWhereClauseText = new TextBox { Dock = DockStyle.Fill, Text = "Id=1" };
            deleteTabLayout.Controls.Add(_deleteWhereClauseText, 1, 1);
            _deleteButton = new Button { Text = "执行删除", Width = 100, Height = 30 };
            _deleteButton.Click += ExecuteDelete;
            deleteTabLayout.Controls.Add(_deleteButton, 1, 2);
            _deleteTab.Controls.Add(deleteTabLayout);
            _operationTabs.TabPages.Add(_deleteTab);

            dataLayout.Controls.Add(_operationTabs, 0, 0);

            // 数据网格
            _dataGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false
            };
            dataLayout.Controls.Add(_dataGrid, 0, 1);

            _dataGroup.Controls.Add(dataLayout);
            mainLayout.Controls.Add(_dataGroup, 0, 1);
            mainLayout.SetColumnSpan(_dataGroup, 2);

            // 状态栏
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel { Text = "就绪" };
            _tableCountLabel = new ToolStripStatusLabel { Text = "表数量: 0" };
            _statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel, _tableCountLabel });

            Controls.Add(mainLayout);
            Controls.Add(_statusStrip);

            ResumeLayout();
        }

        private void LoadEntityTypes()
        {
            try
            {
                _entityTypeCombo.Items.AddRange(new object[] { "UserLog", "Order", "Custom" });
                _entityTypeCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                _logService.Error($"加载实体类型失败: {ex.Message}");
            }
        }

        private void EntityTypeChanged(object sender, EventArgs e)
        {
            switch (_entityTypeCombo.SelectedItem?.ToString())
            {
                case "UserLog":
                    _baseTableNameText.Text = "UserLog";
                    _timeFieldText.Text = "CreateTime";
                    _hashFieldText.Text = "UserId";
                    _frequencyFieldText.Text = "UserId";
                    break;
                case "Order":
                    _baseTableNameText.Text = "Order";
                    _timeFieldText.Text = "OrderTime";
                    _hashFieldText.Text = "OrderNo";
                    _listFieldText.Text = "Status";
                    _compositeFieldsText.Text = "Region,CustomerType";
                    break;
            }
        }

        private void ShardingTypeChanged(object sender, EventArgs e)
        {
            if (_shardingTypeCombo.SelectedItem is ShardingType shardingType)
            {
                switch (shardingType)
                {
                    case ShardingType.Time:
                        _shardingConfigTabs.SelectedTab = _timeTab;
                        break;
                    case ShardingType.Hash:
                        _shardingConfigTabs.SelectedTab = _hashTab;
                        break;
                    case ShardingType.List:
                        _shardingConfigTabs.SelectedTab = _listTab;
                        break;
                    case ShardingType.Composite:
                        _shardingConfigTabs.SelectedTab = _compositeTab;
                        break;
                    case ShardingType.QueryFrequency:
                        _shardingConfigTabs.SelectedTab = _frequencyTab;
                        break;
                }
            }
        }

        private void ApplyConfig(object sender, EventArgs e)
        {
            try
            {
                _connectionString = _connectionStringText.Text;
                var shardingType = (ShardingType)_shardingTypeCombo.SelectedItem;
                var config = new ShardingConfig
                {
                    BaseTableName = _baseTableNameText.Text,
                    ShardingType = shardingType
                };

                switch (shardingType)
                {
                    case ShardingType.Time:
                        config.TimeConfig = new TimeShardingConfig
                        {
                            TimeField = _timeFieldText.Text,
                            Granularity = (TimeGranularity)_timeGranularityCombo.SelectedItem,
                            StartTime = _startTimePicker.Value
                        };
                        break;

                    case ShardingType.Hash:
                        config.HashConfig = new HashShardingConfig
                        {
                            HashField = _hashFieldText.Text,
                            ShardCount = (int)_shardCountNumeric.Value,
                            Algorithm = (HashAlgorithm)_hashAlgorithmCombo.SelectedItem
                        };
                        break;

                    case ShardingType.List:
                        var valueMapping = new Dictionary<string, string>();
                        foreach (DataGridViewRow row in _listMappingGrid.Rows)
                        {
                            if (row.Cells["Value"].Value != null && row.Cells["Suffix"].Value != null)
                            {
                                valueMapping[row.Cells["Value"].Value.ToString()] = row.Cells["Suffix"].Value.ToString();
                            }
                        }
                        config.ListConfig = new ListShardingConfig
                        {
                            ListField = _listFieldText.Text,
                            ValueMapping = valueMapping
                        };
                        break;

                    case ShardingType.Composite:
                        var fields = _compositeFieldsText.Text.Split(',').Select(f => f.Trim()).ToList();
                        config.CompositeConfig = new CompositeShardingConfig
                        {
                            CompositeFields = fields,
                            UseHash = true,
                            ShardCount = (int)_compositeShardCountNumeric.Value
                        };
                        break;

                    case ShardingType.QueryFrequency:
                        config.FrequencyConfig = new QueryFrequencyShardingConfig
                        {
                            Field = _frequencyFieldText.Text,
                            HotThreshold = (long)_hotThresholdNumeric.Value,
                            HotSuffix = _hotSuffixText.Text,
                            ColdSuffix = _coldSuffixText.Text,
                            ColdShardingType = ColdShardingType.ByHash,
                            ColdShardCount = 4
                        };
                        break;
                }

                // 注册配置
                ShardingManager.Configure<object>(config);

                _statusLabel.Text = $"配置已应用: {config.BaseTableName} ({shardingType})";
                _logService.Info($"分表配置已应用: {config.BaseTableName} ({shardingType})");

                RefreshTableList();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"配置失败: {ex.Message}";
                _logService.Error($"应用分表配置失败: {ex.Message}");
                MessageBox.Show($"配置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearConfig(object sender, EventArgs e)
        {
            ShardingManager.Clear();
            _statusLabel.Text = "配置已清除";
            _tableListGrid.Rows.Clear();
            _dataGrid.DataSource = null;
            _logService.Info("分表配置已清除");
        }

        private void RefreshTableList()
        {
            try
            {
                _tableListGrid.Rows.Clear();

                if (!ShardingManager.IsShardingEnabled<object>())
                {
                    _statusLabel.Text = "未配置分表";
                    return;
                }

                var tableNames = ShardingManager.GetAllTableNames<object>();
                foreach (var tableName in tableNames)
                {
                    _tableListGrid.Rows.Add(tableName, "分表", "就绪");
                }

                _tableCountLabel.Text = $"表数量: {tableNames.Count}";
                _statusLabel.Text = $"已加载 {tableNames.Count} 个分表";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"刷新失败: {ex.Message}";
                _logService.Error($"刷新分表列表失败: {ex.Message}");
            }
        }

        private void QueryAllTables(object sender, EventArgs e)
        {
            RefreshTableList();
        }

        private void QueryByCondition(object sender, EventArgs e)
        {
            try
            {
                if (!ShardingManager.IsShardingEnabled<object>())
                {
                    MessageBox.Show("请先配置分表", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 弹出条件输入对话框
                var dialog = new Form
                {
                    Text = "输入查询条件",
                    Size = new Size(400, 200),
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false
                };

                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(10) };
                layout.Controls.Add(new Label { Text = "字段名:" }, 0, 0);
                var fieldText = new TextBox { Dock = DockStyle.Fill };
                layout.Controls.Add(fieldText, 1, 0);

                layout.Controls.Add(new Label { Text = "字段值:" }, 0, 1);
                var valueText = new TextBox { Dock = DockStyle.Fill };
                layout.Controls.Add(valueText, 1, 1);

                var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var okButton = new Button { Text = "确定", DialogResult = DialogResult.OK };
                buttonPanel.Controls.Add(okButton);
                var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
                buttonPanel.Controls.Add(cancelButton);
                layout.Controls.Add(buttonPanel, 0, 2);
                layout.SetColumnSpan(buttonPanel, 2);

                dialog.Controls.Add(layout);
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var fieldName = fieldText.Text;
                    var fieldValue = valueText.Text;

                    if (!string.IsNullOrEmpty(fieldName) && !string.IsNullOrEmpty(fieldValue))
                    {
                        var queryParams = new Dictionary<string, object> { { fieldName, fieldValue } };
                        var tableNames = ShardingManager.GetTableNames<object>(queryParams);

                        _tableListGrid.Rows.Clear();
                        foreach (var tableName in tableNames)
                        {
                            _tableListGrid.Rows.Add(tableName, "分表", "匹配");
                        }

                        _statusLabel.Text = $"查询到 {tableNames.Count} 个匹配的分表";
                    }
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"查询失败: {ex.Message}";
                _logService.Error($"按条件查询分表失败: {ex.Message}");
            }
        }

        private void TableListGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                var tableName = _tableListGrid.Rows[e.RowIndex].Cells["TableName"].Value?.ToString();
                if (!string.IsNullOrEmpty(tableName))
                {
                    _sqlText.Text = $"SELECT * FROM [{tableName}] WHERE 1=1";
                    _insertTableNameText.Text = tableName;
                    _updateTableNameText.Text = tableName;
                    _deleteTableNameText.Text = tableName;
                }
            }
        }

        private void ExecuteQuery(object sender, EventArgs e)
        {
            try
            {
                var sql = _sqlText.Text.Trim();
                if (string.IsNullOrEmpty(sql))
                {
                    MessageBox.Show("请输入 SQL 语句", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _statusLabel.Text = $"执行查询...";
                Application.DoEvents();

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        var dataTable = new DataTable();
                        using (var adapter = new SqlDataAdapter(cmd))
                        {
                            adapter.Fill(dataTable);
                        }

                        _dataGrid.DataSource = dataTable;
                        _statusLabel.Text = $"查询完成: {dataTable.Rows.Count} 条记录";
                        _logService.Info($"执行分表查询: {sql} - 返回 {dataTable.Rows.Count} 条");
                    }
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"查询失败: {ex.Message}";
                _logService.Error($"执行分表查询失败: {ex.Message}");
                MessageBox.Show($"查询失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExecuteInsert(object sender, EventArgs e)
        {
            try
            {
                var tableName = _insertTableNameText.Text.Trim();
                if (string.IsNullOrEmpty(tableName))
                {
                    MessageBox.Show("请输入目标表名", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 解析列和值
                var columns = new List<string>();
                var values = new List<string>();

                foreach (DataGridViewRow row in _insertDataGrid.Rows)
                {
                    if (row.Cells["ColumnValue"].Value != null)
                    {
                        var cellValue = row.Cells["ColumnValue"].Value.ToString();
                        var parts = cellValue.Split('=');
                        if (parts.Length == 2)
                        {
                            columns.Add(parts[0].Trim());
                            values.Add(parts[1].Trim());
                        }
                    }
                }

                if (columns.Count == 0)
                {
                    MessageBox.Show("请输入要插入的数据", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var sql = $"INSERT INTO [{tableName}] ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values.Select(v => $"'{v}'"))})";

                _statusLabel.Text = "执行插入...";
                Application.DoEvents();

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        var affectedRows = cmd.ExecuteNonQuery();
                        _statusLabel.Text = $"插入成功: {affectedRows} 条记录";
                        _logService.Info($"执行分表插入: {sql} - 影响 {affectedRows} 条");
                        MessageBox.Show($"插入成功: {affectedRows} 条记录", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"插入失败: {ex.Message}";
                _logService.Error($"执行分表插入失败: {ex.Message}");
                MessageBox.Show($"插入失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExecuteUpdate(object sender, EventArgs e)
        {
            try
            {
                var tableName = _updateTableNameText.Text.Trim();
                var setClause = _updateSetClauseText.Text.Trim();
                var whereClause = _updateWhereClauseText.Text.Trim();

                if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(setClause))
                {
                    MessageBox.Show("请输入目标表名和 SET 子句", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var sql = $"UPDATE [{tableName}] SET {setClause}";
                if (!string.IsNullOrEmpty(whereClause))
                {
                    sql += $" WHERE {whereClause}";
                }

                _statusLabel.Text = "执行更新...";
                Application.DoEvents();

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        var affectedRows = cmd.ExecuteNonQuery();
                        _statusLabel.Text = $"更新成功: {affectedRows} 条记录";
                        _logService.Info($"执行分表更新: {sql} - 影响 {affectedRows} 条");
                        MessageBox.Show($"更新成功: {affectedRows} 条记录", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"更新失败: {ex.Message}";
                _logService.Error($"执行分表更新失败: {ex.Message}");
                MessageBox.Show($"更新失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExecuteDelete(object sender, EventArgs e)
        {
            try
            {
                var tableName = _deleteTableNameText.Text.Trim();
                var whereClause = _deleteWhereClauseText.Text.Trim();

                if (string.IsNullOrEmpty(tableName))
                {
                    MessageBox.Show("请输入目标表名", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(whereClause))
                {
                    var confirmResult = MessageBox.Show("确定要删除所有数据吗？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (confirmResult != DialogResult.Yes)
                        return;
                }

                var sql = $"DELETE FROM [{tableName}]";
                if (!string.IsNullOrEmpty(whereClause))
                {
                    sql += $" WHERE {whereClause}";
                }

                _statusLabel.Text = "执行删除...";
                Application.DoEvents();

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        var affectedRows = cmd.ExecuteNonQuery();
                        _statusLabel.Text = $"删除成功: {affectedRows} 条记录";
                        _logService.Info($"执行分表删除: {sql} - 影响 {affectedRows} 条");
                        MessageBox.Show($"删除成功: {affectedRows} 条记录", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"删除失败: {ex.Message}";
                _logService.Error($"执行分表删除失败: {ex.Message}");
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
