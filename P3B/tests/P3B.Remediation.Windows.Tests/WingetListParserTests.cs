using P3B.Remediation.Windows.WinGet;

namespace P3B.Remediation.Windows.Tests;

public class WingetListParserTests
{
    [Fact]
    public void Finds_version_in_captured_real_output()
    {
        var match = WingetListParser.FindPackage(CapturedWingetOutput.ListInstalled, CapturedWingetOutput.InstalledPackageId);

        Assert.NotNull(match);
        Assert.Equal(CapturedWingetOutput.InstalledVersion, match.Version);
        Assert.Contains(CapturedWingetOutput.InstalledPackageId, match.Line);
    }

    [Fact]
    public void Absent_package_message_has_no_match()
    {
        Assert.Null(WingetListParser.FindPackage(CapturedWingetOutput.ListAbsent, "TEST.Package"));
    }

    [Fact]
    public void Other_package_in_the_output_is_not_matched()
    {
        Assert.Null(WingetListParser.FindPackage(CapturedWingetOutput.ListInstalled, "TEST.Other"));
        Assert.Null(WingetListParser.FindPackage(CapturedWingetOutput.ListInstalled, "Microsoft.VisualStudio"));
    }

    [Fact]
    public void Progress_spinner_before_the_table_is_ignored()
    {
        var output = "\r-\r\\\r|\r/\r" + CapturedWingetOutput.ListInstalled;

        var match = WingetListParser.FindPackage(output, CapturedWingetOutput.InstalledPackageId);

        Assert.Equal(CapturedWingetOutput.InstalledVersion, match?.Version);
    }

    [Fact]
    public void Id_match_is_case_insensitive_like_winget()
    {
        var match = WingetListParser.FindPackage(CapturedWingetOutput.ListInstalled, "microsoft.visualstudiocode");

        Assert.Equal(CapturedWingetOutput.InstalledVersion, match?.Version);
    }

    [Fact]
    public void Version_range_prefix_is_skipped()
    {
        const string Output = "Name   Id            Version\n-----------------------------\nTEST   TEST.Package  < 2.0.1\n";

        Assert.Equal("2.0.1", WingetListParser.FindPackage(Output, "TEST.Package")?.Version);
    }

    [Fact]
    public void Truncated_id_is_not_guessed()
    {
        const string Output = "Name   Id                 Version\n----------------------------------\nTEST   TEST.VeryLongPac…  1.0\n";

        Assert.Null(WingetListParser.FindPackage(Output, "TEST.VeryLongPackageName"));
    }
}
