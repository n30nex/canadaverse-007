param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $projectRoot 'dist\windows'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler is required (included with Windows 10/11 .NET Framework).' }
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')
$compilerArgs = @('/nologo','/codepage:65001','/optimize+','/platform:anycpu','/target:winexe',('/out:' + (Join-Path $outputRoot 'CanadaverseLauncher.exe')),('/win32manifest:' + (Join-Path $projectRoot 'launcher\windows\app.manifest')),('/resource:' + (Join-Path $projectRoot 'versions.lock.json') + ',versions.lock.json'))
foreach ($reference in $references) { $compilerArgs += '/reference:' + $reference }
$compilerArgs += Join-Path $projectRoot 'launcher\windows\Core.cs'
$compilerArgs += Join-Path $projectRoot 'launcher\windows\Program.cs'
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Launcher compiler failed: $LASTEXITCODE" }
Write-Output (Join-Path $outputRoot 'CanadaverseLauncher.exe')
if ($Test) {
    $testArgs = @('/nologo','/codepage:65001','/optimize+','/target:exe',('/out:' + (Join-Path $outputRoot 'LauncherTests.exe')),('/resource:' + (Join-Path $projectRoot 'versions.lock.json') + ',versions.lock.json'))
    foreach ($reference in $references) { $testArgs += '/reference:' + $reference }
    $testArgs += Join-Path $projectRoot 'launcher\windows\Core.cs'
    $testArgs += Join-Path $projectRoot 'tests\LauncherTests.cs'
    & $compiler @testArgs
    if ($LASTEXITCODE -ne 0) { throw "Test compiler failed: $LASTEXITCODE" }
    & (Join-Path $outputRoot 'LauncherTests.exe') (Join-Path $projectRoot '.local\native-tests')
    if ($LASTEXITCODE -ne 0) { throw "Launcher tests failed: $LASTEXITCODE" }
}
