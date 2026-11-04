param(
    [Parameter(Mandatory)][string]$TestDll,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$profile = Join-Path $OutputDirectory 'test-profile'
New-Item -ItemType Directory -Path $profile -Force | Out-Null
function Run-TestProcess([string]$Name, [string[]]$Arguments, [hashtable]$Environment, [bool]$ExpectSuccess = $true) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.ArgumentList.Add($TestDll)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.WorkingDirectory = $profile
    $start.Environment['APPDATA'] = $profile; $start.Environment['LOCALAPPDATA'] = $profile
    $start.Environment.Remove('VRCFT_OSC_POLL_MS') | Out-Null
    $start.Environment.Remove('VRCFT_OSC_OUTPUT_MS') | Out-Null
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = $Environment[$key] }
    $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw "Patch test timeout: $Name" }
    $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    $text | Set-Content (Join-Path $OutputDirectory "$Name.log") -Encoding utf8
    $success = $process.ExitCode -eq 0; $process.Dispose()
    if ($success -ne $ExpectSuccess) { Write-Host $text; throw "Unexpected patch test result: $Name" }
    Write-Host "PASS $Name"
}
Run-TestProcess 'patch-tests' @((Join-Path $OutputDirectory 'tests.json')) @{}
Run-TestProcess 'wait-defaults' @('--budgets', '4', '4') @{}
Run-TestProcess 'wait-environment' @('--budgets', '6', '7') @{VRCFT_OSC_POLL_MS='6'; VRCFT_OSC_OUTPUT_MS='7'}
Run-TestProcess 'wait-cli-precedence' @('--budgets', '3', '5', '--osc-poll-ms=3', '--osc-output-ms', '5') @{VRCFT_OSC_POLL_MS='6'; VRCFT_OSC_OUTPUT_MS='7'}
Run-TestProcess 'wait-reject-zero' @('--budgets', '0', '4', '--osc-poll-ms=0') @{} $false
Run-TestProcess 'wait-reject-malformed' @('--budgets', '4', '4', '--osc-poll-ms=invalid') @{} $false
