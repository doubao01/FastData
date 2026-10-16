using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FastData.Sharding;

namespace FastData.SyncTool.WinForms.Components
{
    /// <summary>
    /// 分表数据导入控件
    /// 支持CSV/Excel导入，自动分表路由，Upsert逻辑
    /// </summary>
    public class ShardingImportControl : UserControl
    {
        // 连接配置
        private readonly TextBox _connectionStringBox = new TextBox();

        // 分表配置
        private readonly ComboBox _shardingTypeCombo = new ComboBox();
        private readonly ComboBox _entityTypeCombo = new ComboBox();

        // 文件选择
        private readonly TextBox _filePathBox = new TextBox();
        private readonly Button _browseButton = new Button();
        private readonly ComboBox _fileFormatCombo = new ComboBox();

        // 导入设置
        private readonly ComboBox _primaryKeyCombo = new ComboBox();
        private readonly CheckBox _hasHeaderBox = new CheckBox();
        private readonly ComboBox _encodingCombo = new ComboBox();

        // 预览
        private DataGridView _previewGrid;
        private Label _previewCountLabel;

        // 导入进度
        private ProgressBar _progressBar;
        private Label _progressLabel;

        // 操作按钮
        private Button _previewButton;
        private Button _importButton;
        private Button _cancelButton;

        // 状态
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;

        // 导入任务
        private bool _isImporting;

        public ShardingImportControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            // 主布局
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(10)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

            // 数据库配置
            var dbGroup = new GroupBox
            {
                Text = "数据库配置",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var dbLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3
            };

            dbLayout.Controls.Add(new Label { Text = "连接字符串:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            _connectionStringBox.Text = string.Empty;
            _connectionStringBox.Dock = DockStyle.Fill;
            dbLayout.Controls.Add(_connectionStringBox, 1, 0);

            dbLayout.Controls.Add(new Label { Text = "实体类型:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            _entityTypeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _entityTypeCombo.Items.AddRange(new object[] { "UserLog", "OrderData", "自定义" });
            _entityTypeCombo.SelectedIndex = 0;
            _entityTypeCombo.SelectedIndexChanged += EntityTypeChanged;
            _entityTypeCombo.Dock = DockStyle.Fill;
            dbLayout.Controls.Add(_entityTypeCombo, 1, 1);

            dbLayout.Controls.Add(new Label { Text = "分表策略:", TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            _shardingTypeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _shardingTypeCombo.Items.AddRange(new object[] { "时间分表", "哈希分表", "列表分表", "组合键分表", "查询频率分表" });
            _shardingTypeCombo.SelectedIndex = 0;
            _shardingTypeCombo.Dock = DockStyle.Fill;
            dbLayout.Controls.Add(_shardingTypeCombo, 1, 2);

            dbGroup.Controls.Add(dbLayout);
            mainLayout.Controls.Add(dbGroup, 0, 0);
            mainLayout.SetColumnSpan(dbGroup, 2);

            // 文件配置
            var fileGroup = new GroupBox
            {
                Text = "文件配置",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var fileLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2
            };

            fileLayout.Controls.Add(new Label { Text = "文件路径:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            _filePathBox.Dock = DockStyle.Fill;
            fileLayout.Controls.Add(_filePathBox, 1, 0);
            _browseButton.Text = "浏览...";
            _browseButton.Width = 80;
            _browseButton.Click += BrowseFile;
            fileLayout.Controls.Add(_browseButton, 2, 0);

            fileLayout.Controls.Add(new Label { Text = "文件格式:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            _fileFormatCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _fileFormatCombo.Items.AddRange(new object[] { "CSV (*.csv)", "Excel (*.xlsx;*.xls)" });
            _fileFormatCombo.SelectedIndex = 0;
            _fileFormatCombo.Dock = DockStyle.Fill;
            fileLayout.Controls.Add(_fileFormatCombo, 1, 1);

            _encodingCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _encodingCombo.Items.AddRange(new object[] { "UTF-8", "GBK", "GB2312" });
            _encodingCombo.SelectedIndex = 0;
            _encodingCombo.Dock = DockStyle.Fill;
            fileLayout.Controls.Add(_encodingCombo, 2, 1);

            fileGroup.Controls.Add(fileLayout);
            mainLayout.Controls.Add(fileGroup, 0, 1);
            mainLayout.SetColumnSpan(fileGroup, 2);

            // 导入设置
            var settingsGroup = new GroupBox
            {
                Text = "导入设置",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var settingsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1
            };

            settingsLayout.Controls.Add(new Label { Text = "主键字段:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            _primaryKeyCombo.DropDownStyle = ComboBoxStyle.DropDown;
            _primaryKeyCombo.Text = "Id";
            _primaryKeyCombo.Width = 120;
            settingsLayout.Controls.Add(_primaryKeyCombo, 1, 0);

            _hasHeaderBox.Text = "首行为表头";
            _hasHeaderBox.Checked = true;
            _hasHeaderBox.AutoSize = true;
            settingsLayout.Controls.Add(_hasHeaderBox, 2, 0);

            settingsGroup.Controls.Add(settingsLayout);
            mainLayout.Controls.Add(settingsGroup, 0, 2);
            mainLayout.SetColumnSpan(settingsGroup, 2);

            // 预览和进度
            var previewGroup = new GroupBox
            {
                Text = "数据预览",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var previewLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            var previewButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
            _previewButton = new Button { Text = "预览数据", Width = 80, Height = 30 };
            _previewButton.Click += PreviewData;
            previewButtonPanel.Controls.Add(_previewButton);

            _importButton = new Button { Text = "开始导入", Width = 80, Height = 30, BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White };
            _importButton.Click += StartImport;
            previewButtonPanel.Controls.Add(_importButton);

            _cancelButton = new Button { Text = "取消导入", Width = 80, Height = 30, Enabled = false };
            _cancelButton.Click += CancelImport;
            previewButtonPanel.Controls.Add(_cancelButton);
            previewLayout.Controls.Add(previewButtonPanel, 0, 0);

            _previewGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            previewLayout.Controls.Add(_previewGrid, 0, 1);

            _previewCountLabel = new Label { Text = "预览: 0 条记录", AutoSize = true };
            previewLayout.Controls.Add(_previewCountLabel, 0, 2);

            previewGroup.Controls.Add(previewLayout);
            mainLayout.Controls.Add(previewGroup, 0, 3);
            mainLayout.SetColumnSpan(previewGroup, 2);

            // 进度条
            var progressPanel = new Panel { Dock = DockStyle.Fill };
            _progressBar = new ProgressBar
            {
                Location = new Point(10, 10),
                Width = 500,
                Height = 25,
                Style = ProgressBarStyle.Continuous
            };
            progressPanel.Controls.Add(_progressBar);

            _progressLabel = new Label
            {
                Text = "就绪",
                Location = new Point(520, 15),
                AutoSize = true
            };
            progressPanel.Controls.Add(_progressLabel);
            mainLayout.Controls.Add(progressPanel, 0, 4);
            mainLayout.SetColumnSpan(progressPanel, 2);

            // 状态栏
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel { Text = "就绪" };
            _statusStrip.Items.Add(_statusLabel);

            Controls.Add(mainLayout);
            Controls.Add(_statusStrip);

            ResumeLayout();
        }

        private void EntityTypeChanged(object sender, EventArgs e)
        {
            _primaryKeyCombo.Items.Clear();

            switch (_entityTypeCombo.SelectedIndex)
            {
                case 0: // UserLog
                    _primaryKeyCombo.Items.AddRange(new object[] { "Id", "UserId" });
                    _primaryKeyCombo.SelectedIndex = 0;
                    break;
                case 1: // OrderData
                    _primaryKeyCombo.Items.AddRange(new object[] { "Id", "OrderNo" });
                    _primaryKeyCombo.SelectedIndex = 0;
                    break;
                case 2: // 自定义
                    _primaryKeyCombo.Text = "Id";
                    break;
            }
        }

        private void BrowseFile(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "CSV 文件|*.csv|Excel 文件|*.xlsx;*.xls|所有文件|*.*";
                dialog.Title = "选择导入文件";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _filePathBox.Text = dialog.FileName;

                    // 自动检测格式
                    var ext = Path.GetExtension(dialog.FileName).ToLower();
                    if (ext == ".csv")
                        _fileFormatCombo.SelectedIndex = 0;
                    else if (ext == ".xlsx" || ext == ".xls")
                        _fileFormatCombo.SelectedIndex = 1;
                }
            }
        }

        private void PreviewData(object sender, EventArgs e)
        {
            try
            {
                var filePath = _filePathBox.Text.Trim();
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    MessageBox.Show("请选择有效的文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var dataTable = LoadFileData(filePath, maxRows: 100);

                _previewGrid.DataSource = dataTable;
                _previewCountLabel.Text = $"预览: {dataTable.Rows.Count} 条记录（最多显示100条）";

                // 更新主键下拉框
                _primaryKeyCombo.Items.Clear();
                foreach (DataColumn col in dataTable.Columns)
                {
                    _primaryKeyCombo.Items.Add(col.ColumnName);
                }
                if (_primaryKeyCombo.Items.Count > 0)
                    _primaryKeyCombo.SelectedIndex = 0;

                _statusLabel.Text = $"预览完成: {dataTable.Columns.Count} 列, {dataTable.Rows.Count} 行";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"预览失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private DataTable LoadFileData(string filePath, int maxRows = -1)
        {
            var dataTable = new DataTable();
            var ext = Path.GetExtension(filePath).ToLower();

            if (ext == ".csv")
            {
                var encoding = Encoding.UTF8;
                switch (_encodingCombo.SelectedIndex)
                {
                    case 1: encoding = Encoding.GetEncoding("GBK"); break;
                    case 2: encoding = Encoding.GetEncoding("GB2312"); break;
                }

                var lines = File.ReadAllLines(filePath, encoding);
                if (lines.Length == 0) return dataTable;

                // 解析表头
                var startIndex = 0;
                if (_hasHeaderBox.Checked)
                {
                    var headers = ParseCsvLine(lines[0]);
                    foreach (var header in headers)
                    {
                        dataTable.Columns.Add(header.Trim());
                    }
                    startIndex = 1;
                }
                else
                {
                    // 自动生成列名
                    var firstLine = ParseCsvLine(lines[0]);
                    for (int i = 0; i < firstLine.Length; i++)
                    {
                        dataTable.Columns.Add($"Column{i + 1}");
                    }
                }

                // 解析数据
                for (int i = startIndex; i < lines.Length; i++)
                {
                    if (maxRows > 0 && dataTable.Rows.Count >= maxRows) break;

                    var values = ParseCsvLine(lines[i]);
                    var row = dataTable.NewRow();

                    for (int j = 0; j < Math.Min(values.Length, dataTable.Columns.Count); j++)
                    {
                        row[j] = values[j];
                    }

                    dataTable.Rows.Add(row);
                }
            }
            else if (ext == ".xlsx" || ext == ".xls")
            {
                // 使用 OleDb 读取 Excel（需要 Microsoft Access Database Engine）
                var connStr = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={filePath};Extended Properties='Excel 12.0;HDR=YES'";
                using (var conn = new System.Data.OleDb.OleDbConnection(connStr))
                {
                    conn.Open();
                    var sheetName = conn.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Tables, null).Rows[0]["TABLE_NAME"].ToString();
                    var query = $"SELECT * FROM [{sheetName}]";

                    if (maxRows > 0)
                        query = $"SELECT TOP {maxRows} * FROM [{sheetName}]";

                    using (var adapter = new System.Data.OleDb.OleDbDataAdapter(query, conn))
                    {
                        adapter.Fill(dataTable);
                    }
                }
            }

            return dataTable;
        }

        private string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var inQuotes = false;
            var current = new StringBuilder();

            foreach (var c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }

        private async void StartImport(object sender, EventArgs e)
        {
            try
            {
                await StartImportAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = "导入失败";
            }
        }

        private async Task StartImportAsync()
        {
            var filePath = _filePathBox.Text.Trim();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("请选择有效的文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var primaryKey = _primaryKeyCombo.Text.Trim();
            if (string.IsNullOrEmpty(primaryKey))
            {
                MessageBox.Show("请指定主键字段", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                "导入规则:\n\n" +
                "- 数据库中不存在的记录：新增\n" +
                "- 数据库中已存在的记录：更新为导入数据\n" +
                "- 数据库中有但导入文件中没有的记录：保持不变\n\n" +
                "确定要开始导入吗？",
                "确认导入",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            try
            {
                _isImporting = true;
                _importButton.Enabled = false;
                _cancelButton.Enabled = true;
                _previewButton.Enabled = false;

                var dataTable = LoadFileData(filePath);
                _statusLabel.Text = $"正在导入 {dataTable.Rows.Count} 条记录...";
                _progressBar.Maximum = dataTable.Rows.Count;
                _progressBar.Value = 0;

                var shardingType = _shardingTypeCombo.SelectedIndex;
                var connectionString = _connectionStringBox.Text;

                await Task.Run(() => ExecuteImport(dataTable, primaryKey, shardingType, connectionString));

                if (_isImporting)
                {
                    _statusLabel.Text = $"导入完成: {_progressBar.Value} 条记录";
                    MessageBox.Show($"导入完成!\n\n已处理: {_progressBar.Value} 条记录", "导入成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    _statusLabel.Text = "导入已取消";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = "导入失败";
            }
            finally
            {
                _isImporting = false;
                _importButton.Enabled = true;
                _cancelButton.Enabled = false;
                _previewButton.Enabled = true;
            }
        }

        private void ExecuteImport(DataTable dataTable, string primaryKey, int shardingType, string connectionString)
        {
            var insertedCount = 0;
            var updatedCount = 0;
            var errorCount = 0;

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                foreach (DataRow row in dataTable.Rows)
                {
                    if (!_isImporting) break; // 取消检查

                    try
                    {
                        // 确定目标分表
                        var targetTable = GetTargetTableName(row, shardingType, dataTable.Columns);

                        // 确保分表存在
                        EnsureShardingTableExists(conn, targetTable, dataTable.Columns);

                        // 检查记录是否存在
                        var exists = CheckRecordExists(conn, targetTable, primaryKey, row[primaryKey]);

                        if (exists)
                        {
                            // 更新记录
                            UpdateRecord(conn, targetTable, primaryKey, row, dataTable.Columns);
                            updatedCount++;
                        }
                        else
                        {
                            // 插入记录
                            InsertRecord(conn, targetTable, row, dataTable.Columns);
                            insertedCount++;
                        }

                        // 更新进度
                        Invoke(new Action(() =>
                        {
                            _progressBar.Value++;
                            _progressLabel.Text = $"已处理: {_progressBar.Value}/{dataTable.Rows.Count} (新增:{insertedCount} 更新:{updatedCount})";
                        }));
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        // 记录错误但继续处理
                        System.Diagnostics.Debug.WriteLine($"导入记录失败: {ex.Message}");
                    }
                }
            }

            Invoke(new Action(() =>
            {
                _statusLabel.Text = $"导入完成: 新增 {insertedCount}, 更新 {updatedCount}, 失败 {errorCount}";
            }));
        }

        private string GetTargetTableName(DataRow row, int shardingType, DataColumnCollection columns)
        {
            var baseTableName = _entityTypeCombo.SelectedIndex == 0 ? "UserLog" :
                               _entityTypeCombo.SelectedIndex == 1 ? "OrderData" : "ImportData";

            switch (shardingType)
            {
                case 0: // 时间分表
                    var timeField = columns.Contains("CreateTime") ? "CreateTime" :
                                   columns.Contains("OrderTime") ? "OrderTime" :
                                   columns.Contains("Time") ? "Time" : null;

                    if (timeField != null && row[timeField] != DBNull.Value)
                    {
                        var time = Convert.ToDateTime(row[timeField]);
                        return $"{baseTableName}_{time:yyyyMM}";
                    }
                    return $"{baseTableName}_default";

                case 1: // 哈希分表
                    var hashField = columns.Contains("UserId") ? "UserId" :
                                   columns.Contains("OrderNo") ? "OrderNo" :
                                   columns.Contains("Id") ? "Id" : null;

                    if (hashField != null && row[hashField] != DBNull.Value)
                    {
                        var hash = Math.Abs(row[hashField].GetHashCode()) % 4;
                        return $"{baseTableName}_{hash}";
                    }
                    return $"{baseTableName}_0";

                case 2: // 列表分表
                    var listField = columns.Contains("Status") ? "Status" :
                                   columns.Contains("Level") ? "Level" : null;

                    if (listField != null && row[listField] != DBNull.Value)
                    {
                        var value = row[listField].ToString().ToLower();
                        return $"{baseTableName}_{value}";
                    }
                    return $"{baseTableName}_other";

                case 3: // 组合键分表
                    var region = columns.Contains("Region") ? row["Region"]?.ToString() ?? "" : "";
                    var type = columns.Contains("CustomerType") ? row["CustomerType"]?.ToString() ?? "" : "";
                    var compositeHash = Math.Abs((region + type).GetHashCode()) % 4;
                    return $"{baseTableName}_{compositeHash}";

                default:
                    return baseTableName;
            }
        }

        private void EnsureShardingTableExists(SqlConnection conn, string tableName, DataColumnCollection columns)
        {
            // 检查表是否存在
            var checkSql = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'";
            using (var cmd = new SqlCommand(checkSql, conn))
            {
                var exists = (int)cmd.ExecuteScalar() > 0;
                if (exists) return;
            }

            // 创建表
            var columnDefs = new List<string>();
            foreach (DataColumn col in columns)
            {
                columnDefs.Add($"[{col.ColumnName}] NVARCHAR(MAX)");
            }

            var createSql = $@"
                CREATE TABLE [{tableName}] (
                    {string.Join(",\n                    ", columnDefs)}
                )";

            using (var cmd = new SqlCommand(createSql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private bool CheckRecordExists(SqlConnection conn, string tableName, string primaryKey, object keyValue)
        {
            var sql = $"SELECT COUNT(*) FROM [{tableName}] WHERE [{primaryKey}] = @KeyValue";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@KeyValue", keyValue ?? DBNull.Value);
                return (int)cmd.ExecuteScalar() > 0;
            }
        }

        private void InsertRecord(SqlConnection conn, string tableName, DataRow row, DataColumnCollection columns)
        {
            var colNames = string.Join(", ", columns.Cast<DataColumn>().Select(c => $"[{c.ColumnName}]"));
            var paramNames = string.Join(", ", columns.Cast<DataColumn>().Select(c => $"@{c.ColumnName}"));

            var sql = $"INSERT INTO [{tableName}] ({colNames}) VALUES ({paramNames})";

            using (var cmd = new SqlCommand(sql, conn))
            {
                foreach (DataColumn col in columns)
                {
                    cmd.Parameters.AddWithValue($"@{col.ColumnName}", row[col] ?? DBNull.Value);
                }
                cmd.ExecuteNonQuery();
            }
        }

        private void UpdateRecord(SqlConnection conn, string tableName, string primaryKey, DataRow row, DataColumnCollection columns)
        {
            var setClauses = columns.Cast<DataColumn>()
                .Where(c => c.ColumnName != primaryKey)
                .Select(c => $"[{c.ColumnName}] = @{c.ColumnName}");

            var sql = $"UPDATE [{tableName}] SET {string.Join(", ", setClauses)} WHERE [{primaryKey}] = @{primaryKey}";

            using (var cmd = new SqlCommand(sql, conn))
            {
                foreach (DataColumn col in columns)
                {
                    cmd.Parameters.AddWithValue($"@{col.ColumnName}", row[col] ?? DBNull.Value);
                }
                cmd.ExecuteNonQuery();
            }
        }

        private void CancelImport(object sender, EventArgs e)
        {
            _isImporting = false;
            _statusLabel.Text = "正在取消...";
        }

        protected override void Dispose(bool disposing)
        {
            _isImporting = false;
            base.Dispose(disposing);
        }
    }
}
