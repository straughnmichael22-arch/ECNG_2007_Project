using System.Collections.Generic;

namespace EDAEnvironmentManager
{
    // --------------------------------------------------
    // MANIFEST CLASSES
    // Moved unchanged from AuditWindow.cs.
    // --------------------------------------------------

    public class EdaManifest
    {
        public string ManifestVersion { get; set; } = "";

        public List<EdaTool> Tools { get; set; } = new();

        public List<EdaExtension> Extensions { get; set; } = new();
    }

    public class EdaTool
    {
        public string Name { get; set; } = "";

        public string Type { get; set; } = "";

        public string Executable { get; set; } = "";

        public string WhereCommand { get; set; } = "";

        public string MinimumApprovedVersion { get; set; } = "";

        public string MaximumApprovedVersion { get; set; } = "";

        public string VersionArguments { get; set; } = "";

        public string FolderPattern { get; set; } = "";

        public string RelativeExecutablePath { get; set; } = "";

        public List<string> SearchRoots { get; set; } = new();

        public List<string> CommonPaths { get; set; } = new();

        public List<string> ExpectedPaths { get; set; } = new();

        public ComponentActions Actions { get; set; } = new();

        public SmokeTestSettings? SmokeTest { get; set; }

        public RemediationSettings? Remediation { get; set; }
    }

    public class SmokeTestSettings
    {
        public bool Enabled { get; set; }

        public string Method { get; set; } = "";

        public string TestFile { get; set; } = "";

        public string Entity { get; set; } = "";

        public string Standard { get; set; } = "";

        public string Source { get; set; } = "";

        public string ExpectedOutput { get; set; } = "";
    }

    public class RemediationSettings
    {
        public string DownloadUrl { get; set; } = "";

        public List<string> Instructions { get; set; } = new();
    }

    public class EdaExtension
    {
        public string Name { get; set; } = "";

        public string Type { get; set; } = "";

        public string Editor { get; set; } = "";

        public List<string> ApprovedExtensions { get; set; } = new();

        public string ExtensionId { get; set; } = "";

        public bool CheckEnabled { get; set; }

        public ComponentActions Actions { get; set; } = new();
    }

    public class ComponentActions
    {
        public bool ShowSmokeTest { get; set; }

        public bool ShowRemediation { get; set; }
    }
}
