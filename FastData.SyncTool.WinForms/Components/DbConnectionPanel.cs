using System;
using System.Windows.Forms;
using FastData.Database;
using FastData.Infrastructure;

namespace FastData.SyncTool.WinForms
{
    public class DbConnectionPanel : UserControl
    {
        private readonly ComboBox sourceProviderBox = new ComboBox();
        private readonly TextBox sourceConnectionBox = new TextBox();
        private readonly ComboBox targetProviderBox = new ComboBox();
        private readonly TextBox targetConnectionBox = new TextBox();
        private readonly ComboBox intermediateProviderBox = new ComboBox();
        private readonly TextBox intermediateConnectionBox = new TextBox();
        private readonly Button testSourceButton = new Button();
        private readonly Button testTargetButton = new Button();

        public event Action OnConfigChanged;

        public DbConnectionPanel()
        {
            InitControls();
            BindEvents();
        }

        private void InitControls()
        {
            Dock = DockStyle.Fill;
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 9 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

            int row = 0;
            AddHeader(panel, "源数据库", row++);
            AddRow(panel, row++, "Provider:", sourceProviderBox, sourceConnectionBox, testSourceButton);

            AddHeader(panel, "目标数据库", row++);
            AddRow(panel, row++, "Provider:", targetProviderBox, targetConnectionBox, testTargetButton);

            AddHeader(panel, "中间库（可选）", row++);
            AddRow(panel, row++, "Provider:", intermediateProviderBox, intermediateConnectionBox, null);

            sourceProviderBox.Items.AddRange(DatabaseProviderMappings.AllProviderNames);
            var defaultProvider = DatabaseProviderMappings.NormalizeProviderName("SqlServer");
            var defaultIndex = sourceProviderBox.Items.IndexOf(defaultProvider);
            sourceProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceProviderBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;

            targetProviderBox.Items.AddRange(DatabaseProviderMappings.AllProviderNames);
            targetProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            targetProviderBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;

            intermediateProviderBox.Items.AddRange(DatabaseProviderMappings.AllProviderNames);
            intermediateProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            intermediateProviderBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;

            sourceConnectionBox.Width = 300;
            targetConnectionBox.Width = 300;
            intermediateConnectionBox.Width = 300;

            Controls.Add(panel);
        }

        private void AddHeader(TableLayoutPanel panel, string text, int row)
        {
            var label = new Label { Text = text, Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold), AutoSize = true };
            panel.Controls.Add(label, 0, row);
            panel.SetColumnSpan(label, 3);
        }

        private void AddRow(TableLayoutPanel panel, int row, string label, ComboBox provider, TextBox connStr, Button testBtn)
        {
            panel.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
            var inner = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            provider.Width = 100;
            connStr.Width = 300;
            inner.Controls.Add(provider);
            inner.Controls.Add(connStr);
            if (testBtn != null) { testBtn.Text = "测试"; testBtn.Width = 60; inner.Controls.Add(testBtn); }
            panel.Controls.Add(inner, 1, row);
        }

        private void BindEvents()
        {
            sourceProviderBox.SelectedIndexChanged += (s, e) => OnConfigChanged?.Invoke();
            sourceConnectionBox.TextChanged += (s, e) => OnConfigChanged?.Invoke();
            targetProviderBox.SelectedIndexChanged += (s, e) => OnConfigChanged?.Invoke();
            targetConnectionBox.TextChanged += (s, e) => OnConfigChanged?.Invoke();
            intermediateProviderBox.SelectedIndexChanged += (s, e) => OnConfigChanged?.Invoke();
            intermediateConnectionBox.TextChanged += (s, e) => OnConfigChanged?.Invoke();
            testSourceButton.Click += (s, e) => TestConnection(sourceProviderBox.SelectedItem?.ToString(), sourceConnectionBox.Text);
            testTargetButton.Click += (s, e) => TestConnection(targetProviderBox.SelectedItem?.ToString(), targetConnectionBox.Text);
        }

        private void TestConnection(string provider, string connStr)
        {
            if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(connStr))
            {
                MessageBox.Show("请填写 Provider 和连接字符串", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                var invariantName = DatabaseProviderMappings.NormalizeProviderName(provider) ?? provider;
                using (var conn = DbProviderAutoRegistrar.GetFactory(invariantName).CreateConnection())
                {
                    conn.ConnectionString = connStr;
                    conn.Open();
                    MessageBox.Show("连接成功!", "测试结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("连接失败: " + ex.Message, "测试结果", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public string SourceProvider { get { return DatabaseProviderMappings.NormalizeProviderName(sourceProviderBox.SelectedItem?.ToString()); } }
        public string SourceConnectionString { get { return sourceConnectionBox.Text; } }
        public string TargetProvider { get { return DatabaseProviderMappings.NormalizeProviderName(targetProviderBox.SelectedItem?.ToString()); } }
        public string TargetConnectionString { get { return targetConnectionBox.Text; } }
        public string IntermediateProvider { get { return DatabaseProviderMappings.NormalizeProviderName(intermediateProviderBox.SelectedItem?.ToString()); } }
        public string IntermediateConnectionString { get { return intermediateConnectionBox.Text; } }

        public void SetSource(string provider, string connStr) { SelectProvider(sourceProviderBox, provider); sourceConnectionBox.Text = connStr ?? ""; }
        public void SetTarget(string provider, string connStr) { SelectProvider(targetProviderBox, provider); targetConnectionBox.Text = connStr ?? ""; }
        public void SetIntermediate(string provider, string connStr) { SelectProvider(intermediateProviderBox, provider); intermediateConnectionBox.Text = connStr ?? ""; }

        private static void SelectProvider(ComboBox box, string provider)
        {
            var normalized = DatabaseProviderMappings.NormalizeProviderName(provider);
            if (string.IsNullOrEmpty(normalized))
                return;

            var index = box.Items.IndexOf(normalized);
            if (index >= 0)
                box.SelectedIndex = index;
        }
    }
}
