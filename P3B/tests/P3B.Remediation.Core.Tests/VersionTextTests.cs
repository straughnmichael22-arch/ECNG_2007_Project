using P3B.Remediation.Core.Verification;

namespace P3B.Remediation.Core.Tests;

public class VersionTextTests
{
    [Theory]
    [InlineData("TEST tool 1.2.3", "1.2.3")]
    [InlineData("TEST tool v1.2.3 (build 9)", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("version: 1.2.3.", "1.2.3")]
    [InlineData("TEST tool 1.2.3-dev", "1.2.3")]
    public void Whole_version_is_found(string text, string version)
    {
        Assert.True(VersionText.ContainsVersion(text, version));
    }

    [Theory]
    [InlineData("TEST tool 11.2.3", "1.2.3")]
    [InlineData("TEST tool 1.2.30", "1.2.3")]
    [InlineData("TEST tool 1.2.3.4", "1.2.3")]
    [InlineData("TEST tool 0.1.2.3", "1.2.3")]
    [InlineData("TEST tool dev1.2.3", "1.2.3")]
    [InlineData("TEST tool 1.2", "1.2.3")]
    public void Partial_or_different_versions_are_not_matched(string text, string version)
    {
        Assert.False(VersionText.ContainsVersion(text, version));
    }
}
