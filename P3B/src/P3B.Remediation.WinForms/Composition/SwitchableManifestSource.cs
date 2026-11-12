using P3B.Remediation.Core.Integration;
using P3B.Remediation.Core.Manifest;

namespace P3B.Remediation.WinForms.Composition;

/// <summary>The manifest the user has chosen; unconfigured until a file is opened.</summary>
public sealed class SwitchableManifestSource : IManifestSource
{
    private IManifestSource _current = new UnconfiguredManifestSource();

    public string? ManifestPath { get; private set; }

    public void UseFile(string path)
    {
        var service = new ManifestService(path);
        Volatile.Write(ref _current, service);
        ManifestPath = service.ManifestPath;
    }

    public ManifestLoadResult Load() => Volatile.Read(ref _current).Load();
}
