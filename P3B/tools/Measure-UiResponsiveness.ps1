<#
.SYNOPSIS
    Measures P3B-PR-02 and P3B-PR-03 on the real WinForms window using Windows UI Automation.

.DESCRIPTION
    Starts the P3B dashboard with TEST manifest and audit files in a temporary data folder, then
    for each trial:
      - PR-02 (acknowledgement): clicks "Fix selected issue..." and times until the window visibly
        responds (the Fix button is disabled while the operation is in progress). The time until the
        approval dialog appears is also recorded, for information.
      - PR-03 (completion shown): clicks "Do not change anything" and times until the window shows
        the outcome ("Not approved") and is ready for the next command.

    SAFE: the script only ever declines the approval, so nothing is installed or changed, and the
    data folder is temporary (not %LOCALAPPDATA%\P3B). Run this on the agreed reference machine
    for the SRS trials.

.EXAMPLE
    dotnet build P3B.slnx
    powershell -ExecutionPolicy Bypass -File tools\Measure-UiResponsiveness.ps1 -Trials 20
#>
param(
    [int] $Trials = 20,
    [string] $AppPath = (Join-Path $PSScriptRoot '..\src\P3B.Remediation.WinForms\bin\Debug\net10.0-windows\P3B.Remediation.WinForms.exe'),
    [string] $OutputCsv
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

function Find-ByName($root, [string] $name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    return $root.FindFirst($Scope::Descendants, $condition)
}

# Matches by title prefix: UI Automation drops '&' from WinForms titles (it is a mnemonic marker).
function Find-Window([int] $processId, [string] $titlePrefix) {
    $byProcess = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $isWindow = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    foreach ($window in $AE::RootElement.FindAll($Scope::Children, $byProcess)) {
        if ($window.Current.Name -like "$titlePrefix*") { return $window }
        foreach ($child in $window.FindAll($Scope::Descendants, $isWindow)) {
            if ($child.Current.Name -like "$titlePrefix*") { return $child }
        }
    }
    return $null
}

function Invoke-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Wait-Until([scriptblock] $condition, [int] $timeoutMs = 10000) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $timeoutMs) {
        $result = & $condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds 10
    }
    throw "Timed out after $timeoutMs ms."
}

if (-not (Test-Path $AppPath)) { throw "Build the solution first. Not found: $AppPath" }

$work = Join-Path ([IO.Path]::GetTempPath()) ("p3b-ui-trial-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
@'
{ "_testFixture": "TEST FIXTURE - fake values only", "manifestVersion": "p3b-provisional-0", "tools": [
  { "id": "TEST-TOOL-A", "displayName": "Test tool A", "approvedVersions": [ "TEST-VERSION-1" ],
    "remediation": { "method": "winget", "source": "TEST.Package.A" } } ] }
'@ | Set-Content (Join-Path $work 'manifest.json') -Encoding UTF8
@'
{ "issues": [ { "issueId": "TEST-ISSUE-A", "toolId": "TEST-TOOL-A", "state": "missing" } ] }
'@ | Set-Content (Join-Path $work 'audit.json') -Encoding UTF8

$arguments = @('--manifest', "`"$work\manifest.json`"", '--audit', "`"$work\audit.json`"", '--data-folder', "`"$work\data`"")
$app = Start-Process -FilePath $AppPath -ArgumentList $arguments -PassThru
$results = @()
try {
    $main = Wait-Until { Find-Window $app.Id 'Windows EDA Environment Manager' } 30000
    $fix = Wait-Until { $b = Find-ByName $main 'Fix selected issue...'; if ($b -and $b.Current.IsEnabled) { $b } } 15000

    for ($trial = 1; $trial -le $Trials; $trial++) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        Invoke-Element $fix
        # First visible acknowledgement: the window disables the Fix button and shows In Progress.
        Wait-Until { -not $fix.Current.IsEnabled } | Out-Null
        $acknowledgeMs = $watch.Elapsed.TotalMilliseconds
        # Informational: the approval dialog (includes UI Automation's slower window search).
        $dialog = Wait-Until { Find-Window $app.Id 'Approve this change?' }
        $dialogMs = $watch.Elapsed.TotalMilliseconds

        $decline = Wait-Until { Find-ByName $dialog 'Do not change anything' }
        $watch.Restart()
        Invoke-Element $decline
        Wait-Until { (Find-ByName $main 'Not approved') -and $fix.Current.IsEnabled } | Out-Null
        $completionMs = $watch.Elapsed.TotalMilliseconds

        $row = [pscustomobject]@{
            Trial = $trial
            AcknowledgeMs = [math]::Round($acknowledgeMs, 1)
            AcknowledgePass = $acknowledgeMs -lt 1000
            ApprovalDialogMs = [math]::Round($dialogMs, 1)
            CompletionShownMs = [math]::Round($completionMs, 1)
            CompletionPass = $completionMs -lt 2000
        }
        $results += $row
        $row | Format-Table -HideTableHeaders | Out-String | ForEach-Object { $_.Trim() }
    }
}
finally {
    if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2 }
    if (-not $app.HasExited) { Stop-Process -Id $app.Id }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

$ack = ($results | Where-Object AcknowledgePass).Count
$dialogs = ($results | Where-Object { $_.ApprovalDialogMs -lt 1000 }).Count
$done = ($results | Where-Object CompletionPass).Count
"P3B-PR-02 (real window): $ack of $($results.Count) trials acknowledged within 1 s (required 19 of 20) -> $(if ($ack -ge 19) {'PASS'} else {'FAIL'})"
"           (information: approval dialog shown within 1 s in $dialogs of $($results.Count) trials, including UI Automation search time)"
"P3B-PR-03 (real window): $done of $($results.Count) outcomes shown within 2 s (required 19 of 20) -> $(if ($done -ge 19) {'PASS'} else {'FAIL'})"
if ($OutputCsv) { $results | Export-Csv -NoTypeInformation -Path $OutputCsv }
