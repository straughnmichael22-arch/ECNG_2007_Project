using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.WinForms.Presentation;

namespace P3B.Remediation.WinForms.Tests;

public class LabelAndDialogTests
{
    private static ActionPreview Preview(ReversalExpectation expectation)
    {
        var tool = new ManifestTool(new ToolReference("TEST-TOOL-A"), "Test tool A", ["TEST-VERSION-1"], new ManifestRemediation(RemediationMethod.WinGet, "TEST.Package.A"));
        var action = new RemediationAction(new RemediationRequest(new ToolReference("TEST-TOOL-A"), new IssueReference("TEST-ISSUE"), AuditIssueState.Missing), tool);
        return new ActionPreview(action, ["TEST change"], "TEST reversal", expectation);
    }

    [Fact]
    public void Approval_needs_an_extra_confirmation_only_when_not_fully_reversible()
    {
        Assert.False(ApprovalDialog.RequiresAcknowledgement(Preview(ReversalExpectation.Reversible)));
        Assert.True(ApprovalDialog.RequiresAcknowledgement(Preview(ReversalExpectation.PartiallyReversible)));
        Assert.True(ApprovalDialog.RequiresAcknowledgement(Preview(ReversalExpectation.NotReversible)));
    }

    [Fact]
    public void Approval_text_shows_tool_version_changes_reversal_and_reversibility()
    {
        var text = ApprovalDialog.Describe(Preview(ReversalExpectation.PartiallyReversible));

        Assert.Contains("TEST-TOOL-A", text);
        Assert.Contains("TEST-VERSION-1", text);
        Assert.Contains("TEST change", text);
        Assert.Contains("How to reverse: TEST reversal", text);
        Assert.Contains("PARTIAL", text);
    }

    [Fact]
    public void Only_a_completed_reversal_is_labelled_reversed()
    {
        foreach (var outcome in Enum.GetValues<RollbackOutcome>())
        {
            var label = StatusText.Rollback(outcome);
            if (outcome == RollbackOutcome.Reversed)
            {
                Assert.Equal("Reversed", label);
            }
            else
            {
                Assert.False(label.StartsWith("Reversed", StringComparison.Ordinal), $"{outcome}: {label}");
            }
        }
    }

    [Fact]
    public void Every_remediation_outcome_has_a_label_and_unsupported_is_not_failed()
    {
        foreach (var outcome in Enum.GetValues<RemediationOutcome>())
        {
            Assert.False(string.IsNullOrWhiteSpace(StatusText.Outcome(outcome)));
        }

        Assert.Equal(StatusText.Successful, StatusText.Outcome(RemediationOutcome.Succeeded));
        Assert.Equal(StatusText.Failed, StatusText.Outcome(RemediationOutcome.Failed));
        Assert.Equal("Not supported", StatusText.Outcome(RemediationOutcome.Unsupported));
    }

    [Theory]
    [InlineData(SmokeTestOutcome.Passed, "Passed")]
    [InlineData(SmokeTestOutcome.Failed, "Failed")]
    [InlineData(SmokeTestOutcome.TimedOut, "Timed out")]
    [InlineData(SmokeTestOutcome.Unsupported, "Not verified")]
    [InlineData(SmokeTestOutcome.Cancelled, "Cancelled")]
    public void Verification_labels(SmokeTestOutcome outcome, string expected)
    {
        var result = new SmokeTestResult
        {
            PreviewId = Guid.NewGuid(),
            Tool = new ToolReference("TEST-TOOL-A"),
            Outcome = outcome,
            Message = "TEST",
            CompletedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal(expected, StatusText.Verification(result));
    }

    [Theory]
    [InlineData("missing", "Not installed")]
    [InlineData("unapprovedVersion", "Unapproved version")]
    [InlineData("misconfigured", "Set up incorrectly")]
    [InlineData(null, "(not stated)")]
    public void Problems_are_described_in_plain_language(string? state, string expected)
    {
        Assert.Equal(expected, StatusText.Problem(state));
    }

    [Fact]
    public void Command_line_options_are_read()
    {
        string[] args = ["--manifest", @"C:\TEST\manifest.json", "--AUDIT", @"C:\TEST\audit.json"];

        Assert.Equal(@"C:\TEST\manifest.json", Program.ValueAfter(args, "--manifest"));
        Assert.Equal(@"C:\TEST\audit.json", Program.ValueAfter(args, "--audit"));
        Assert.Null(Program.ValueAfter(["--manifest"], "--manifest"));
    }
}
