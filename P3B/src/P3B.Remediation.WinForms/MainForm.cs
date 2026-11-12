using System.Diagnostics;
using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.WinForms.Presentation;

namespace P3B.Remediation.WinForms;

/// <summary>
/// The remediation dashboard: P3A's audit and its Ready / Not Ready verdict (kept apart from
/// P3B's own results), detected issues with supported / manual / unsupported indicators, the
/// approve-and-fix workflow with progress and automatic re-audit, remediation and smoke-test
/// results, reversal records, and saving results for P3A. Long operations run off the UI thread,
/// and only one runs at a time.
/// </summary>
internal sealed class MainForm : Form
{
    private const string JsonFilter = "JSON files (*.json)|*.json|All files (*.*)|*.*";

    private readonly DashboardController _controller;
    private readonly string? _initialManifest;
    private readonly string? _initialAudit;

    private readonly Label _banner = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8), Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
    private readonly Label _manifestLabel = HeaderLabel();
    private readonly Label _auditLabel = HeaderLabel();
    private readonly ToolTip _toolTip = new();
    private readonly Button _openManifest = new() { Text = "Open manifest...", AutoSize = true };
    private readonly Button _loadAudit = new() { Text = "Load audit file...", AutoSize = true };

    private readonly Label _p3aStatus = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(4, 6, 4, 6), Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
    private readonly Button _runAudit = new() { Text = "Run P3A audit", AutoSize = true };

    private readonly ListView _issues = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly Button _fix = new() { Text = "Fix selected issue...", AutoSize = true };
    private readonly Button _openDownload = new() { Text = "Open download page", AutoSize = true, Enabled = false };
    private readonly Button _recheck = new() { Text = "I've done this - check again", AutoSize = true, Enabled = false };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly ProgressBar _progress = new() { Width = 160, Style = ProgressBarStyle.Blocks, Margin = new Padding(12, 6, 6, 0) };
    private readonly Label _progressText = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 0), MaximumSize = new Size(560, 0) };

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox _details = ReadOnlyText();
    private readonly TextBox _result = ReadOnlyText();
    private readonly Label _smokeSummary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), MaximumSize = new Size(900, 0) };
    private readonly ListView _smokeChecks = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly ListView _reversals = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly TextBox _reversalDetails = ReadOnlyText();
    private readonly Button _reverse = new() { Text = "Reverse this change...", AutoSize = true, Enabled = false };
    private readonly Button _discard = new() { Text = "Discard record...", AutoSize = true, Enabled = false };

    private readonly Button _saveResults = new() { Text = "Save results for P3A re-audit...", AutoSize = true, Enabled = false };
    private readonly Label _resultsInfo = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 0) };

    private readonly ListView _p3aComponents = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly Label _p3aSummary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), MaximumSize = new Size(900, 0) };

    private readonly TabPage _detailsTab = new("Issue details");
    private readonly TabPage _resultTab = new("P3B remediation result");
    private readonly TabPage _smokeTab = new("P3B smoke tests");
    private readonly TabPage _reversalTab = new("Reversal records");
    private readonly TabPage _p3aTab = new("P3A audit (all components)");

    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };

    private CancellationTokenSource? _operation;
    private bool _rebuildingLists;

    public MainForm(Func<IApprovalProvider, DashboardController> createController, string? manifestPath = null, string? auditPath = null)
    {
        ArgumentNullException.ThrowIfNull(createController);
        _controller = createController(new WinFormsApprovalProvider(this));
        _initialManifest = manifestPath;
        _initialAudit = auditPath;

        Text = "Windows EDA Environment Manager - Installation & Remediation (P3B)";
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(900, 640);
        Size = new Size(1100, 760);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        WireEvents();
        RefreshAll();
    }

    /// <summary>Single-line label that shortens long text with "..." so the buttons beside it stay visible.</summary>
    private static Label HeaderLabel() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        AutoEllipsis = true,
        Height = 28,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static TextBox ReadOnlyText() => KeepUnselected(new TextBox
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = SystemColors.Window,
    });

    /// <summary>
    /// Stops Windows highlighting all the text when a read-only box gets the focus, which looked
    /// like a selection the user had made. The text can still be selected and copied.
    /// </summary>
    internal static TextBox KeepUnselected(TextBox box)
    {
        box.GotFocus += (_, _) => box.BeginInvoke(() => box.Select(0, 0));
        box.TextChanged += (_, _) => box.Select(0, 0);
        return box;
    }

    private void BuildLayout()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 4, 8, 4) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // P3A's verdict sits in its own box, apart from anything P3B reports.
        var p3aBox = new GroupBox { Text = "P3A environment audit (decides Ready / Not Ready)", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6, 2, 6, 4) };
        var p3aRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        p3aRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p3aRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        p3aRow.Controls.Add(_p3aStatus, 0, 0);
        p3aRow.Controls.Add(_runAudit, 1, 0);
        p3aBox.Controls.Add(p3aRow);

        header.Controls.Add(_banner, 0, 0);
        header.SetColumnSpan(_banner, 2);
        header.Controls.Add(p3aBox, 0, 1);
        header.SetColumnSpan(p3aBox, 2);
        header.Controls.Add(_manifestLabel, 0, 2);
        header.Controls.Add(_openManifest, 1, 2);
        header.Controls.Add(_auditLabel, 0, 3);
        header.Controls.Add(_loadAudit, 1, 3);

        _issues.Columns.Add("Tool", 185);
        _issues.Columns.Add("Problem", 120);
        _issues.Columns.Add("Found version", 105);
        _issues.Columns.Add("Approved version", 135);
        _issues.Columns.Add("Automatic fix", 125);
        _issues.Columns.Add("P3B status", 135);
        _issues.Columns.Add("P3B verification", 125);
        _issues.ShowItemToolTips = true;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
        actions.Controls.AddRange([_fix, _openDownload, _recheck, _cancel, _progress, _progressText]);

        var issuesPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 4) };
        issuesPanel.Controls.Add(_issues);
        issuesPanel.Controls.Add(new Label { Text = "Detected issues (from P3A's audit)", Dock = DockStyle.Top, AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) });
        issuesPanel.Controls.Add(actions);

        _smokeChecks.Columns.Add("Check", 330);
        _smokeChecks.Columns.Add("Result", 90);
        _smokeChecks.Columns.Add("Explanation", 520);

        _reversals.Columns.Add("Recorded", 140);
        _reversals.Columns.Add("Tool", 150);
        _reversals.Columns.Add("Reversal", 150);
        _reversals.Columns.Add("Changes", 450);

        _detailsTab.Controls.Add(_details);
        _resultTab.Controls.Add(_result);
        _smokeTab.Controls.Add(_smokeChecks);
        _smokeTab.Controls.Add(_smokeSummary);

        var reversalSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        reversalSplit.Panel1.Controls.Add(_reversals);
        reversalSplit.Panel2.Controls.Add(_reversalDetails);
        var reversalButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        reversalButtons.Controls.AddRange([_reverse, _discard]);
        _reversalTab.Controls.Add(reversalSplit);
        _reversalTab.Controls.Add(reversalButtons);

        _p3aComponents.Columns.Add("Component", 200);
        _p3aComponents.Columns.Add("P3A readiness", 100);
        _p3aComponents.Columns.Add("Installation", 100);
        _p3aComponents.Columns.Add("Version", 100);
        _p3aComponents.Columns.Add("P3A's reason", 520);
        _p3aComponents.ShowItemToolTips = true;
        _p3aTab.Controls.Add(_p3aComponents);
        _p3aTab.Controls.Add(_p3aSummary);

        _tabs.TabPages.AddRange([_detailsTab, _resultTab, _smokeTab, _reversalTab, _p3aTab]);

        var split = _split;
        split.Panel1.Controls.Add(issuesPanel);
        split.Panel2.Controls.Add(_tabs);
        split.Panel2.Padding = new Padding(8, 0, 8, 0);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 4, 8, 8) };
        footer.Controls.AddRange([_saveResults, _resultsInfo]);

        Controls.Add(split);
        Controls.Add(footer);
        Controls.Add(header);
    }

    private void WireEvents()
    {
        Load += OnLoadAsync;
        FormClosing += OnFormClosing;
        _openManifest.Click += (_, _) => OpenManifest(null);
        _loadAudit.Click += (_, _) => LoadAudit(null);
        _issues.SelectedIndexChanged += (_, _) =>
        {
            if (!_rebuildingLists)
            {
                ShowSelectedIssue();
            }
        };
        _fix.Click += OnFixClickedAsync;
        _runAudit.Click += async (_, _) => await RunP3AAuditAsync();
        _recheck.Click += async (_, _) => await RunP3AAuditAsync();
        _openDownload.Click += (_, _) => OpenDownloadPage();
        _cancel.Click += (_, _) => CancelOperation();
        _reversals.SelectedIndexChanged += (_, _) =>
        {
            if (!_rebuildingLists)
            {
                ShowSelectedReversal();
            }
        };
        _reverse.Click += OnReverseClickedAsync;
        _discard.Click += OnDiscardClickedAsync;
        _saveResults.Click += OnSaveResultsClickedAsync;
    }

    private async void OnLoadAsync(object? sender, EventArgs e)
    {
        // Set once the window has its real size: issues on top, details below.
        _split.SplitterDistance = _split.Height * 45 / 100;

        if (_initialManifest is not null)
        {
            OpenManifest(_initialManifest);
        }

        await RefreshReversalsAsync();

        if (_initialAudit is not null)
        {
            LoadAudit(_initialAudit);
        }
        else if (_controller.CanRunP3AAudit && _controller.ManifestStatus.IsUsable)
        {
            // Start where a student would: with P3A's audit of this computer.
            await RunP3AAuditAsync();
        }

        RefreshAll();
    }

    private async Task RunP3AAuditAsync()
    {
        if (!_controller.CanRunP3AAudit || _controller.IsBusy)
        {
            return;
        }

        // P3A's audit cannot be stopped part-way, so Cancel stays disabled while it runs.
        _progress.Style = ProgressBarStyle.Marquee;
        var progress = new Progress<string>(text => _progressText.Text = text);
        try
        {
            var running = _controller.RunP3AAuditAsync(progress, CancellationToken.None);
            RefreshAll();
            var audit = await running;
            if (audit is null)
            {
                ShowProblem("Please wait", "Another operation is already running.");
            }
        }
        catch (Exception ex)
        {
            ShowProblem("P3A's audit could not be run", ex.Message);
        }
        finally
        {
            _progress.Style = ProgressBarStyle.Blocks;
            RefreshAll();
            RefreshP3AComponents();
            SelectFirstIssueIfNone();
        }
    }

    private void OpenDownloadPage()
    {
        var url = SelectedIssue()?.Guidance?.DownloadUrl;
        if (!P3AManifestSchema.IsWebAddress(url))
        {
            return;
        }

        try
        {
            // Opens the official page from the manifest in the browser; P3B downloads and runs nothing.
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowProblem("The download page could not be opened", $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Open it yourself: {url}");
        }
    }

    private void RefreshP3AComponents()
    {
        var audit = _controller.LastP3AAudit;
        _p3aComponents.BeginUpdate();
        _p3aComponents.Items.Clear();
        foreach (var component in audit?.Components ?? [])
        {
            _p3aComponents.Items.Add(new ListViewItem(
            [
                component.Name,
                component.ReadinessStatus,
                component.InstallationStatus,
                component.Version,
                component.Reason ?? string.Empty,
            ])
            {
                ToolTipText = component.Reason,
                ForeColor = component.ReadinessStatus switch
                {
                    "Ready" => Color.DarkGreen,
                    "Not Ready" => Color.Firebrick,
                    _ => SystemColors.GrayText,
                },
            });
        }

        _p3aComponents.EndUpdate();
        _p3aSummary.Text = audit is null
            ? "P3A's audit has not been run in this session."
            : $"{audit.Summary}{Environment.NewLine}This list is P3A's own result. P3B's remediation and smoke-test results are on the P3B tabs.";
    }

    private void SelectFirstIssueIfNone()
    {
        if (_issues.SelectedItems.Count == 0 && _issues.Items.Count > 0)
        {
            _issues.Items[0].Selected = true;
        }
        else if (_issues.Items.Count == 0)
        {
            ShowSelectedIssue();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_controller.IsBusy)
        {
            return;
        }

        e.Cancel = true;
        MessageBox.Show(
            this,
            _operation is null
                ? $"P3B is still {_controller.CurrentActivity ?? "working"}. This changes nothing and usually takes a few seconds; close the window when it has finished."
                : "P3B is still working. Press Cancel and wait for it to stop before closing, so that every change is recorded.",
            "Please wait",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenManifest(string? path)
    {
        path ??= AskForFile("Open the Local EDA Tool Manifest");
        if (path is null)
        {
            return;
        }

        try
        {
            var result = _controller.OpenManifest(path);
            if (!result.IsUsable)
            {
                ShowProblem("The manifest cannot be used", result.Description);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        {
            ShowProblem("The manifest could not be opened", ex.Message);
        }

        RefreshAll();
    }

    private void LoadAudit(string? path)
    {
        path ??= AskForFile("Load P3A's audit results");
        if (path is null)
        {
            return;
        }

        try
        {
            var result = _controller.LoadAuditResults(path);
            if (!result.IsLoaded)
            {
                ShowProblem("The audit results could not be loaded", result.Description);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        {
            ShowProblem("The audit results could not be loaded", ex.Message);
        }

        RefreshAll();
        if (_issues.Items.Count > 0)
        {
            _issues.Items[0].Selected = true;
        }
    }

    private async void OnFixClickedAsync(object? sender, EventArgs e)
    {
        if (SelectedIssue() is not { Support: FixSupport.Supported } item)
        {
            return;
        }

        if (_controller.IsBusy)
        {
            ShowProblem("Please wait", "Another operation is already running. Only one change can be made at a time.");
            return;
        }

        using var operation = StartOperation();
        var progress = new Progress<string>(text => _progressText.Text = text);
        try
        {
            var running = _controller.RemediateAsync(item, progress, operation.Token);
            RefreshAll();
            var run = await running;

            if (run is null)
            {
                ShowProblem("Please wait", "Another operation is already running. Only one change can be made at a time.");
                return;
            }

            var p3a = _controller.LastP3AAudit is { HasVerdict: true } audit && !_controller.AnyChangeAttempted
                ? $" P3A's re-audit: {StatusText.Verdict(audit.Verdict)}."
                : string.Empty;
            _progressText.Text = $"P3B {StatusText.Outcome(run.Remediation.Outcome)}: {item.ToolLabel}. P3B verification: {StatusText.Verification(run.SmokeTest)}.{p3a}";
            RefreshP3AComponents();
            ShowRun(run);
            _tabs.SelectedTab = _resultTab;
            await RefreshReversalsAsync();
        }
        catch (Exception ex)
        {
            _progressText.Text = "The operation stopped unexpectedly.";
            ShowProblem("The fix could not be completed", ex.Message);
        }
        finally
        {
            EndOperation();
        }
    }

    private void CancelOperation()
    {
        _operation?.Cancel();
        _cancel.Enabled = false;
        _progressText.Text = "Cancelling: P3B is stopping the installer and checking what changed. Please wait.";
    }

    private async void OnReverseClickedAsync(object? sender, EventArgs e)
    {
        if (SelectedReversal() is not { } entry || _controller.IsBusy)
        {
            return;
        }

        try
        {
            var preview = await _controller.PreviewReversalAsync(entry.EntryId, CancellationToken.None);
            if (preview is null)
            {
                await RefreshReversalsAsync();
                return;
            }

            if (preview.Kind != ReversalKind.Automatic)
            {
                MessageBox.Show(
                    this,
                    $"P3B cannot reverse this change automatically.{Environment.NewLine}{Environment.NewLine}{preview.Method}",
                    StatusText.Reversibility(preview.Kind),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var confirm = MessageBox.Show(
                this,
                $"P3B will reverse this change to {preview.ToolId}:{Environment.NewLine}"
                + string.Concat(preview.ChangesToReverse.Select(c => $"{Environment.NewLine}  - {c}"))
                + $"{Environment.NewLine}{Environment.NewLine}How: {preview.Method}{Environment.NewLine}{Environment.NewLine}Continue?",
                "Confirm reversal",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                _progressText.Text = "Not reversed: you did not confirm.";
                return;
            }

            using var operation = StartOperation();
            _progressText.Text = $"Reversing a change to {preview.ToolId}. Please wait.";
            try
            {
                var running = _controller.ReverseAsync(preview, userConfirmed: true, operation.Token);
                RefreshAll();
                var result = await running;
                _progressText.Text = StatusText.Rollback(result.Outcome);
                MessageBox.Show(
                    this,
                    $"{StatusText.Rollback(result.Outcome)}{Environment.NewLine}{Environment.NewLine}{result.Message}",
                    "Reversal result",
                    MessageBoxButtons.OK,
                    result.Outcome == RollbackOutcome.Reversed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally
            {
                EndOperation();
            }

            await RefreshReversalsAsync();
        }
        catch (Exception ex)
        {
            ShowProblem("The reversal could not be completed", ex.Message);
        }
    }

    private async void OnDiscardClickedAsync(object? sender, EventArgs e)
    {
        if (SelectedReversal() is not { } entry || _controller.IsBusy)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"{JsonFileReversalJournal.DiscardWarning}{Environment.NewLine}{Environment.NewLine}Discard the reversal record for {entry.ToolId}?",
            "Discard reversal record?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _controller.DiscardReversalAsync(entry.EntryId, userAcknowledgedWarning: true, CancellationToken.None);
            await RefreshReversalsAsync();
        }
        catch (Exception ex)
        {
            ShowProblem("The record could not be discarded", ex.Message);
        }
    }

    private async void OnSaveResultsClickedAsync(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog { Filter = JsonFilter, FileName = "p3b-remediation-results.json", Title = "Save results for P3A re-audit" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _controller.SaveResultsAsync(dialog.FileName, CancellationToken.None);
            _resultsInfo.Text = $"Saved for P3A re-audit: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowProblem("The results could not be saved", ex.Message);
        }
    }

    private CancellationTokenSource StartOperation()
    {
        _operation = new CancellationTokenSource();
        _progress.Style = ProgressBarStyle.Marquee;
        return _operation;
    }

    private void EndOperation()
    {
        _operation = null;
        _progress.Style = ProgressBarStyle.Blocks;
        RefreshAll();
    }

    /// <summary>Rebuilds the issues list, then updates labels and buttons.</summary>
    private void RefreshAll()
    {
        RefreshIssues();
        RefreshState();
    }

    /// <summary>Updates the banner, labels and button states without rebuilding any list.</summary>
    private void RefreshState()
    {
        var banner = StatusText.Banner(_controller);
        _banner.Text = banner.Text;
        _banner.ForeColor = banner.Kind switch
        {
            BannerKind.Ready => Color.DarkGreen,
            BannerKind.NotReady => Color.Firebrick,
            BannerKind.InProgress => Color.DarkBlue,
            BannerKind.ReauditNeeded => Color.DarkGoldenrod,
            _ => SystemColors.ControlText,
        };

        var p3a = StatusText.P3AReadiness(_controller);
        _p3aStatus.Text = p3a.Text;
        _p3aStatus.ForeColor = p3a.Kind switch
        {
            BannerKind.Ready => Color.DarkGreen,
            BannerKind.NotReady => Color.Firebrick,
            BannerKind.ReauditNeeded => Color.DarkGoldenrod,
            _ => SystemColors.GrayText,
        };
        _toolTip.SetToolTip(_p3aStatus, _controller.LastP3AAudit?.Summary ?? "P3A's audit decides whether this computer is Ready. P3B never does.");

        var manifest = _controller.ManifestStatus;
        _manifestLabel.Text = manifest.IsUsable
            ? $"Manifest: {Path.GetFileName(_controller.ManifestPath)} ({ManifestContents(manifest.Manifest)}, format {manifest.Manifest.ManifestVersion})"
            : $"Manifest: not loaded. {manifest.Description}";
        _toolTip.SetToolTip(_manifestLabel, manifest.IsUsable ? _controller.ManifestPath : manifest.Description);

        _auditLabel.Text = _controller.AuditStatus is { IsLoaded: true }
            ? $"Issues: {(_controller.AuditPath is { } file ? $"from file {Path.GetFileName(file)}" : "from P3A's audit")} ({_controller.Issues.Count} issue(s))"
            : "Issues: none loaded yet.";
        _toolTip.SetToolTip(_auditLabel, _controller.AuditPath ?? _controller.AuditStatus?.Description ?? string.Empty);

        var busy = _controller.IsBusy;
        var selected = SelectedIssue();
        _fix.Enabled = !busy && selected is { Support: FixSupport.Supported };
        _fix.Visible = selected is null or { Support: FixSupport.Supported };
        _openDownload.Visible = _openDownload.Enabled = !busy && selected?.Guidance?.DownloadUrl is not null;
        _toolTip.SetToolTip(_openDownload, selected?.Guidance?.DownloadUrl ?? string.Empty);
        _recheck.Visible = _controller.CanRunP3AAudit && selected is { Support: FixSupport.ManualSteps or FixSupport.NotSupported };
        _recheck.Enabled = !busy && _recheck.Visible && _controller.ManifestStatus.IsUsable;
        _runAudit.Enabled = !busy && _controller.CanRunP3AAudit && _controller.ManifestStatus.IsUsable;
        _cancel.Enabled = busy && _operation is { IsCancellationRequested: false };
        _openManifest.Enabled = !busy;
        _loadAudit.Enabled = !busy;
        _issues.Enabled = !busy;
        _reverse.Enabled = !busy && SelectedReversal() is not null;
        _discard.Enabled = !busy && SelectedReversal() is not null;
        _saveResults.Enabled = !busy && _controller.HasResults;
        _resultsInfo.Text = _controller.LastSaveProblem
            ?? (_controller.HasResults ? $"Results are also saved automatically for P3A: {_controller.ResultsPath}" : string.Empty);
    }

    private static string ManifestContents(LocalEdaToolManifest manifest)
    {
        var extensions = manifest.Tools.Count(t => t.Kind == ManifestComponentKind.Extension);
        var tools = $"{manifest.Tools.Count - extensions} tool(s)";
        return extensions == 0 ? tools : $"{tools} and {extensions} extension group(s)";
    }

    private void RefreshIssues()
    {
        var selectedKey = SelectedIssue()?.Key;
        _rebuildingLists = true;
        _issues.BeginUpdate();
        _issues.Items.Clear();
        foreach (var item in _controller.Issues)
        {
            var row = new ListViewItem(
            [
                item.ToolLabel,
                StatusText.Problem(item.Issue.State),
                item.Issue.DetectedVersion ?? string.Empty,
                item.TargetVersion ?? item.ApprovedText ?? string.Empty,
                StatusText.Support(item.Support),
                StatusText.Status(item),
                StatusText.Verification(item.LastRun?.SmokeTest),
            ])
            {
                Tag = item,
                ToolTipText = item.SupportReason,
                ForeColor = item.Status switch
                {
                    IssueStatus.Successful => Color.DarkGreen,
                    IssueStatus.Failed or IssueStatus.CouldNotRun => Color.Firebrick,
                    IssueStatus.InProgress => Color.DarkBlue,
                    _ when item.Support == FixSupport.ManualSteps => Color.DarkGoldenrod,
                    _ when item.Support != FixSupport.Supported => SystemColors.GrayText,
                    _ => SystemColors.WindowText,
                },
            };
            _issues.Items.Add(row);
        }

        // Keep the same issue selected; after a re-audit it may no longer be listed.
        foreach (ListViewItem row in _issues.Items)
        {
            if (selectedKey is not null && row.Tag is IssueItem { } shown && shown.Key == selectedKey)
            {
                row.Selected = true;
                break;
            }
        }

        _issues.EndUpdate();
        _rebuildingLists = false;
    }

    private void ShowSelectedIssue()
    {
        if (SelectedIssue() is not { } item)
        {
            _details.Text = _controller.Issues.Count == 0 && _controller.LastP3AAudit is { Verdict: Core.Integration.ReadinessVerdict.Ready }
                ? "P3A's audit found no problems. There is nothing for P3B to fix."
                : string.Empty;
            RefreshState();
            return;
        }

        var lines = new List<string>
        {
            $"Tool: {item.ToolLabel}",
            $"Problem reported by P3A: {StatusText.Problem(item.Issue.State)}",
            $"Issue ID: {item.Issue.IssueId ?? "(not stated)"}",
        };
        if (!string.IsNullOrWhiteSpace(item.Issue.DetectedVersion))
        {
            lines.Add($"Version found: {item.Issue.DetectedVersion}");
        }

        if (!string.IsNullOrWhiteSpace(item.Issue.Detail))
        {
            lines.Add($"P3A's explanation: {item.Issue.Detail}");
        }

        if (item.ApprovedText is { } approved)
        {
            lines.Add($"Approved version(s) in the manifest: {approved}");
        }

        lines.Add(string.Empty);
        lines.Add($"Automatic fix: {StatusText.Support(item.Support)}");
        lines.Add(item.SupportReason);

        if (item.Guidance is { } guidance)
        {
            lines.Add(string.Empty);
            lines.Add("Manual steps from the manifest (P3B will not do these for you):");
            lines.AddRange(guidance.Steps.Select((step, index) => $"  {index + 1}. {step}"));
            if (guidance.DownloadUrl is not null)
            {
                lines.Add($"Official download page: {guidance.DownloadUrl}");
            }
        }

        if (item.Support is FixSupport.NotSupported or FixSupport.ManualSteps)
        {
            lines.Add(string.Empty);
            lines.Add("This is not a failed installation. P3B has no supported automatic fix for this issue, so it must be fixed by hand.");
            if (_controller.CanRunP3AAudit)
            {
                lines.Add("When you have finished, press \"I've done this - check again\". P3A's audit then decides whether the problem is fixed.");
            }
        }

        _details.Text = string.Join(Environment.NewLine, lines);

        if (item.LastRun is { } run)
        {
            ShowRun(run);
        }
        else
        {
            _result.Text = "This issue has not been fixed in this session.";
            _smokeSummary.Text = "Smoke tests run after a successful fix.";
            _smokeChecks.Items.Clear();
        }

        _tabs.SelectedTab = _detailsTab;
        RefreshState();
    }

    private void ShowRun(RemediationRunResult run)
    {
        var remediation = run.Remediation;
        var lines = new List<string>
        {
            $"Result: {StatusText.Outcome(remediation.Outcome)}",
            remediation.Message,
        };
        if (remediation.Action is { } action)
        {
            lines.Add($"Approved target version: {action.TargetVersion}");
        }

        if (!string.IsNullOrWhiteSpace(remediation.PriorState))
        {
            lines.Add($"Before: {remediation.PriorState}");
        }

        lines.Add(string.Empty);
        lines.Add(remediation.ObservedChanges.Count == 0 ? "Changes made: none." : "Changes made:");
        lines.AddRange(remediation.ObservedChanges.Select(c => $"  - {c}"));

        if (remediation.Reversal is { } reversal)
        {
            lines.Add(string.Empty);
            lines.Add($"Reversal: {StatusText.Reversibility(reversal.Kind)}. {reversal.Method}");
            if (remediation.ReversalJournalEntryId is not null)
            {
                lines.Add("The reversal information is saved under Reversal records.");
            }
        }

        if (remediation.Outcome == RemediationOutcome.Cancelled && remediation.ObservedChanges.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("The operation was cancelled, but some changes were made first. They are listed above and under Reversal records.");
        }

        lines.Add(string.Empty);
        var mayHaveChanged = remediation.Outcome is RemediationOutcome.Succeeded or RemediationOutcome.Failed or RemediationOutcome.Cancelled;
        lines.Add(!mayHaveChanged
            ? "This is P3B's result only. Nothing was changed, so P3A's audit was not run again."
            : _controller.CanRunP3AAudit
                ? "This is P3B's result only. P3B does not decide whether the computer is Ready: P3A's audit was run again afterwards, and its verdict is shown in the P3A box at the top."
                : "This is P3B's result only. P3B does not decide whether the computer is Ready. Run P3A's audit again to check.");
        _result.Text = string.Join(Environment.NewLine, lines);

        _smokeChecks.Items.Clear();
        if (run.SmokeTest is { } smokeTest)
        {
            _smokeSummary.Text = $"Verification: {StatusText.Verification(smokeTest)}. {smokeTest.Message}{Environment.NewLine}{smokeTest.ReadinessNote}";
            foreach (var check in smokeTest.Checks)
            {
                _smokeChecks.Items.Add(new ListViewItem([check.Description, StatusText.Check(check.Status), check.Explanation]));
            }
        }
        else
        {
            _smokeSummary.Text = "Smoke tests were not run, because the fix did not complete successfully.";
        }
    }

    private async Task RefreshReversalsAsync()
    {
        try
        {
            var entries = await _controller.GetPendingReversalsAsync(CancellationToken.None);
            _rebuildingLists = true;
            _reversals.BeginUpdate();
            _reversals.Items.Clear();
            foreach (var entry in entries.OrderByDescending(e => e.RecordedAt))
            {
                _reversals.Items.Add(new ListViewItem(
                [
                    entry.RecordedAt.ToLocalTime().ToString("g"),
                    entry.ToolId,
                    StatusText.Reversibility(entry.Kind),
                    string.Join("; ", entry.ChangesToReverse),
                ])
                { Tag = entry });
            }

            _reversals.EndUpdate();
            _rebuildingLists = false;
            _reversalTab.Text = entries.Count == 0 ? "Reversal records" : $"Reversal records ({entries.Count})";
            ShowSelectedReversal();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _reversalDetails.Text = $"The reversal records could not be read: {ex.Message}";
        }
    }

    private void ShowSelectedReversal()
    {
        _reversalDetails.Text = SelectedReversal() is { } entry
            ? string.Join(
                Environment.NewLine,
                $"Tool: {entry.ToolId}    Issue: {entry.IssueId}    Recorded: {entry.RecordedAt.ToLocalTime():g}",
                $"Reversal: {StatusText.Reversibility(entry.Kind)}",
                $"How: {entry.Method}",
                $"Before the change: {entry.PriorState ?? "(not recorded)"}",
                $"Changes: {string.Join("; ", entry.ChangesToReverse)}")
            : _reversals.Items.Count == 0
                ? "No changes are waiting to be reversed."
                : "Select a record to see how it can be reversed.";
        RefreshState();
    }

    private IssueItem? SelectedIssue() =>
        _issues.SelectedItems.Count > 0 ? _issues.SelectedItems[0].Tag as IssueItem : null;

    private ReversalJournalEntry? SelectedReversal() =>
        _reversals.SelectedItems.Count > 0 ? _reversals.SelectedItems[0].Tag as ReversalJournalEntry : null;

    private string? AskForFile(string title)
    {
        using var dialog = new OpenFileDialog { Filter = JsonFilter, Title = title, CheckFileExists = true };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void ShowProblem(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
