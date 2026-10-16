using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using FastData.Sharding;

namespace FastData.SyncTool.WinForms.Components
{
    /// <summary>
    /// 分表数据操作控件
    /// 支持数据导出、统计信息、跨表查询、批量增删改
    /// </summary>
    public class ShardingDataControl : UserControl
    {
        // 连接配置
        private readonly TextBox _connectionStringBox = new TextBox();

        // 统计信息
        private DataGridView _statsGrid;
        private Button _refreshStatsButton;
        private Label _totalRecordsLabel;

        // 跨表查询
        private TextBox _crossQuerySqlBox;
        private DataGridView _queryResultGrid;
        private Button _executeQueryButton;
        private Button _exportQueryButton;

        // 批量操作
        private ComboBox _batchOperationCombo;
        private TextBox _batchTableBox;
        private TextBox _batchSqlBox;
        private Button _batchExecuteButton;
        private Button _batchPreviewButton;

        // 数据导出
        private Button _exportAllButton;

        // 状态
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private Label _recordCountLabel;

        public ShardingDataControl()
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
                RowCount = 3,
                Padding = new Padding(10)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            // 连接配置
            var connPanel = new Panel { Dock = DockStyle.Fill };
            connPanel.Controls.Add(new Label { Text = "连接字符串:", Location = new Point(0, 5), AutoSize = true });
            _connectionStringBox.Text = string.Empty;
            _connectionStringBox.Location = new Point(100, 2);
            _connectionStringBox.Width = 400;
            connPanel.Controls.Add(_connectionStringBox);
            mainLayout.Controls.Add(connPanel, 0, 0);
            mainLayout.SetColumnSpan(connPanel, 2);

            // 左上：分表统计
            var statsGroup = new GroupBox
            {
                Text = "分表统计信息",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var statsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            statsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            statsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            var statsButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
            _refreshStatsButton = new Button { Text = "刷新统计", Width = 80, Height = 30 };
            _refreshStatsButton.Click += RefreshStats;
            statsButtonPanel.Controls.Add(_refreshStatsButton);

            _exportAllButton = new Button { Text = "导出全部", Width = 80, Height = 30, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White };
            _exportAllButton.Click += ExportAllStats;
            statsButtonPanel.Controls.Add(_exportAllButton);
            statsLayout.Controls.Add(statsButtonPanel, 0, 0);

            _statsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _statsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TableName", HeaderText = "表名", Width = 150 });
            _statsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RecordCount", HeaderText = "记录数", Width = 80 });
            _statsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "类型", Width = 80 });
            _statsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "大小(MB)", Width = 80 });
            statsLayout.Controls.Add(_statsGrid, 0, 1);

            _totalRecordsLabel = new Label { Text = "总记录数: 0", AutoSize = true };
            statsLayout.Controls.Add(_totalRecordsLabel, 0, 2);

            statsGroup.Controls.Add(statsLayout);
            mainLayout.Controls.Add(statsGroup, 0, 1);

            // 右上：跨表查询
            var queryGroup = new GroupBox
            {
                Text = "跨表查询",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var queryLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            queryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            _crossQuerySqlBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                Text = "-- 跨表查询示例\r\n-- SELECT * FROM UserLog_202501 WHERE UserId = 'User001'\r\n-- UNION ALL\r\n-- SELECT * FROM UserLog_202502 WHERE UserId = 'User001'"
            };
            queryLayout.Controls.Add(_crossQuerySqlBox, 0, 0);

            var queryButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
            _executeQueryButton = new Button { Text = "执行查询", Width = 80, Height = 30 };
            _executeQueryButton.Click += ExecuteCrossQuery;
            queryButtonPanel.Controls.Add(_executeQueryButton);

            _exportQueryButton = new Button { Text = "导出结果", Width = 80, Height = 30 };
            _exportQueryButton.Click += ExportQueryResult;
            queryButtonPanel.Controls.Add(_exportQueryButton);
            queryLayout.Controls.Add(queryButtonPanel, 0, 1);

            _queryResultGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true
            };
            queryLayout.Controls.Add(_queryResultGrid, 0, 2);

            _recordCountLabel = new Label { Text = "查询结果: 0 条记录", AutoSize = true };
            queryLayout.Controls.Add(_recordCountLabel, 0, 3);

            queryGroup.Controls.Add(queryLayout);
            mainLayout.Controls.Add(queryGroup, 1, 1);

            // 下方：批量操作
            var batchGroup = new GroupBox
            {
                Text = "批量增删改操作",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var batchLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5
            };
            batchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            batchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            batchLayout.Controls.Add(new Label { Text = "操作类型:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            _batchOperationCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _batchOperationCombo.Items.AddRange(new object[] { "批量插入", "批量更新", "批量删除", "自定义SQL" });
            _batchOperationCombo.SelectedIndex = 0;
            _batchOperationCombo.SelectedIndexChanged += BatchOperationChanged;
            batchLayout.Controls.Add(_batchOperationCombo, 1, 0);

            batchLayout.Controls.Add(new Label { Text = "目标表名:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            _batchTableBox = new TextBox { Dock = DockStyle.Fill };
            batchLayout.Controls.Add(_batchTableBox, 1, 1);

            batchLayout.Controls.Add(new Label { Text = "SQL语句:", TextAlign = ContentAlignment.TopRight }, 0, 2);
            _batchSqlBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                Text = "-- 批量插入示例\r\n-- INSERT INTO TableName (Col1, Col2) VALUES ('Val1', 'Val2')"
            };
            batchLayout.Controls.Add(_batchSqlBox, 1, 2);
            batchLayout.SetRowSpan(_batchSqlBox, 2);

            var batchButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
            _batchPreviewButton = new Button { Text = "预览", Width = 80, Height = 30 };
            _batchPreviewButton.Click += PreviewBatch;
            batchButtonPanel.Controls.Add(_batchPreviewButton);

            _batchExecuteButton = new Button { Text = "执行", Width = 80, Height = 30, BackColor = Color.FromArgb(244, 67, 54), ForeColor = Color.White };
            _batchExecuteButton.Click += ExecuteBatch;
            batchButtonPanel.Controls.Add(_batchExecuteButton);
            batchLayout.Controls.Add(batchButtonPanel, 1, 4);

            batchGroup.Controls.Add(batchLayout);
            mainLayout.Controls.Add(batchGroup, 0, 2);
            mainLayout.SetColumnSpan(batchGroup, 2);

            // 状态栏
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel { Text = "就绪" };
            _statusStrip.Items.Add(_statusLabel);

            Controls.Add(mainLayout);
            Controls.Add(_statusStrip);

            ResumeLayout();
        }

        private void BatchOperationChanged(object sender, EventArgs e)
        {
            switch (_batchOperationCombo.SelectedIndex)
            {
                case 0: // 批量插入
                    _batchSqlBox.Text = "-- 批量插入语法\r\nINSERT INTO [表名] ([列1], [列2], [列3])\r\nVALUES\r\n('值1', '值2', '值3'),\r\n('值4', '值5', '值6')";
                    break;
                case 1: // 批量更新
                    _batchSqlBox.Text = "-- 批量更新语法\r\nUPDATE [表名] SET [列1] = '新值'\r\nWHERE [条件]";
                    break;
                case 2: // 批量删除
                    _batchSqlBox.Text = "-- 批量删除语法\r\nDELETE FROM [表名]\r\nWHERE [条件]";
                    break;
                case 3: // 自定义SQL
                    _batchSqlBox.Text = "-- 自定义SQL\r\n";
                    break;
            }
        }

        private void RefreshStats(object sender, EventArgs e)
        {
            try
            {
                _statsGrid.Rows.Clear();
                var totalRecords = 0L;

                using (var conn = new SqlConnection(_connectionStringBox.Text))
                {
                    conn.Open();

                    // 获取所有用户表
                    var tablesSql = @"
                        SELECT 
                            t.name AS TableName,
                            p.rows AS RecordCount,
                            CAST(ROUND(((SUM(a.total_pages) * 8) / 1024.00), 2) AS NUMERIC(36,2)) AS SizeMB
                        FROM sys.tables t
                        INNER JOIN sys.indexes i ON t.OBJECT_ID = i.object_id
                        INNER JOIN sys.partitions p ON i.object_id = p.OBJECT_ID AND i.index_id = p.index_id
                        INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
                        WHERE t.is_ms_shipped = 0 AND i.OBJECT_ID > 255
                        GROUP BY t.name, p.rows
                        ORDER BY t.name";

                    using (var cmd = new SqlCommand(tablesSql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var tableName = reader.GetString(0);
                            var recordCount = reader.GetInt64(1);
                            var sizeMB = reader.GetDecimal(2);

                            // 判断表类型
                            var type = "普通表";
                            if (ShardingManager.IsShardingEnabled<object>())
                            {
                                var shardingTables = ShardingManager.GetAllTableNames<object>();
                                if (shardingTables.Contains(tableName))
                                {
                                    type = "分表";
                                }
                                else if (shardingTables.Any(st => tableName.StartsWith(st)))
                                {
                                    type = "分表";
                                }
                            }

                            _statsGrid.Rows.Add(tableName, recordCount.ToString("N0"), type, sizeMB);
                            totalRecords += recordCount;
                        }
                    }
                }

                _totalRecordsLabel.Text = $"总记录数: {totalRecords:N0}";
                _statusLabel.Text = $"统计完成: {_statsGrid.Rows.Count} 个表";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取统计信息失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportAllStats(object sender, EventArgs e)
        {
            if (_statsGrid.Rows.Count == 0)
            {
                MessageBox.Show("请先刷新统计信息", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV 文件|*.csv|Excel 文件|*.xlsx";
                dialog.Title = "导出统计信息";
                dialog.FileName = $"sharding-stats-{DateTime.Now:yyyyMMddHHmmss}.csv";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("表名,记录数,类型,大小(MB)");

                        foreach (DataGridViewRow row in _statsGrid.Rows)
                        {
                            sb.AppendLine($"{row.Cells["TableName"].Value},{row.Cells["RecordCount"].Value},{row.Cells["Type"].Value},{row.Cells["Size"].Value}");
                        }

                        File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                        _statusLabel.Text = $"统计信息已导出: {dialog.FileName}";
                        MessageBox.Show($"统计信息已导出到:\n{dialog.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExecuteCrossQuery(object sender, EventArgs e)
        {
            try
            {
                var sql = _crossQuerySqlBox.Text.Trim();
                if (string.IsNullOrEmpty(sql) || sql.StartsWith("--"))
                {
                    MessageBox.Show("请输入有效的SQL查询语句", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _statusLabel.Text = "执行查询...";
                Application.DoEvents();

                using (var conn = new SqlConnection(_connectionStringBox.Text))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.CommandTimeout = 300; // 5分钟超时
                        var dataTable = new DataTable();
                        using (var adapter = new SqlDataAdapter(cmd))
                        {
                            adapter.Fill(dataTable);
                        }

                        _queryResultGrid.DataSource = dataTable;
                        _recordCountLabel.Text = $"查询结果: {dataTable.Rows.Count:N0} 条记录";
                        _statusLabel.Text = $"查询完成: {dataTable.Rows.Count:N0} 条记录";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = "查询失败";
            }
        }

        private void ExportQueryResult(object sender, EventArgs e)
        {
            if (_queryResultGrid.DataSource == null)
            {
                MessageBox.Show("请先执行查询", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV 文件|*.csv|所有文件|*.*";
                dialog.Title = "导出查询结果";
                dialog.FileName = $"query-result-{DateTime.Now:yyyyMMddHHmmss}.csv";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var dataTable = (DataTable)_queryResultGrid.DataSource;
                        var sb = new StringBuilder();

                        // 写入表头
                        var headers = dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                        sb.AppendLine(string.Join(",", headers));

                        // 写入数据
                        foreach (DataRow row in dataTable.Rows)
                        {
                            var values = row.ItemArray.Select(v => $"\"{v?.ToString() ?? ""}\"");
                            sb.AppendLine(string.Join(",", values));
                        }

                        File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                        _statusLabel.Text = $"查询结果已导出: {dialog.FileName}";
                        MessageBox.Show($"查询结果已导出到:\n{dialog.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void PreviewBatch(object sender, EventArgs e)
        {
            var sql = _batchSqlBox.Text.Trim();
            if (string.IsNullOrEmpty(sql) || sql.StartsWith("--"))
            {
                MessageBox.Show("请输入有效的SQL语句", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 显示预览对话框
            var result = MessageBox.Show(
                $"即将执行以下SQL:\n\n{sql}\\n\n确定要执行吗？",
                "预览SQL",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                ExecuteBatch(sender, e);
            }
        }

        private void ExecuteBatch(object sender, EventArgs e)
        {
            var sql = _batchSqlBox.Text.Trim();
            if (string.IsNullOrEmpty(sql) || sql.StartsWith("--"))
            {
                MessageBox.Show("请输入有效的SQL语句", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 危险操作确认
            var operation = _batchOperationCombo.SelectedItem.ToString();
            if (operation == "批量删除" || operation == "批量更新")
            {
                var confirmResult = MessageBox.Show(
                    $"确定要执行 {operation} 吗？\n\n这可能会影响大量数据！",
                    "确认操作",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirmResult != DialogResult.Yes)
                    return;
            }

            try
            {
                _statusLabel.Text = "执行批量操作...";
                Application.DoEvents();

                using (var conn = new SqlConnection(_connectionStringBox.Text))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.CommandTimeout = 600; // 10分钟超时
                        var affectedRows = cmd.ExecuteNonQuery();

                        _statusLabel.Text = $"批量操作完成: 影响 {affectedRows:N0} 条记录";
                        MessageBox.Show($"批量操作完成\n\n影响记录数: {affectedRows:N0}", "操作成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"批量操作失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = "批量操作失败";
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
