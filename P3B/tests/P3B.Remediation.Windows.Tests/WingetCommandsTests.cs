using P3B.Remediation.Windows.WinGet;

namespace P3B.Remediation.Windows.Tests;

public class WingetCommandsTests
{
    [Fact]
    public void Install_is_exact_version_pinned_per_user_and_winget_source_only()
    {
        var arguments = WingetCommands.Install("TEST.Package", "1.2.3");

        Assert.Equal(
            [
                "install", "--id", "TEST.Package", "--exact", "--version", "1.2.3",
                "--source", "winget", "--scope", "user", "--silent",
                "--accept-package-agreements", "--disable-interactivity",
            ],
            arguments);
    }

    [Fact]
    public void Uninstall_targets_exactly_the_recorded_package_and_version()
    {
        var arguments = WingetCommands.Uninstall("TEST.Package", "1.2.3");

        Assert.Equal(
            [
                "uninstall", "--id", "TEST.Package", "--exact", "--version", "1.2.3",
                "--source", "winget", "--scope", "user", "--silent", "--disable-interactivity",
            ],
            arguments);
    }

    [Fact]
    public void List_is_read_only_and_restricted_to_the_winget_source()
    {
        Assert.Equal(
            ["list", "--id", "TEST.Package", "--exact", "--source", "winget", "--disable-interactivity"],
            WingetCommands.List("TEST.Package"));
    }

    [Fact]
    public void No_command_forces_overrides_targets_the_machine_or_accepts_source_agreements()
    {
        string[] forbidden = ["--force", "--override", "--custom", "machine", "--accept-source-agreements", "--ignore-security-hash"];
        var all = WingetCommands.Install("TEST.Package", "1.0")
            .Concat(WingetCommands.Uninstall("TEST.Package", "1.0"))
            .Concat(WingetCommands.List("TEST.Package"))
            .Concat(WingetCommands.Version());

        Assert.DoesNotContain(all, a => forbidden.Contains(a, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("--force")]
    [InlineData("-h")]
    [InlineData("TEST Package")]
    [InlineData("TEST.Package;calc")]
    [InlineData("TEST.Package&calc")]
    [InlineData("TEST.Package|calc")]
    [InlineData("TEST\"Package")]
    [InlineData("TEST.Package\n--force")]
    [InlineData("TEST.Package\n")]
    [InlineData("")]
    public void Unsafe_identifiers_are_rejected(string value)
    {
        Assert.False(WingetCommands.IsSafeToken(value));
        Assert.Throws<ArgumentException>(() => WingetCommands.Install(value, "1.0"));
        Assert.Throws<ArgumentException>(() => WingetCommands.Install("TEST.Package", value));
    }

    [Theory]
    [InlineData("Microsoft.VisualStudioCode")]
    [InlineData("TEST.Package-Name_2")]
    [InlineData("1.144.0")]
    [InlineData("4.1.0+build5")]
    public void Normal_identifiers_and_versions_are_accepted(string value)
    {
        Assert.True(WingetCommands.IsSafeToken(value));
    }

    [Fact]
    public void Exit_codes_are_formatted_as_hresults()
    {
        Assert.Equal("0x8A150014", WingetCommands.FormatExitCode(WingetCommands.NoInstalledPackageFound));
    }
}
