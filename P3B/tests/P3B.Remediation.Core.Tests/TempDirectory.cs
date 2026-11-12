namespace P3B.Remediation.Core.Tests;

/// <summary>A throwaway folder under the user's temp directory, deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "p3b-tests", Guid.NewGuid().ToString("N"))).FullName;
    }

    public string Root { get; }

    public string PathOf(string fileName) => Path.Combine(Root, fileName);

    public string WriteText(string fileName, string content)
    {
        var path = PathOf(fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public string CopyFixture(string fixtureFileName)
    {
        var path = PathOf(fixtureFileName);
        File.Copy(TestManifests.FixturePath(fixtureFileName), path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Root))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Root, recursive: true);
    }
}
