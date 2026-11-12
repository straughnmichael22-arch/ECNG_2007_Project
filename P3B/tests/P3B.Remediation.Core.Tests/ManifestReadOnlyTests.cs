using System.Reflection;
using System.Security.Cryptography;
using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;
using P3B.Remediation.Core.Results;
using P3B.Remediation.Core.Tests.Fakes;

namespace P3B.Remediation.Core.Tests;

/// <summary>P3B-DR-02: the supplied manifest is reference data and is never modified.</summary>
public class ManifestReadOnlyTests
{
    private static readonly string[] WritingVerbs = ["Write", "Save", "Update", "Set", "Delete", "Modify", "Add", "Remove"];

    [Fact]
    public void Manifest_seam_exposes_no_write_operations()
    {
        var seam = typeof(IManifestSource);

        Assert.All(seam.GetProperties(), p => Assert.False(p.CanWrite, $"{p.Name} is writable"));
        Assert.All(
            seam.GetMethods().Where(m => !m.IsSpecialName),
            m => Assert.DoesNotContain(WritingVerbs, verb => m.Name.StartsWith(verb, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(typeof(LocalEdaToolManifest))]
    [InlineData(typeof(ManifestTool))]
    [InlineData(typeof(ManifestRemediation))]
    public void Manifest_model_has_no_public_setters(Type modelType)
    {
        var settable = modelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name);

        Assert.Empty(settable);
    }

    [Fact]
    public void Loaded_tool_lists_cannot_be_modified_through_casts()
    {
        var manifest = new ManifestService(TestManifests.FixturePath("valid.provisional.json")).Load().Manifest;

        Assert.NotNull(manifest);
        Assert.Throws<NotSupportedException>(() => ((IList<ManifestTool>)manifest.Tools).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)manifest.Tools[0].ApprovedVersions)[0] = "TEST-CHANGED");
    }

    [Fact]
    public async Task Load_and_full_remediation_run_leave_a_read_only_manifest_file_unchanged()
    {
        using var temp = new TempDirectory();
        var manifestPath = temp.CopyFixture("valid.provisional.json");
        File.SetAttributes(manifestPath, FileAttributes.ReadOnly);
        var hashBefore = Hash(manifestPath);
        var writtenBefore = File.GetLastWriteTimeUtc(manifestPath);

        var manifest = new ManifestService(manifestPath);
        Assert.Equal(ManifestLoadStatus.Loaded, manifest.Load().Status);

        var service = TestSupport.Service(new FakeExecutor(), manifest: manifest);
        var run = await service.RemediateAsync(TestSupport.Request());

        Assert.Equal(RemediationOutcome.Succeeded, run.Remediation.Outcome);
        Assert.Equal(hashBefore, Hash(manifestPath));
        Assert.Equal(writtenBefore, File.GetLastWriteTimeUtc(manifestPath));
        Assert.True(File.GetAttributes(manifestPath).HasFlag(FileAttributes.ReadOnly));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
