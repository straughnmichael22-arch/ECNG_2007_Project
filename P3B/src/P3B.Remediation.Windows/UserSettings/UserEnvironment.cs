using System.Runtime.InteropServices;
using Microsoft.Win32;
using P3B.Remediation.Core.Configuration;

namespace P3B.Remediation.Windows.UserSettings;

/// <summary>
/// The current user's environment variables (HKEY_CURRENT_USER\Environment only; never the
/// machine environment). Values are read without expanding <c>%VAR%</c> references and written
/// back with their original registry type, so entries such as <c>%USERPROFILE%\bin</c> in the
/// user PATH are preserved. Other programs are notified of the change.
/// </summary>
/// <remarks>Not exercised by unit tests: it changes the real user environment.</remarks>
public sealed partial class UserEnvironment : IUserEnvironment
{
    private const string EnvironmentKeyPath = "Environment";
    private const nint HwndBroadcast = 0xFFFF;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    public string? GetUserVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKeyPath, writable: false);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void SetUserVariable(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using (var key = Registry.CurrentUser.CreateSubKey(EnvironmentKeyPath, writable: true))
        {
            if (value is null)
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(name, value, KindFor(key, name, value));
            }
        }

        SendMessageTimeout(HwndBroadcast, WmSettingChange, 0, "Environment", SmtoAbortIfHung, 5000, out _);
    }

    private static RegistryValueKind KindFor(RegistryKey key, string name, string value)
    {
        if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is null)
        {
            return value.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String;
        }

        var kind = key.GetValueKind(name);
        return kind is RegistryValueKind.String or RegistryValueKind.ExpandString
            ? kind
            : throw new InvalidOperationException($"The user variable '{name}' is not a text value, so P3B will not change it.");
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(
        nint hWnd,
        uint msg,
        nuint wParam,
        string lParam,
        uint flags,
        uint timeout,
        out nuint result);
}
