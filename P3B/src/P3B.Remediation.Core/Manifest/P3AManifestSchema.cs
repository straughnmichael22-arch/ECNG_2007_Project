using System.Text.Json;
using P3B.Remediation.Core.Integration;

namespace P3B.Remediation.Core.Manifest;

/// <summary>
/// Reads and validates P3A's manifest (<c>eda-tool-manifest.json</c>, <c>manifestVersion</c> "1.0"),
/// the manifest the group's audit actually uses.
/// </summary>
/// <remarks>
/// <para>AGREED: the format is P3A's, read the way P3A reads it (property names are not
/// case-sensitive). P3B reads only what it needs:</para>
/// <list type="bullet">
/// <item><c>tools[].type</c> identifies the tool (it is what P3A's audit reports), <c>name</c> is shown;</item>
/// <item><c>minimumApprovedVersion</c>/<c>maximumApprovedVersion</c> become the approved range;</item>
/// <item><c>remediation.downloadUrl</c> and <c>remediation.instructions</c> become manual guidance;</item>
/// <item><c>extensions[]</c> become extension groups identified by <c>type</c>.</item>
/// </list>
/// <para>P3A's manifest gives no exact version and no package identifier, so nothing read
/// from it can be installed automatically: every remediation method is
/// <see cref="RemediationMethod.LinkToInstaller"/> or <see cref="RemediationMethod.None"/>.
/// Detection data and <c>smokeTest</c> are left to P3A.</para>
/// </remarks>
public static class P3AManifestSchema
{
    public const string SupportedVersion = "1.0";

    private const string MissingProperty = "Required property is missing.";

    internal static ManifestLoadResult Validate(JsonElement root, string source)
    {
        var errors = new List<ManifestValidationError>();
        var components = new List<ManifestTool>();

        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ManifestValidationError("$", "The manifest must be a JSON object."));
            return Invalid(source, errors);
        }

        var toolCount = ReadArray(root, "tools", errors, (element, path) => ReadTool(element, path, errors), components);
        var extensionCount = ReadArray(root, "extensions", errors, (element, path) => ReadExtension(element, path, errors), components);

        if (errors.Count == 0 && toolCount + extensionCount == 0)
        {
            errors.Add(new ManifestValidationError("$", "The manifest defines no tools or extensions."));
        }

        var duplicate = components.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            errors.Add(new ManifestValidationError("$", $"More than one tool or extension has the type '{duplicate.Key}'."));
        }

        if (errors.Count > 0)
        {
            return Invalid(source, errors);
        }

        return ManifestLoadResult.Loaded(
            new LocalEdaToolManifest(SupportedVersion, components),
            $"Loaded {toolCount} tool(s) and {extensionCount} extension group(s) from {source} (P3A manifest format {SupportedVersion}).");
    }

    private static int ReadArray(
        JsonElement root,
        string name,
        List<ManifestValidationError> errors,
        Func<JsonElement, string, ManifestTool?> read,
        List<ManifestTool> into)
    {
        // Like P3A, a missing or null list is treated as empty.
        if (!TryGet(root, name, out var array) || array.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ManifestValidationError($"$.{name}", "Must be an array."));
            return 0;
        }

        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            if (read(element, $"$.{name}[{index}]") is { } component)
            {
                into.Add(component);
            }

            index++;
        }

        return index;
    }

    private static ManifestTool? ReadTool(JsonElement element, string path, List<ManifestValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ManifestValidationError(path, "Each tool must be a JSON object."));
            return null;
        }

        var errorsBefore = errors.Count;
        var name = RequiredString(element, path, "name", errors);
        var type = RequiredString(element, path, "type", errors);
        var minimum = OptionalVersion(element, path, "minimumApprovedVersion", errors);
        var maximum = OptionalVersion(element, path, "maximumApprovedVersion", errors);

        if (minimum.Parsed is not null && maximum.Parsed is not null && minimum.Parsed > maximum.Parsed)
        {
            errors.Add(new ManifestValidationError(
                $"{path}.minimumApprovedVersion",
                $"The minimum approved version ({minimum.Text}) is above the maximum ({maximum.Text})."));
        }

        var (downloadUrl, instructions) = ReadRemediation(element, path, errors);

        if (errors.Count > errorsBefore || name is null || type is null)
        {
            return null;
        }

        var remediation = downloadUrl is null
            ? new ManifestRemediation(RemediationMethod.None, null)
            : new ManifestRemediation(RemediationMethod.LinkToInstaller, downloadUrl);

        return new ManifestTool(
            new ToolReference(type),
            name,
            approvedVersions: [],
            remediation,
            approvedRange: new ApprovedVersionRange(minimum.Text, maximum.Text),
            instructions: instructions);
    }

    private static ManifestTool? ReadExtension(JsonElement element, string path, List<ManifestValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ManifestValidationError(path, "Each extension entry must be a JSON object."));
            return null;
        }

        var errorsBefore = errors.Count;
        var name = RequiredString(element, path, "name", errors);
        var type = RequiredString(element, path, "type", errors);
        var approved = OptionalStringArray(element, path, "approvedExtensions", errors);
        var extensionId = OptionalString(element, path, "extensionId", errors);

        if (errors.Count > errorsBefore || name is null || type is null || approved is null)
        {
            return null;
        }

        // P3A falls back to extensionId when approvedExtensions is empty.
        if (approved.Length == 0 && !string.IsNullOrWhiteSpace(extensionId))
        {
            approved = [extensionId];
        }

        return new ManifestTool(
            new ToolReference(type),
            name,
            approvedVersions: [],
            new ManifestRemediation(RemediationMethod.None, null),
            kind: ManifestComponentKind.Extension,
            approvedExtensions: approved);
    }

    private static (string? DownloadUrl, string[] Instructions) ReadRemediation(
        JsonElement tool,
        string toolPath,
        List<ManifestValidationError> errors)
    {
        var path = $"{toolPath}.remediation";
        if (!TryGet(tool, "remediation", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return (null, []);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ManifestValidationError(path, "Must be an object."));
            return (null, []);
        }

        var url = OptionalString(element, path, "downloadUrl", errors);
        if (!string.IsNullOrWhiteSpace(url) && !IsWebAddress(url))
        {
            // The dashboard opens this address in the browser, so it must be a web page.
            errors.Add(new ManifestValidationError($"{path}.downloadUrl", "Must be an absolute http or https address."));
            url = null;
        }

        var instructions = OptionalStringArray(element, path, "instructions", errors) ?? [];
        return (string.IsNullOrWhiteSpace(url) ? null : url, instructions);
    }

    /// <summary>True for an absolute http or https address, the only kind the dashboard will open.</summary>
    public static bool IsWebAddress(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static (string? Text, Version? Parsed) OptionalVersion(
        JsonElement owner,
        string ownerPath,
        string name,
        List<ManifestValidationError> errors)
    {
        var text = OptionalString(owner, ownerPath, name, errors);
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        // The same rule P3A's audit applies (System.Version).
        if (!Version.TryParse(text, out var parsed))
        {
            errors.Add(new ManifestValidationError($"{ownerPath}.{name}", $"'{text}' is not a version number such as 1.2.3."));
            return (null, null);
        }

        return (text, parsed);
    }

    private static bool TryGet(JsonElement owner, string name, out JsonElement value)
    {
        foreach (var property in owner.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>The manifest's <c>manifestVersion</c>, matched without regard to case as P3A does.</summary>
    internal static string? ReadVersion(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && TryGet(root, "manifestVersion", out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? RequiredString(JsonElement owner, string ownerPath, string name, List<ManifestValidationError> errors)
    {
        var path = $"{ownerPath}.{name}";
        if (!TryGet(owner, name, out var value))
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
        if (!TryGet(owner, name, out var value) || value.ValueKind == JsonValueKind.Null)
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

    private static string[]? OptionalStringArray(JsonElement owner, string ownerPath, string name, List<ManifestValidationError> errors)
    {
        if (!TryGet(owner, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array
            || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
        {
            errors.Add(new ManifestValidationError($"{ownerPath}.{name}", "Must be an array of non-empty strings."));
            return null;
        }

        return value.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static ManifestLoadResult Invalid(string source, List<ManifestValidationError> errors) =>
        ManifestLoadResult.Failed(
            ManifestLoadStatus.Invalid,
            $"The manifest at {source} is invalid: {string.Join("; ", errors)}",
            errors);
}
