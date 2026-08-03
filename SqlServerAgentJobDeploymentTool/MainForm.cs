namespace SqlServerAgentJobDeploymentTool;

using System.Drawing;
using System.Text;
using Microsoft.Data.SqlClient;

internal sealed class MainForm : Form
{
    private readonly TextBox _manifestPathTextBox;
    private readonly TextBox _serverTextBox;
    private readonly TextBox _connectionDatabaseTextBox;
    private readonly TextBox _databaseNameTextBox;
    private readonly CheckBox _useSslCheckBox;
    private readonly ComboBox _authenticationTypeComboBox;
    private readonly TextBox _usernameTextBox;
    private readonly TextBox _passwordTextBox;
    private readonly Label _usernameLabel;
    private readonly Label _passwordLabel;
    private readonly TextBox _manifestTextBox;
    private readonly TextBox _outputTextBox;
    private readonly Button _loadManifestButton;
    private readonly Button _browseButton;
    private readonly Button _saveButton;
    private readonly Button _saveAsButton;
    private readonly Button _formatButton;
    private readonly Button _dryRunButton;
    private readonly Button _deployButton;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly TextBox _validationSummaryTextBox;

    public MainForm()
    {
        Text = "SQL Agent Job Deployment Tool";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1280, 820);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        StatusStrip statusStrip = new();
        _statusLabel = new ToolStripStatusLabel("Ready");
        statusStrip.Items.Add(_statusLabel);

        Panel headerPanel = new()
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = Color.White,
            Padding = new Padding(18, 14, 18, 14)
        };

        TableLayoutPanel headerLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        headerLayout.Controls.Add(new Label
        {
            Text = "SQL Agent Job Deployment Tool",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 41, 55)
        }, 0, 0);

        headerLayout.Controls.Add(new Label
        {
            Text = "Load a manifest, review it, then deploy SQL Server Agent jobs with a target server and database override.",
            AutoSize = true,
            ForeColor = Color.FromArgb(75, 85, 99)
        }, 0, 1);

        headerPanel.Controls.Add(headerLayout);

        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 820,
            FixedPanel = FixedPanel.Panel2
        };

        Label manifestPathLabel = new() { Text = "Manifest", AutoSize = true };
        _manifestPathTextBox = new TextBox { Dock = DockStyle.Fill };
        _browseButton = new Button { Text = "Browse..." };
        _loadManifestButton = new Button { Text = "Load" };
        _saveButton = new Button { Text = "Save" };
        _saveAsButton = new Button { Text = "Save As" };
        _formatButton = new Button { Text = "Format JSON" };

        _browseButton.Click += BrowseButton_Click;
        _loadManifestButton.Click += LoadManifestButton_Click;
        _saveButton.Click += SaveManifestButton_Click;
        _saveAsButton.Click += SaveAsButton_Click;
        _formatButton.Click += FormatButton_Click;

        TableLayoutPanel manifestPathRow = CreateRow(_manifestPathTextBox, _browseButton, _loadManifestButton, _saveButton, _saveAsButton, _formatButton);

        Panel connectionGroup = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        Label connectionTitle = new()
        {
            Text = "Connection settings",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2),
            Dock = DockStyle.Top
        };

        _serverTextBox = new TextBox { Dock = DockStyle.Fill, Text = "localhost", Margin = new Padding(0) };
        _connectionDatabaseTextBox = new TextBox { Dock = DockStyle.Fill, Text = "msdb", Margin = new Padding(0) };
        _useSslCheckBox = new CheckBox { Text = "Use SSL", AutoSize = true, Margin = new Padding(0) };
        _authenticationTypeComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0)
        };
        _authenticationTypeComboBox.Items.AddRange(["Windows", "SQL"]);
        _authenticationTypeComboBox.SelectedIndex = 0;
        _authenticationTypeComboBox.SelectedIndexChanged += AuthenticationTypeComboBox_SelectedIndexChanged;

        _usernameTextBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _passwordTextBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Margin = new Padding(0) };

        TableLayoutPanel connectionLayout = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 3,
            Padding = new Padding(0)
        };
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        connectionLayout.Controls.Add(CreateCompactLabel("Server"), 0, 0);
        connectionLayout.Controls.Add(_serverTextBox, 1, 0);
        connectionLayout.Controls.Add(CreateCompactLabel("Connection database"), 2, 0);
        connectionLayout.Controls.Add(_connectionDatabaseTextBox, 3, 0);
        connectionLayout.Controls.Add(CreateCompactLabel("Authentication"), 0, 1);
        connectionLayout.Controls.Add(_authenticationTypeComboBox, 1, 1);
        _useSslCheckBox.Text = "Encrypt connection";
        connectionLayout.Controls.Add(_useSslCheckBox, 2, 1);
        connectionLayout.SetColumnSpan(_useSslCheckBox, 2);
        _usernameLabel = CreateCompactLabel("Username");
        _passwordLabel = CreateCompactLabel("Password");
        connectionLayout.Controls.Add(_usernameLabel, 0, 2);
        connectionLayout.Controls.Add(_usernameTextBox, 1, 2);
        connectionLayout.Controls.Add(_passwordLabel, 2, 2);
        connectionLayout.Controls.Add(_passwordTextBox, 3, 2);

        connectionGroup.Controls.Add(connectionLayout);
        connectionGroup.Controls.Add(connectionTitle);

        _databaseNameTextBox = new TextBox { Dock = DockStyle.Fill };

        TableLayoutPanel databaseRow = CreateLabeledPanel(
            new Label { Text = "Step database override", AutoSize = true },
            CreateSingleRow(_databaseNameTextBox));

        GroupBox manifestGroup = new()
        {
            Dock = DockStyle.Fill,
            Text = "Manifest content"
        };

        _manifestTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9F)
        };
        manifestGroup.Controls.Add(_manifestTextBox);

        GroupBox actionsGroup = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Actions",
            Padding = new Padding(4, 8, 4, 4),
            Width = 110
        };

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Padding = new Padding(0),
            WrapContents = false
        };

        _dryRunButton = new Button { Text = "Dry run", Width = 84, Margin = new Padding(0, 0, 0, 6) };
        _deployButton = new Button { Text = "Deploy", Width = 84, Margin = new Padding(0) };
        _dryRunButton.Click += async (_, _) => await RunDeploymentAsync(true);
        _deployButton.Click += async (_, _) => await RunDeploymentAsync(false);

        buttons.Controls.Add(_dryRunButton);
        buttons.Controls.Add(_deployButton);

        actionsGroup.Controls.Add(buttons);

        Panel leftPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        Panel manifestWorkArea = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        manifestWorkArea.Controls.Add(manifestGroup);
        manifestWorkArea.Controls.Add(actionsGroup);

        TableLayoutPanel leftLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        leftLayout.Controls.Add(CreateLabeledPanel(manifestPathLabel, manifestPathRow), 0, 0);
        leftLayout.Controls.Add(connectionGroup, 0, 1);
        leftLayout.Controls.Add(databaseRow, 0, 2);
        leftLayout.Controls.Add(manifestWorkArea, 0, 3);
        leftPanel.Controls.Add(leftLayout);

        GroupBox outputGroup = new()
        {
            Dock = DockStyle.Fill,
            Text = "Output",
            Padding = new Padding(4, 8, 4, 4)
        };

        _outputTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false
        };
        outputGroup.Controls.Add(_outputTextBox);

        Panel rightPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        TableLayoutPanel rightLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        GroupBox validationGroup = new()
        {
            Dock = DockStyle.Fill,
            Text = "Validation summary",
            Padding = new Padding(4, 8, 4, 4)
        };

        _validationSummaryTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false
        };
        validationGroup.Controls.Add(_validationSummaryTextBox);

        rightLayout.RowStyles.Clear();
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rightLayout.Controls.Clear();
        rightLayout.Controls.Add(validationGroup, 0, 0);
        rightLayout.Controls.Add(outputGroup, 0, 1);
        rightPanel.Controls.Add(rightLayout);

        split.Panel1.Controls.Add(leftPanel);
        split.Panel2.Controls.Add(rightPanel);

        Controls.Add(split);
        Controls.Add(headerPanel);
        Controls.Add(statusStrip);

        Load += MainForm_Load;
        _manifestTextBox.TextChanged += ManifestTextBox_TextChanged;
        UpdateAuthenticationVisibility();
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        string samplePath = Path.Combine(AppContext.BaseDirectory, "transactionprocessor-readmodel-transaction-jobs.json");
        _manifestPathTextBox.Text = samplePath;

        if (File.Exists(samplePath))
        {
            await LoadManifestFromPathAsync(samplePath);
        }
        else
        {
            AppendOutput($"Sample manifest not found at '{samplePath}'.");
        }
    }

    private async void BrowseButton_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Select SQL Agent manifest"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _manifestPathTextBox.Text = dialog.FileName;
        await LoadManifestFromPathAsync(dialog.FileName);
    }

    private async void LoadManifestButton_Click(object? sender, EventArgs e)
    {
        await LoadManifestFromPathAsync(_manifestPathTextBox.Text);
    }

    private async void SaveManifestButton_Click(object? sender, EventArgs e)
    {
        await SaveManifestAsync(false);
    }

    private async void SaveAsButton_Click(object? sender, EventArgs e)
    {
        await SaveManifestAsync(true);
    }

    private async Task LoadManifestFromPathAsync(string manifestPath)
    {
        try
        {
            string manifestText = await File.ReadAllTextAsync(manifestPath);
            _manifestPathTextBox.Text = manifestPath;
            _manifestTextBox.Text = DeploymentManifestLoader.Format(manifestText);
            AppendOutput($"Loaded manifest from '{manifestPath}'.");
            SetStatus($"Loaded {Path.GetFileName(manifestPath)}");
            UpdateValidationSummary();
        }
        catch (Exception ex)
        {
            AppendOutput(ex.Message);
            MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunDeploymentAsync(bool dryRun)
    {
        SetBusyState(true);

        try
        {
            UpdateValidationSummary();
            DeploymentManifest manifest = DeploymentManifestLoader.Parse(_manifestTextBox.Text);

            string overrideDatabaseName = _databaseNameTextBox.Text.Trim();

            ManifestValidator.Validate(manifest);

            if (dryRun)
            {
                AppendOutput("Dry run. Jobs that would be deployed:");
                foreach (JobDefinition job in manifest.Jobs)
                {
                    AppendOutput($"- {job.Name}");
                }

                UpdateValidationSummary(manifest);
                SetStatus("Dry run completed");
                return;
            }

            string connectionString = BuildConnectionString();
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync();

            AppendOutput("Connecting to SQL Server...");
            SqlAgentDeploymentService deployer = new(connection, new TextBoxWriter(_outputTextBox));
            await deployer.DeployAsync(manifest, overrideDatabaseName, CancellationToken.None);
            AppendOutput("Deployment completed.");
            UpdateValidationSummary(manifest);
            SetStatus("Deployment completed");
        }
        catch (Exception ex)
        {
            AppendOutput(ex.Message);
            UpdateValidationSummary();
            SetStatus("Deployment failed");
            MessageBox.Show(this, ex.Message, "Deployment failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task SaveManifestAsync(bool saveAs)
    {
        try
        {
            string manifestPath = _manifestPathTextBox.Text.Trim();
            if (saveAs || string.IsNullOrWhiteSpace(manifestPath))
            {
                using SaveFileDialog dialog = new()
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    Title = "Save SQL Agent manifest",
                    FileName = string.IsNullOrWhiteSpace(manifestPath) ? "sql-agent-jobs.json" : Path.GetFileName(manifestPath)
                };

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                manifestPath = dialog.FileName;
                _manifestPathTextBox.Text = manifestPath;
            }

            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new InvalidOperationException("Select a manifest path before saving.");
            }

            string formattedManifest = DeploymentManifestLoader.Format(_manifestTextBox.Text);
            _manifestTextBox.Text = formattedManifest;
            await File.WriteAllTextAsync(manifestPath, formattedManifest);
            AppendOutput($"Saved manifest to '{manifestPath}'.");
            SetStatus($"Saved {Path.GetFileName(manifestPath)}");
            UpdateValidationSummary();
        }
        catch (Exception ex)
        {
            AppendOutput(ex.Message);
            SetStatus("Save failed");
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ManifestTextBox_TextChanged(object? sender, EventArgs e)
    {
        UpdateValidationSummary();
    }

    private void FormatButton_Click(object? sender, EventArgs e)
    {
        try
        {
            _manifestTextBox.Text = DeploymentManifestLoader.Format(_manifestTextBox.Text);
            AppendOutput("Formatted manifest JSON.");
            SetStatus("JSON formatted");
        }
        catch (Exception ex)
        {
            AppendOutput(ex.Message);
            MessageBox.Show(this, ex.Message, "Format failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AppendOutput(string message)
    {
        if (_outputTextBox.InvokeRequired)
        {
            _outputTextBox.BeginInvoke(() => AppendOutput(message));
            return;
        }

        _outputTextBox.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
    }

    private void SetBusyState(bool busy)
    {
        _dryRunButton.Enabled = !busy;
        _deployButton.Enabled = !busy;
        _browseButton.Enabled = !busy;
        _loadManifestButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        _saveAsButton.Enabled = !busy;
        _formatButton.Enabled = !busy;
        _manifestTextBox.ReadOnly = busy;
        _databaseNameTextBox.ReadOnly = busy;
        _serverTextBox.ReadOnly = busy;
        _connectionDatabaseTextBox.ReadOnly = busy;
        _useSslCheckBox.Enabled = !busy;
        _authenticationTypeComboBox.Enabled = !busy;
        _usernameTextBox.ReadOnly = busy;
        _passwordTextBox.ReadOnly = busy;
    }

    private void SetStatus(string message)
    {
        if (_statusLabel.Owner?.InvokeRequired == true)
        {
            _statusLabel.Owner.BeginInvoke(() => _statusLabel.Text = message);
            return;
        }

        _statusLabel.Text = message;
    }

    private static TableLayoutPanel CreateLabeledPanel(Control label, Control content)
    {
        label.Margin = new Padding(0);
        content.Margin = new Padding(0);

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private static Label CreateCompactLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 6, 0)
        };
    }

    private static TableLayoutPanel CreateInlineField(string labelText, Control content)
    {
        Label label = new()
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 6, 0)
        };

        content.Margin = new Padding(0);

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(content, 1, 0);
        return panel;
    }

    private static TableLayoutPanel CreateRow(params Control[] controls)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = controls.Length,
            Margin = new Padding(0)
        };

        for (int index = 0; index < controls.Length; index++)
        {
            row.ColumnStyles.Add(index == 0 ? new ColumnStyle(SizeType.Percent, 100F) : new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(controls[index], index, 0);
        }

        return row;
    }

    private static Panel CreateSingleRow(Control control)
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0);
        panel.Controls.Add(control);
        return panel;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SetStatus("Ready");
        UpdateValidationSummary();
    }

    private void UpdateValidationSummary()
    {
        if (_validationSummaryTextBox.IsDisposed)
        {
            return;
        }

        if (_validationSummaryTextBox.InvokeRequired)
        {
            _validationSummaryTextBox.BeginInvoke(UpdateValidationSummary);
            return;
        }

        UpdateValidationSummaryCore(_manifestTextBox.Text);
    }

    private void UpdateValidationSummary(DeploymentManifest manifest)
    {
        if (_validationSummaryTextBox.IsDisposed)
        {
            return;
        }

        if (_validationSummaryTextBox.InvokeRequired)
        {
            _validationSummaryTextBox.BeginInvoke(() => UpdateValidationSummary(manifest));
            return;
        }

        int totalSteps = manifest.Jobs.Sum(job => job.Steps.Count);
        _validationSummaryTextBox.Text = $"""
            Manifest status: valid
            Jobs: {manifest.Jobs.Count}
            Steps: {totalSteps}
            """;
    }

    private void UpdateValidationSummaryCore(string manifestText)
    {
        if (string.IsNullOrWhiteSpace(manifestText))
        {
            _validationSummaryTextBox.Text = """
                Manifest status: empty
                Jobs: 0
                Steps: 0
                """;
            return;
        }

        try
        {
            DeploymentManifest manifest = DeploymentManifestLoader.Parse(manifestText);
            ManifestValidator.Validate(manifest);

            int totalSteps = manifest.Jobs.Sum(job => job.Steps.Count);
            _validationSummaryTextBox.Text = $"""
                Manifest status: valid
                Jobs: {manifest.Jobs.Count}
                Steps: {totalSteps}
                """;
        }
        catch (Exception ex)
        {
            _validationSummaryTextBox.Text = $"""
                Manifest status: invalid
                Error: {ex.Message}
                """;
        }
    }

    private void AuthenticationTypeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateAuthenticationVisibility();
    }

    private void UpdateAuthenticationVisibility()
    {
        bool showSqlCredentials = IsSqlAuthenticationSelected();
        _usernameLabel.Visible = showSqlCredentials;
        _usernameTextBox.Visible = showSqlCredentials;
        _passwordLabel.Visible = showSqlCredentials;
        _passwordTextBox.Visible = showSqlCredentials;
    }

    private bool IsSqlAuthenticationSelected()
    {
        return _authenticationTypeComboBox.SelectedIndex == 1;
    }

    private string BuildConnectionString()
    {
        string server = _serverTextBox.Text.Trim();
        string database = _connectionDatabaseTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException("Enter a SQL Server name.");
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("Enter a connection database name.");
        }

        SqlConnectionStringBuilder builder = new()
        {
            DataSource = server,
            InitialCatalog = database,
            Encrypt = _useSslCheckBox.Checked,
            TrustServerCertificate = _useSslCheckBox.Checked
        };

        if (IsSqlAuthenticationSelected())
        {
            string username = _usernameTextBox.Text.Trim();
            string password = _passwordTextBox.Text;

            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException("Enter a SQL username.");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Enter a SQL password.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = username;
            builder.Password = password;
        }
        else
        {
            builder.IntegratedSecurity = true;
            builder.UserID = string.Empty;
            builder.Password = string.Empty;
        }

        return builder.ConnectionString;
    }

    private sealed class TextBoxWriter : TextWriter
    {
        private readonly TextBox _target;

        public TextBoxWriter(TextBox target)
        {
            _target = target;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            if (_target.IsDisposed)
            {
                return;
            }

            if (_target.InvokeRequired)
            {
                _target.BeginInvoke(() => _target.AppendText($"{value}{Environment.NewLine}"));
                return;
            }

            _target.AppendText($"{value}{Environment.NewLine}");
        }
    }
}
