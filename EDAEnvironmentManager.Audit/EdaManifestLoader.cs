using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EDAEnvironmentManager.Audit
{
    // --------------------------------------------------
    // MANIFEST LOADING
    // Moved from AuditWindow.LoadManifest; the status messages are unchanged.
    // --------------------------------------------------

    public static class EdaManifestLoader
    {
        public const string FileName = "eda-tool-manifest.json";

        public static string DefaultPath =>
            Path.Combine(AppContext.BaseDirectory, FileName);

        public static EdaManifest? TryLoad(string manifestPath, out string statusMessage)
        {
            try
            {
                if (!File.Exists(manifestPath))
                {
                    statusMessage = "Manifest Not Found.";
                    return null;
                }

                string json = File.ReadAllText(manifestPath);

                EdaManifest? manifest = JsonSerializer.Deserialize<EdaManifest>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

                if (manifest == null)
                {
                    statusMessage = "Could not read manifest.";
                    return null;
                }

                manifest.Tools ??= new List<EdaTool>();
                manifest.Extensions ??= new List<EdaExtension>();

                statusMessage = "Manifest Found. Ready to audit.";
                return manifest;
            }
            catch (Exception ex)
            {
                statusMessage = "Error loading manifest: " + ex.Message;
                return null;
            }
        }
    }
}
