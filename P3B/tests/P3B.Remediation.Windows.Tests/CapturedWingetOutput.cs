namespace P3B.Remediation.Windows.Tests;

/// <summary>
/// Real output captured on the development machine on 2026-10-30 from WinGet v1.29.380 using
/// read-only <c>winget list --id ... --exact --source winget --disable-interactivity</c> commands.
/// Used only to test parsing; the package shown is not an approved course tool or version.
/// </summary>
internal static class CapturedWingetOutput
{
    public const string InstalledPackageId = "Microsoft.VisualStudioCode";
    public const string InstalledVersion = "1.144.0";

    public const string ListInstalled =
        "Name                                Id                         Version\r\n"
        + "----------------------------------------------------------------------\r\n"
        + "Microsoft Visual Studio Code (User) Microsoft.VisualStudioCode 1.144.0\r\n";

    public const string ListAbsent = "No installed package found matching input criteria.\r\n";

    public const string SourceAgreementsPrompt =
        "The `msstore` source requires that you view the following agreements before using.\r\n"
        + "Terms of Transaction: https://aka.ms/microsoft-store-terms-of-transaction\r\n"
        + "One or more of the source agreements were not agreed to. Operation cancelled. Please accept the source agreements or remove the corresponding sources.\r\n";
}
