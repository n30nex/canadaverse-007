[CmdletBinding()]
param(
    [string]$Engine = 'F:\007\Project-Midas',
    [string]$Output = 'F:\007\local\verification\release-title',
    [string]$Rom = 'F:\007\Project-Midas\Data\baserom.u.z64',
    [ValidateSet('title','mission')][string]$Mode = 'title',
    [int]$Frames = 1800
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Output | Out-Null
@{Mode=$Mode;Status='running';StartedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'result.json')
foreach ($name in @('ProjectMidas.exe','SDL2.dll','gamecontrollerdb.txt','gamecontrollerdefault.txt')) {
    Copy-Item -LiteralPath (Join-Path $Engine $name) -Destination $Output -Force
}
if (Test-Path -LiteralPath (Join-Path $Engine 'libwinpthread-1.dll')) {
    Copy-Item -LiteralPath (Join-Path $Engine 'libwinpthread-1.dll') -Destination $Output -Force
}
# No saves or ROM copies in the test directory. State writers are disabled below.
Copy-Item -LiteralPath 'F:\007\Project-Midas\midas.ini' -Destination (Join-Path $Output 'midas.ini') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $Output 'Data') | Out-Null
if (Test-Path -LiteralPath (Join-Path $Engine 'Data\shaders_base.txt')) {
    Copy-Item -LiteralPath (Join-Path $Engine 'Data\shaders_base.txt') -Destination (Join-Path $Output 'Data') -Force
}
$vars = @{
    GE_ROM=$Rom; GE_NOINISAVE='1'; GE_WINDOW='1920x1080';
    GE_FRAMES="$Frames"; GE_SHOT='240'; GE_NOMSGBOX='1';
    GE_INPUTREC=(Join-Path $Output 'input-recording.txt')
}
if ($Mode -eq 'title') {
    $vars.GE_AUTOSTART='1'; $vars.GE_AUTOSTART_UNTIL='1100'
} else {
    $vars.GE_STAGE='dam'; $vars.GE_DIFF='0'; $vars.GE_SKIPINTRO='1'
    $vars.GE_DRIVE='1'; $vars.GE_AUTOFIRE='1000'
}
$saved = @{}
try {
    foreach ($key in $vars.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key,'Process'); [Environment]::SetEnvironmentVariable($key,$vars[$key],'Process') }
    $process = Start-Process -FilePath (Join-Path $Output 'ProjectMidas.exe') -WorkingDirectory $Output -PassThru -RedirectStandardOutput (Join-Path $Output 'stdout.log') -RedirectStandardError (Join-Path $Output 'stderr.log')
    $process.Id | Set-Content -LiteralPath (Join-Path $Output 'pid.txt')
    if (!$process.WaitForExit(55000)) { throw "Test did not finish in 55 seconds. PID $($process.Id); inspect logs." }
    @{Mode=$Mode;Frames=$Frames;ExitCode=$process.ExitCode;FinishedUtc=[DateTime]::UtcNow.ToString('o');Engine=$Engine;Environment=$vars} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'result.json')
    if ($process.ExitCode -ne 0) { throw "Game exited with code $($process.ExitCode)" }
    Write-Output "Test finished: $Output"
} finally {
    foreach ($key in $vars.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key],'Process') }
}
