using System.Text.Json;
using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// Parses and validates a manifest written in the P3B provisional format.
/// </summary>
/// <remarks>
/// PROVISIONAL – this is NOT the group's manifest; P3A's format is read by
/// <see cref="P3AManifestSchema"/>. This format implements
/// docs/proposals/local-eda-tool-manifest.PROVISIONAL.md and is kept because it is the only
/// format that can express an automatic installation (an exact version and a WinGet package
/// identifier), which P3A's manifest does not yet provide. Any other version is rejected as
/// unsupported rather than guessed at.
/// <para>
/// Property names are case-sensitive. Properties P3B does not use (for example P3A
/// detection data) are ignored.
/// </para>
/// </remarks>
public static class ProvisionalManifestSchema
{
    public const string SupportedVersion = "p3b-provisional-0";

    private const string MissingProperty = "Required property is missing.";
    private const int MaximumSmokeTestTimeoutSeconds = 300;

    /// <summary>Used when a <c>versionCommand</c> smoke test gives no <c>timeoutSeconds</c>.</summary>
    public static readonly TimeSpan DefaultSmokeTestTimeout = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<string, RemediationMethod> Methods = new(StringComparer.Ordinal)
    {
        ["winget"] = RemediationMethod.WinGet,
        ["vscodeExtension"] = RemediationMethod.VsCodeExtension,
        ["officialArchive"] = RemediationMethod.OfficialArchive,
        ["linkToInstaller"] = RemediationMethod.LinkToInstaller,
        ["none"] = RemediationMethod.None,
    };

    /// <param name="utf8Json">The manifest content (UTF-8, without a byte-order mark).</param>
    /// <param name="source">Where the content came from, for messages.</param>
    public static ManifestLoadResult Parse(ReadOnlyMemory<byte> utf8Json, string source)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json);
        }
        catch (JsonException ex)
        {
            return ManifestLoadResult.Failed(
                ManifestLoadStatus.Malformed,
                $"The manifest at {source} is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            return Validate(document.RootElement, source);
        }
    }

    internal static ManifestLoadResult Validate(JsonElement root, string source)
    {
        var errors = new List<ManifestValidationError>();

        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ManifestValidationError("$", "The manifest must be a JSON object."));
            return Invalid(source, errors);
        }

        var version = RequiredString(root, "$", "manifestVersion", errors);
        if (version is not null && version != SupportedVersion)
        {
            return ManifestLoadResult.Failed(
                ManifestLoadStatus.UnsupportedVersion,
                $"The manifest at {source} declares version '{version}', which P3B does not support. "
                + $"P3B reads P3A's manifest format '{P3AManifestSchema.SupportedVersion}' and the provisional test format '{SupportedVersion}'.");
        }

        var tools = ReadTools(root, errors);

        if (errors.Count > 0 || version is null)
        {
            return Invalid(source, errors);
        }

        return ManifestLoadResult.Loaded(
            new LocalEdaToolManifest(version, tools),
            $"Loaded {tools.Count} tool definition(s) from {source} (provisional format {SupportedVersion}).");
    }

    private static List<ManifestTool> ReadTools(JsonElement root, List<ManifestValidationError> errors)
    {
        var tools = new List<ManifestTool>();

        if (!root.TryGetProperty("tools", out var array))
        {
            errors.Add(new ManifestValidationError("$.tools", MissingProperty));
            return tools;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ManifestValidationError("$.tools", "Must be an array."));
            return tools;
        }

        if (array.GetArrayLength() == 0)
        {
            errors.Add(new ManifestValidationError("$.tools", "At least one tool is required."));
            return tools;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var path = $"$.tools[{index}]";
            var tool = ReadTool(element, path, errors);
            if (tool is not null)
            {
                if (seen.Add(tool.Id.Value))
                {
                    tools.Add(tool);
                }
                else
                {
                    errors.Add(new ManifestValidationError($"{path}.id", $"Tool '{tool.Id}' is defined more than once."));
                }
            }

            index++;
        }

        return tools;
    }

    private static ManifestTool? ReadTool(JsonElement element, string path, List<ManifestValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ManifestValidationError(path, "Each tool must be a JSON object."));
            return null;
        }

        var errorsBefore = errors.Count;
        var id = RequiredString(element, path, "id", errors);
        var displayName = OptionalString(element, path, "displayName", errors);
        var versions = RequiredStringArray(element, path, "approvedVersions", errors);
        var remediation = ReadRemediation(element, path, errors);
        var smokeChecks = ReadSmokeChecks(element, path, errors);

        if (errors.Count > errorsBefore || id is null || versions is null || remediation is null)
        {
            return null;
        }

        var kind = remediation.Method == RemediationMethod.VsCodeExtension ? ManifestComponentKind.Extension : ManifestComponentKind.Tool;
        return new ManifestTool(new ToolReference(id), displayName, versions, remediation, smokeChecks, kind: kind);
    }

    private static List<SmokeCheckDefinition> ReadSmokeChecks(JsonElement tool, string toolPath, List<ManifestValidationError> errors)
    {
        var checks = new List<SmokeCheckDefinition>();
        var path = $"{toolPath}.smokeTests";

        if (!tool.TryGetProperty("smokeTests", out var array) || array.ValueKind == JsonValueKind.Null)
        {
            return checks;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ManifestValidationError(path, "Must be an array."));
            return checks;
        }

        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var check = ReadSmokeCheck(element, $"{path}[{index}]", errors);
            if (check is not null)
            {
                checks.Add(check);
            }

            index++;
        }

        return checks;
    }

    private static SmokeCheckDefinition? ReadSmokeCheck(JsonElement element, string path, List<ManifestValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ManifestValidationError(path, "Each smoke test must be a JSON object."));
            return null;
        }

        var kind = RequiredString(element, path, "kind", errors);
        switch (kind)
        {
            case null:
                return null;
            case "installedVersion":
                return new InstalledVersionCheck();
            case "commandOnPath":
                var command = RequiredCommand(element, path, errors);
                return command is null ? null : new CommandOnPathCheck(command);
            case "fileExists":
                var file = RequiredString(element, path, "path", errors);
                if (file is null)
                {
                    return null;
                }

                if (file.Contains('"') || !(Path.IsPathFullyQualified(file) || file.StartsWith('%')))
                {
                    errors.Add(new ManifestValidationError($"{path}.path", "Must be an absolute path, optionally starting with an environment variable such as %LOCALAPPDATA%."));
                    return null;
                }

                return new FileExistsCheck(file);
            case "versionCommand":
                var executable = RequiredCommand(element, path, errors);
                var arguments = OptionalStringArray(element, path, "arguments", errors);
                var timeout = OptionalTimeout(element, path, errors);
                return executable is null || arguments is null || timeout is null
                    ? null
                    : new VersionCommandCheck(executable, arguments, timeout.Value);
            case "userEnvironmentVariable":
                var name = RequiredString(element, path, "name", errors);
                var contains = RequiredString(element, path, "contains", errors);
                return name is null || contains is null ? null : new UserEnvironmentVariableCheck(name, contains);
            default:
                // Reported as unsupported when run, rather than guessed at or rejected outright.
                return new UnrecognisedSmokeCheck(kind);
        }
    }

    private static string? RequiredCommand(JsonElement owner, string ownerPath, List<ManifestValidationError> errors)
    {
        var command = RequiredString(owner, ownerPath, "command", errors);
        if (command is not null && !SmokeCheckCommands.IsAcceptable(command))
        {
            errors.Add(new ManifestValidationError(
                $"{ownerPath}.command",
                "Must be a command name (for example 'tool' or 'tool.exe') or an absolute path, without quotes or shell characters."));
            return null;
        }

        return command;
    }

    private static string[]? OptionalStringArray(JsonElement owner, string ownerPath, string name, List<ManifestValidationError> errors)
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            errors.Add(new ManifestValidationError($"{ownerPath}.{name}", "Must be an array of strings."));
            return null;
        }

        return value.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static TimeSpan? OptionalTimeout(JsonElement owner, string ownerPath, List<ManifestValidationError> errors)
    {
        if (!owner.TryGetProperty("timeoutSeconds", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return DefaultSmokeTestTimeout;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var seconds)
            || seconds < 1
            || seconds > MaximumSmokeTestTimeoutSeconds)
        {
            errors.Add(new ManifestValidationError(
                $"{ownerPath}.timeoutSeconds",
                $"Must be a whole number of seconds from 1 to {MaximumSmokeTestTimeoutSeconds}."));
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static ManifestRemediation? ReadRemediation(JsonElement tool, string toolPath, List<ManifestValidationError> errors)
    {
        var path = $"{toolPath}.remediation";

        if (!tool.TryGetProperty("remediation", out var element))
        {
            errors.Add(new ManifestValidationError(path, MissingProperty));
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ManifestValidationError(path, "Must be an object."));
            return null;
        }

        var methodName = RequiredString(element, path, "method", errors);
        var source = OptionalString(element, path, "source", errors);

        if (methodName is null)
        {
            return null;
        }

        if (!Methods.TryGetValue(methodName, out var method))
        {
            errors.Add(new ManifestValidationError(
                $"{path}.method",
                $"Unsupported remediation method '{methodName}'. Expected one of: {string.Join(", ", Methods.Keys)}."));
            return null;
        }

        if (method != RemediationMethod.None && string.IsNullOrWhiteSpace(source))
        {
            errors.Add(new ManifestValidationError($"{path}.source", $"Required when method is '{methodName}'."));
            return null;
        }

        if (method == RemediationMethod.VsCodeExtension && !VsCodeExtensionId.IsValid(source))
        {
            errors.Add(new ManifestValidationError($"{path}.source", "Must be a VS Code extension identifier such as 'publisher.name'."));
            return null;
        }

        return new ManifestRemediation(method, source);
    }

    private static string? RequiredString(JsonElement owner, string ownerPath, string name, List<ManifestValidationError> errors)
    {
        var path = $"{ownerPath}.{name}";

        if (!owner.TryGetProperty(name, out var value))
        {
            errors.Add(new ManifestValidationError(path, MissingProperty));
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(new ManifestValidationError(path, "Must be a non-empty string."));
            return null;
        }

        return text;
    }

    private static string? OptionalString(JsonElement owner, string ownerPath, string name, List<ManifestValidationError> errors)
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add(new ManifestValidationError($"{ownerPath}.{name}", "Must be a string when present."));
            return null;
        }

        return value.GetString();
    }

    private static string[]? RequiredStringArray(JsonElement owner, string ownerPath, string name, List<ManifestValidationError> errors)
    {
        var path = $"{ownerPath}.{name}";

        if (!owner.TryGetProperty(name, out var value))
        {
            errors.Add(new ManifestValidationError(path, MissingProperty));
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ManifestValidationError(path, "Must be an array of strings."));
            return null;
        }

        if (value.GetArrayLength() == 0)
        {
            errors.Add(new ManifestValidationError(path, "At least one value is required."));
            return null;
        }

        var items = new List<string>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                errors.Add(new ManifestValidationError($"{path}[{index}]", "Must be a non-empty string."));
            }
            else
            {
                items.Add(text);
            }

            index++;
        }

        return items.Count == value.GetArrayLength() ? items.ToArray() : null;
    }

    private static ManifestLoadResult Invalid(string source, List<ManifestValidationError> errors) =>
        ManifestLoadResult.Failed(
            ManifestLoadStatus.Invalid,
            $"The manifest at {source} is invalid: {string.Join("; ", errors)}",
            errors);
}
