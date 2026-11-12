using P3B.Remediation.Core.Actions;

namespace P3B.Remediation.WinForms;

/// <summary>
/// Shows the affected tool, target version, proposed changes and reversal method before the user
/// decides (3B-UR-001). Enter does not approve; the user must click Approve. If the change may not
/// be fully reversible, Approve stays disabled until the user confirms they understand.
/// </summary>
internal sealed class ApprovalDialog : Form
{
    internal const string AcknowledgementText = "I understand that this change may not be fully reversible.";

    public ApprovalDialog(ActionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        Text = "Approve this change?";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 420);
        Font = new Font("Segoe UI", 9F);

        var heading = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 48,
            Padding = new Padding(8, 8, 8, 0),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Text = $"P3B wants to install {preview.Action.Tool.Name} version {preview.TargetVersion ?? "(not applicable)"}.",
        };

        var details = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = Describe(preview),
        };
        MainForm.KeepUnselected(details);

        var approve = new Button { Text = "Approve", AutoSize = true, DialogResult = DialogResult.OK };
        var reject = new Button { Text = "Do not change anything", AutoSize = true, DialogResult = DialogResult.Cancel };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(reject);
        buttons.Controls.Add(approve);

        Controls.Add(details);
        Controls.Add(heading);

        if (RequiresAcknowledgement(preview))
        {
            var acknowledge = new CheckBox { Text = AcknowledgementText, AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(8, 4, 8, 0) };
            approve.Enabled = false;
            acknowledge.CheckedChanged += (_, _) => approve.Enabled = acknowledge.Checked;
            Controls.Add(acknowledge);
        }

        Controls.Add(buttons);
        CancelButton = reject;

        // The safe choice has the focus, so pressing Enter by accident never approves a change.
        ActiveControl = reject;
    }

    /// <summary>True when the user must confirm before approving a change that may not be fully reversible.</summary>
    internal static bool RequiresAcknowledgement(ActionPreview preview) =>
        preview.ExpectedReversal != ReversalExpectation.Reversible;

    internal static string Describe(ActionPreview preview)
    {
        var lines = new List<string>
        {
            $"Affected tool: {preview.Tool}",
            $"Target version: {preview.TargetVersion ?? "Not applicable"}",
            string.Empty,
            "Proposed changes:",
        };
        lines.AddRange(preview.ProposedChanges.Select(change => $"  - {change}"));
        lines.Add(string.Empty);
        lines.Add($"How to reverse: {preview.ReversalMethod}");
        lines.Add(string.Empty);
        lines.Add(preview.ExpectedReversal switch
        {
            ReversalExpectation.Reversible => "Reversibility: P3B expects to be able to reverse this change.",
            ReversalExpectation.PartiallyReversible =>
                "Reversibility: PARTIAL. Another version is installed. If it is replaced, P3B cannot restore it automatically.",
            _ => "Reversibility: NONE. P3B cannot reverse this change.",
        });
        return string.Join(Environment.NewLine, lines);
    }
}
