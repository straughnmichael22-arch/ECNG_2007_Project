using P3B.Remediation.Core.Actions;
using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Verification;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>The core check handlers against in-memory fakes only.</summary>
public class SmokeCheckHandlerTests
{
    private static RemediationAction Action() => TestSupport.Action();

    [Fact]
    public async Task Installed_version_passes_when_the_approved_version_is_installed()
    {
        var installer = new FakeInstaller { States = [InstalledState.TargetVersion(TestManifests.TargetVersion, "TEST listed")] };

        var result = await new InstalledVersionCheckHandler([installer]).RunAsync(new InstalledVersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Passed, result.Status);
    }

    [Fact]
    public async Task Installed_version_fails_when_another_version_is_installed()
    {
        var installer = new FakeInstaller { States = [InstalledState.OtherVersion("TEST-VERSION-NEWER", "TEST listed")] };

        var result = await new InstalledVersionCheckHandler([installer]).RunAsync(new InstalledVersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Failed, result.Status);
        Assert.Contains("TEST-VERSION-NEWER", result.Explanation);
        Assert.Contains(TestManifests.TargetVersion, result.Explanation);
    }

    [Theory]
    [InlineData(InstalledStateKind.NotInstalled, "is not installed")]
    [InlineData(InstalledStateKind.Unknown, "could not be confirmed")]
    public async Task Installed_version_fails_with_a_reason_when_not_confirmed(InstalledStateKind kind, string reason)
    {
        var installer = new FakeInstaller { States = [new InstalledState(kind, null, "TEST evidence")] };

        var result = await new InstalledVersionCheckHandler([installer]).RunAsync(new InstalledVersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Failed, result.Status);
        Assert.Contains(reason, result.Explanation);
    }

    [Fact]
    public async Task Installed_version_is_unsupported_without_an_installer_for_the_method()
    {
        var result = await new InstalledVersionCheckHandler([]).RunAsync(new InstalledVersionCheck(), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Unsupported, result.Status);
    }

    [Fact]
    public async Task File_exists_expands_variables_and_finds_the_file()
    {
        var files = new FakeFileProbe();
        files.Variables["LOCALAPPDATA"] = @"C:\Users\TEST\AppData\Local";
        files.Files.Add(@"C:\Users\TEST\AppData\Local\TEST\tool.exe");

        var result = await new FileExistsCheckHandler(files).RunAsync(new FileExistsCheck(@"%LOCALAPPDATA%\TEST\tool.exe"), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Passed, result.Status);
    }

    [Theory]
    [InlineData(@"C:\TEST\missing.exe", "does not exist")]
    [InlineData(@"%TEST_UNDEFINED%\tool.exe", "not defined")]
    public async Task File_exists_fails_with_a_reason(string path, string reason)
    {
        var result = await new FileExistsCheckHandler(new FakeFileProbe()).RunAsync(new FileExistsCheck(path), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Failed, result.Status);
        Assert.Contains(reason, result.Explanation);
    }

    [Fact]
    public async Task Command_on_path_reports_where_it_was_found()
    {
        var locator = new FakeCommandLocator();
        locator.Commands["TEST-tool"] = @"C:\TEST\bin\TEST-tool.exe";

        var found = await new CommandOnPathCheckHandler(locator).RunAsync(new CommandOnPathCheck("TEST-tool"), Action(), CancellationToken.None);
        var missing = await new CommandOnPathCheckHandler(locator).RunAsync(new CommandOnPathCheck("TEST-absent"), Action(), CancellationToken.None);

        Assert.Equal(SmokeCheckStatus.Passed, found.Status);
        Assert.Contains(@"C:\TEST\bin\TEST-tool.exe", found.Explanation);
        Assert.Equal(SmokeCheckStatus.Failed, missing.Status);
        Assert.Contains("not found", missing.Explanation);
    }

    [Theory]
    [InlineData(@"C:\Other;C:\TEST\bin", SmokeCheckStatus.Passed)]
    [InlineData(@"C:\Other", SmokeCheckStatus.Failed)]
    [InlineData(null, SmokeCheckStatus.Failed)]
    public async Task User_environment_variable_check(string? value, SmokeCheckStatus expected)
    {
        var environment = new FakeUserEnvironment();
        environment.SetExternally("Path", value);

        var result = await new UserEnvironmentVariableCheckHandler(environment)
            .RunAsync(new UserEnvironmentVariableCheck("Path", @"c:\test\BIN"), Action(), CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
    }
}
