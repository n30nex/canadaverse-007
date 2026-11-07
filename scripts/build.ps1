[CmdletBinding()]
param(
    [string]$Workspace = 'F:\007',
    [string]$Source = '',
    [string]$Build = '',
    [string]$Rom = '',
    [string]$CMake = 'D:\VS2022BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe',
    [int]$Jobs = 6,
    [switch]$Clean
)
$ErrorActionPreference = 'Stop'
if (!$Source) { $Source = Join-Path $Workspace 'source\Project-Midas' }
if (!$Build) { $Build = Join-Path $Workspace 'build\Project-Midas-make' }
if (!$Rom) { $Rom = Join-Path $Workspace 'Project-Midas\Data\baserom.u.z64' }
$mingw = Join-Path $Workspace 'toolchains\mingw32'
$sdl = Join-Path $Workspace 'toolchains\SDL2-2.30.11\i686-w64-mingw32'
$make = Join-Path $mingw 'bin\mingw32-make.exe'
$toolchain = Join-Path $PSScriptRoot '..\cmake\native-mingw32.cmake'
$patch = Join-Path $PSScriptRoot '..\patches\windows-gcc14.patch'
foreach ($file in @($CMake, $make, "$mingw\bin\gcc.exe", "$sdl\lib\libSDL2.dll.a", $Rom, "$Source\CMakeLists.txt")) {
    if (!(Test-Path -LiteralPath $file)) { throw "Missing build prerequisite: $file" }
}
$oldPath = $env:PATH
$oldEpoch = $env:SOURCE_DATE_EPOCH
$oldUtf8 = $env:PYTHONUTF8
try {
    $env:PATH = "$mingw\bin;" + $oldPath
    $env:SOURCE_DATE_EPOCH = '1792748901'
    $env:PYTHONUTF8 = '1'
    $head = (& git -C $Source rev-parse HEAD).Trim()
    if ($head -ne '9062f9f120b7f64d39e5db59500ed50e8e7a6686') { throw "Unexpected upstream base: $head. Review the lock file and patch before upgrading." }
    & git -C $Source apply --reverse --check $patch 2>$null
    if ($LASTEXITCODE -ne 0) {
        & git -C $Source apply --check $patch
        if ($LASTEXITCODE -ne 0) { throw 'Windows compatibility patch conflicts with this source tree.' }
        & git -C $Source apply $patch
        if ($LASTEXITCODE -ne 0) { throw 'Could not apply Windows compatibility patch.' }
    }
    & python (Join-Path $PSScriptRoot 'prepare-assets.py') --source $Source --rom $Rom
    if ($LASTEXITCODE) { throw "Asset preparation failed ($LASTEXITCODE)" }
    & $CMake -S $Source -B $Build -G 'MinGW Makefiles' "-DCMAKE_MAKE_PROGRAM=$make" "-DCMAKE_TOOLCHAIN_FILE=$toolchain" "-DMINGW_ROOT=$mingw" "-DSDL2_MINGW_ROOT=$sdl" -DCMAKE_BUILD_TYPE=Release -DGE_VIDEO=ON -DGE_ROMDATA_FALLBACK=OFF -DGE_UNLOCK_ALL=OFF '-DCMAKE_EXE_LINKER_FLAGS=-Wl,--no-insert-timestamp'
    if ($LASTEXITCODE) { throw "CMake configuration failed ($LASTEXITCODE)" }
    if ($Clean) { & $CMake --build $Build --clean-first --parallel $Jobs }
    else { & $CMake --build $Build --parallel $Jobs }
    if ($LASTEXITCODE) { throw "Compilation failed ($LASTEXITCODE)" }
    # This POSIX MinGW toolchain uses the matching 32-bit pthread runtime.
    Copy-Item -LiteralPath (Join-Path $mingw 'bin\libwinpthread-1.dll') -Destination $Build -Force
    Write-Output "Built $Build\ProjectMidas.exe"
} finally {
    $env:PATH = $oldPath
    $env:SOURCE_DATE_EPOCH = $oldEpoch
    $env:PYTHONUTF8 = $oldUtf8
}
