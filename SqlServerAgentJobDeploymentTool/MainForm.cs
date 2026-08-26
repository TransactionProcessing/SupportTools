namespace SqlServerAgentJobDeploymentTool;

using System.Drawing;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

internal sealed class MainForm : Form
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainForm> _logger;
    private TextBox _manifestPathTextBox = null!;
    private TextBox _serverTextBox = null!;
    private TextBox _connectionDatabaseTextBox = null!;
    private TextBox _databaseNameTextBox = null!;
    private CheckBox _useSslCheckBox = null!;
    private ComboBox _authenticationTypeComboBox = null!;
    private TextBox _usernameTextBox = null!;
    private TextBox _passwordTextBox = null!;
    private Label _usernameLabel = null!;
    private Label _passwordLabel = null!;
    private TextBox _manifestTextBox = null!;
    private TextBox _outputTextBox = null!;
    private Button _loadManifestButton = null!;
    private Button _browseButton = null!;
    private Button _saveButton = null!;
    private Button _saveAsButton = null!;
    private Button _formatButton = null!;
    private Button _dryRunButton = null!;
    private Button _deployButton = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private TextBox _validationSummaryTextBox = null!;

    public MainForm(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MainForm>();

        Text = "SQL Agent Job Deployment Tool";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1280, 820);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        var statusStrip = CreateStatusStrip();
        var headerPanel = CreateHeaderPanel();
        var split = CreateSplitContainer();
        split.Panel1.Controls.Add(CreateLeftPanel());
        split.Panel2.Controls.Add(CreateRightPanel());

        Controls.Add(split);
        Controls.Add(headerPanel);
        Controls.Add(statusStrip);

        Load += MainForm_Load;
        _manifestTextBox.TextChanged += ManifestTextBox_TextChanged;
        UpdateAuthenticationVisibility();
    }

    private StatusStrip CreateStatusStrip()
    {
        StatusStrip statusStrip = new();
        _statusLabel = new ToolStripStatusLabel("Ready");
        statusStrip.Items.Add(_statusLabel);
        return statusStrip;
    }

    private Panel CreateHeaderPanel()
    {
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
        return headerPanel;
    }

    private SplitContainer CreateSplitContainer()
    {
        return new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 820,
            FixedPanel = FixedPanel.Panel2
        };
    }

    private Panel CreateLeftPanel()
    {
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
        Panel connectionGroup = CreateConnectionGroup();
        TableLayoutPanel databaseRow = CreateLabeledPanel(
            new Label { Text = "Step database override", AutoSize = true },
            CreateSingleRow(_databaseNameTextBox = new TextBox { Dock = DockStyle.Fill }));
        Panel manifestWorkArea = CreateManifestWorkArea();

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

        Panel leftPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        leftPanel.Controls.Add(leftLayout);
        return leftPanel;
    }

    private Panel CreateConnectionGroup()
    {
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
        _useSslCheckBox = new CheckBox { Text = "Encrypt connection", AutoSize = true, Margin = new Padding(0) };
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
        _usernameLabel = CreateCompactLabel("Username");
        _passwordLabel = CreateCompactLabel("Password");

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
        connectionLayout.Controls.Add(_useSslCheckBox, 2, 1);
        connectionLayout.SetColumnSpan(_useSslCheckBox, 2);
        connectionLayout.Controls.Add(_usernameLabel, 0, 2);
        connectionLayout.Controls.Add(_usernameTextBox, 1, 2);
        connectionLayout.Controls.Add(_passwordLabel, 2, 2);
        connectionLayout.Controls.Add(_passwordTextBox, 3, 2);

        connectionGroup.Controls.Add(connectionLayout);
        connectionGroup.Controls.Add(connectionTitle);
        return connectionGroup;
    }

    private Panel CreateManifestWorkArea()
    {
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

        Panel manifestWorkArea = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        manifestWorkArea.Controls.Add(manifestGroup);
        manifestWorkArea.Controls.Add(actionsGroup);
        return manifestWorkArea;
    }

    private Panel CreateRightPanel()
    {
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

        TableLayoutPanel rightLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rightLayout.Controls.Add(validationGroup, 0, 0);
        rightLayout.Controls.Add(outputGroup, 0, 1);

        Panel rightPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        rightPanel.Controls.Add(rightLayout);
        return rightPanel;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        string samplePath = Path.Combine(AppContext.BaseDirectory, "transactionprocessor-readmodel-transaction-jobs.json");
        _manifestPathTextBox.Text = samplePath;
        _logger.LogInformation("UI loaded. Sample manifest path: {SamplePath}.", samplePath);

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
        string currentOperation = "loading manifest";
        try
        {
            string manifestText = await File.ReadAllTextAsync(manifestPath);
            _manifestPathTextBox.Text = manifestPath;
            _manifestTextBox.Text = DeploymentManifestLoader.Format(manifestText);
            AppendOutput($"Loaded manifest from '{manifestPath}'.");
            _logger.LogInformation("Loaded manifest from {ManifestPath}.", manifestPath);
            SetStatus($"Loaded {Path.GetFileName(manifestPath)}");
            UpdateValidationSummary();
        }
        catch (Exception ex)
        {
            DeploymentErrorReporter.ReportUi(ex, _logger, AppendOutput, currentOperation, manifestPath);
            MessageBox.Show(this, DeploymentErrorReporter.GetUserMessage(ex, currentOperation, manifestPath), "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunDeploymentAsync(bool dryRun)
    {
        SetBusyState(true);
        string currentOperation = "preparing deployment";
        string? currentContext = null;

        try
        {
            UpdateValidationSummary();
            DeploymentManifest manifest = DeploymentManifestLoader.Parse(_manifestTextBox.Text);

            string overrideDatabaseName = _databaseNameTextBox.Text.Trim();

            currentOperation = "validating manifest";
            ManifestValidator.Validate(manifest);
            _logger.LogInformation("Starting {Mode} deployment for {JobCount} job(s). Database override: {DatabaseOverride}.",
                dryRun ? "dry-run" : "live",
                manifest.Jobs.Count,
                string.IsNullOrWhiteSpace(overrideDatabaseName) ? "<none>" : overrideDatabaseName);

            if (dryRun)
            {
                AppendOutput("Dry run. Jobs that would be deployed:");
                foreach (JobDefinition job in manifest.Jobs)
                {
                    AppendOutput($"- {job.Name}");
                }

                UpdateValidationSummary(manifest);
                SetStatus("Dry run completed");
                _logger.LogInformation("Dry run completed.");
                return;
            }

            currentOperation = "opening SQL connection";
            string connectionString = BuildConnectionString();
            currentContext = DeploymentErrorReporter.DescribeConnectionTarget(connectionString);
            await using SqlConnection connection = new(connectionString);
            AppendOutput($"Connecting to SQL Server at {currentContext}...");
            _logger.LogInformation("Connecting to SQL Server at {ConnectionTarget}.", currentContext);
            await connection.OpenAsync();

            _logger.LogInformation("SQL connection opened to {ConnectionTarget}.", currentContext);
            currentOperation = "deploying SQL Agent jobs";
            SqlAgentDeploymentService deployer = new(connection, new TextBoxWriter(_outputTextBox), _loggerFactory.CreateLogger<SqlAgentDeploymentService>());
            await deployer.DeployAsync(manifest, overrideDatabaseName, CancellationToken.None);
            AppendOutput("Deployment completed.");
            UpdateValidationSummary(manifest);
            SetStatus("Deployment completed");
            _logger.LogInformation("Deployment completed.");
        }
        catch (Exception ex)
        {
            DeploymentErrorReporter.ReportUi(ex, _logger, AppendOutput, currentOperation, currentContext);
            UpdateValidationSummary();
            SetStatus("Deployment failed");
            MessageBox.Show(this, DeploymentErrorReporter.GetUserMessage(ex, currentOperation, currentContext), "Deployment failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task SaveManifestAsync(bool saveAs)
    {
        string currentOperation = "saving manifest";
        try
        {
            string manifestPath = _manifestPathTextBox.Text.Trim();
            _logger.LogInformation("Saving manifest. SaveAs: {SaveAs}. Path: {ManifestPath}.", saveAs, string.IsNullOrWhiteSpace(manifestPath) ? "<empty>" : manifestPath);
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
            _logger.LogInformation("Saved manifest to {ManifestPath}.", manifestPath);
            SetStatus($"Saved {Path.GetFileName(manifestPath)}");
            UpdateValidationSummary();
        }
        catch (Exception ex)
        {
            string manifestPath = _manifestPathTextBox.Text.Trim();
            DeploymentErrorReporter.ReportUi(ex, _logger, AppendOutput, currentOperation, manifestPath);
            SetStatus("Save failed");
            MessageBox.Show(this, DeploymentErrorReporter.GetUserMessage(ex, currentOperation, manifestPath), "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _logger.LogInformation("Formatted manifest JSON.");
            SetStatus("JSON formatted");
        }
        catch (Exception ex)
        {
            DeploymentErrorReporter.ReportUi(ex, _logger, AppendOutput, "formatting manifest JSON");
            MessageBox.Show(this, DeploymentErrorReporter.GetUserMessage(ex, "formatting manifest JSON"), "Format failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                Error: {DeploymentErrorReporter.GetUserMessage(ex, "validating manifest")}
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
            TrustServerCertificate = false
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
