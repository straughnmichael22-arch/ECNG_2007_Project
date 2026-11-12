using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Reversal;
using P3B.Remediation.Core.Safety;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>InstallationService with a scripted installer: nothing is ever installed.</summary>
public class InstallationServiceTests
{
    private static (InstallationService Service, ActionPreview Preview, ApprovalDecision Approval) Prepare(
        FakeInstaller installer,
        RemediationAction? action = null)
    {
        var service = new InstallationService([installer]);
        var preview = service.CreatePreview(action ?? TestSupport.Action());
        return (service, preview, ApprovalDecision.Approve(preview));
    }

    [Fact]
    public void Preview_shows_tool_target_version_changes_and_reversal_method()
    {
        var (_, preview, _) = Prepare(new FakeInstaller());

        Assert.Equal(TestManifests.ToolA, preview.Tool.Value);
        Assert.Equal(TestManifests.TargetVersion, preview.TargetVersion);
        Assert.Contains(preview.ProposedChanges, c => c.Contains(TestManifests.TargetVersion, StringComparison.Ordinal));
        Assert.Contains(preview.ProposedChanges, c => c.Contains("checks that", StringComparison.Ordinal));
        Assert.Contains("cannot be reversed automatically", preview.ReversalMethod);
    }

    [Fact]
    public async Task Without_approval_nothing_is_queried_or_installed()
    {
        var installer = new FakeInstaller();
        var (service, preview, _) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, ApprovalDecision.Reject(preview), CancellationToken.None);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(0, installer.StateCalls);
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public async Task Unavailable_installer_is_reported_before_anything_runs()
    {
        var installer = new FakeInstaller { Available = false };
        var (service, preview, approval) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Contains("not available", report.Message);
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public async Task Unknown_prior_state_stops_before_installing()
    {
        var installer = new FakeInstaller { States = [InstalledState.Unknown("TEST cannot tell")] };
        var (service, preview, approval) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Contains("TEST cannot tell", report.Message);
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public async Task Already_installed_target_version_changes_nothing()
    {
        var installer = new FakeInstaller { States = [InstalledState.TargetVersion(TestManifests.TargetVersion, "TEST present")] };
        var (service, preview, approval) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Succeeded, report.Status);
        Assert.Empty(report.ObservedChanges);
        Assert.Equal(ReversalKind.NothingToReverse, report.Reversal?.Kind);
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public async Task Verified_new_install_succeeds_with_automatic_uninstall_reversal()
    {
        var installer = new FakeInstaller();
        var (service, preview, approval) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Succeeded, report.Status);
        Assert.Contains("verified", report.Message);
        Assert.Single(report.ObservedChanges);
        var reversal = Assert.IsType<ReversalInfo>(report.Reversal);
        Assert.Equal(ReversalKind.Automatic, reversal.Kind);
        var uninstall = Assert.IsType<WingetUninstallReversal>(reversal.Operation);
        Assert.Equal($"TEST-SOURCE-{TestManifests.ToolA}", uninstall.PackageId);
        Assert.Equal(TestManifests.TargetVersion, uninstall.Version);
    }

    [Fact]
    public async Task Installer_success_without_verification_is_reported_as_failure()
    {
        var installer = new FakeInstaller
        {
            States = [InstalledState.NotInstalled("TEST before"), InstalledState.NotInstalled("TEST still absent")],
        };
        var (service, preview, approval) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Contains("could not verify", report.Message);
        Assert.Equal(ReversalKind.NothingToReverse, report.Reversal?.Kind);
    }

    [Fact]
    public async Task Failed_installer_is_reported_with_its_exit_code()
    {
        var installer = new FakeInstaller
        {
            States = [InstalledState.NotInstalled("TEST before")],
            Attempt = new InstallAttempt(InstallAttemptStatus.Failed, unchecked((int)0x8A150011), "TEST installer error"),
        };
        var (service, preview, approval) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Contains("0x8A150011", report.Message);
        Assert.Contains("TEST installer error", report.Message);
    }

    [Fact]
    public async Task Timed_out_installer_with_partial_install_is_failed_but_reversible()
    {
        var installer = new FakeInstaller
        {
            Attempt = new InstallAttempt(InstallAttemptStatus.TimedOut, null, "TEST stopped after timeout"),
        };
        var (service, preview, approval) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Contains("TimedOut", report.Message);
        Assert.Equal(ReversalKind.Automatic, report.Reversal?.Kind);
    }

    [Fact]
    public async Task Cancelled_installation_is_checked_afterwards_and_reports_partial_changes()
    {
        var installer = new FakeInstaller { ThrowCancellationDuringInstall = true };
        var (service, preview, approval) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Cancelled, report.Status);
        Assert.Equal(2, installer.StateCalls);
        Assert.Single(report.ObservedChanges);
        Assert.Equal(ReversalKind.Automatic, report.Reversal?.Kind);
    }

    [Fact]
    public async Task Replacing_another_version_is_reported_as_manual_only_reversal()
    {
        var installer = new FakeInstaller
        {
            States =
            [
                InstalledState.OtherVersion("TEST-VERSION-UNAPPROVED", "TEST other"),
                InstalledState.TargetVersion(TestManifests.TargetVersion, "TEST now approved"),
            ],
        };
        var (service, preview, approval) = Prepare(installer);

        var report = await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Succeeded, report.Status);
        Assert.Equal(ReversalKind.ManualOnly, report.Reversal?.Kind);
        Assert.Null(report.Reversal?.Operation);
        Assert.Contains("cannot restore the previous version", report.Reversal?.Method);
    }

    [Fact]
    public async Task Only_the_manifest_approved_target_version_is_installed_never_the_detected_one()
    {
        var installer = new FakeInstaller();
        var request = TestSupport.Request(
            state: Integration.AuditIssueState.UnapprovedVersion,
            detectedVersion: "TEST-VERSION-NEWER-UNAPPROVED");
        var (service, preview, approval) = Prepare(installer, TestSupport.Action(request));

        await service.ExecuteAsync(preview, approval, CancellationToken.None);

        Assert.Equal(TestManifests.TargetVersion, installer.LastInstallTarget?.Version);
    }

    [Fact]
    public async Task Vivado_is_never_installed_even_if_the_manifest_allows_it()
    {
        var installer = new FakeInstaller();
        var vivado = TestManifests.Tool("TEST-Vivado-Tool");
        var action = new RemediationAction(TestSupport.Request("TEST-Vivado-Tool"), vivado);
        var service = new InstallationService([installer]);

        Assert.False(service.CanHandle(action));

        var preview = service.CreatePreview(action);
        var report = await service.ExecuteAsync(preview, ApprovalDecision.Approve(preview), CancellationToken.None);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(ProhibitedSoftwarePolicy.VivadoMessage, report.Message);
        Assert.Equal(0, installer.StateCalls);
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public async Task End_to_end_through_the_remediation_service_records_the_reversal()
    {
        var journal = new InMemoryReversalJournal();
        var service = TestSupport.Service(new InstallationService([new FakeInstaller()]), journal: journal);

        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Succeeded, run.Remediation.Outcome);
        var entry = Assert.Single(journal.Entries);
        Assert.Equal(run.Remediation.ReversalJournalEntryId, entry.EntryId);
        Assert.IsType<WingetUninstallReversal>(entry.Operation);
    }

    [Fact]
    public void Archive_method_has_no_installer_so_it_is_not_handled()
    {
        var service = new InstallationService([new FakeInstaller()]);
        var archiveTool = TestManifests.Tool(TestManifests.ToolA, RemediationMethod.OfficialArchive);

        Assert.False(service.CanHandle(new RemediationAction(TestSupport.Request(), archiveTool)));
    }
}
