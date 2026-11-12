namespace P3B.Remediation.Windows.Tests;

/// <summary>Locates the harmless P3B.TestProcess helper built alongside this test project.</summary>
internal static class TestProcessHelper
{
    public static string ExecutablePath
    {
        get
        {
            // tests\<this project>\bin\<Configuration>\<tfm>\  ->  tests\P3B.TestProcess\bin\<Configuration>\net10.0\
            var tfmDirectory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            var configuration = tfmDirectory.Parent!.Name;
            var testsDirectory = tfmDirectory.Parent.Parent!.Parent!.Parent!;
            var path = Path.Combine(testsDirectory.FullName, "P3B.TestProcess", "bin", configuration, "net10.0", "P3B.TestProcess.exe");
            Assert.True(File.Exists(path), $"Helper process not found at {path}");
            return path;
        }
    }
}
