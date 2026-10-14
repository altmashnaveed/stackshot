# Real Cine recordings through ShotStack + Recorder over a known pattern window (bottom left, a few seconds each),
# then every frame of the result is checked. Also builds the capture-order probes.
#   .\run-e2e.ps1                 the standard set (Cine with and without sound, overload, cancel, the three lossy qualities)
#   .\run-e2e.ps1 -Only cine      scenarios whose name starts with this
#   .\run-e2e.ps1 -Probes         only the order probes (ScreenSource newest/pick, raw DXGI, GDI)
# Each scenario writes %TEMP%\stackshot-cine-e2e\out-<name>\e2e.txt (and report.json for Cine); exit code = failures.
param([string]$Only = '', [switch]$Probes)
$ErrorActionPreference = 'Continue'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out = Join-Path $env:TEMP 'stackshot-cine-e2e'
New-Item -ItemType Directory -Force $out | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path (Split-Path $csc) 'WPF'
$src = @(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
$common = @('/nologo', '/codepage:65001', '/optimize+', '/platform:anycpu', "/win32manifest:$(Join-Path $root 'src\app.manifest')",
            '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll',
            '/r:System.Web.Extensions.dll', '/r:System.Security.dll', '/r:System.Xaml.dll', '/r:System.Management.dll',
            "/resource:$(Join-Path $root 'assets\stackshot.ico'),stackshot.ico", "/resource:$(Join-Path $root 'assets\logo-256.png'),logo.png")
foreach ($dll in 'PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll', 'WindowsFormsIntegration.dll') { $common += '/r:' + (Join-Path $wpf $dll) }
foreach ($dll in 'UIAutomationProvider.dll', 'UIAutomationTypes.dll') { $common += '/r:' + (Join-Path $wpf $dll) }
$pattern = Join-Path $PSScriptRoot 'Pattern.cs'

function Build($exe, $main, $target, $files) {
    $o = & $csc @common "/target:$target" "/main:$main" "/out:$(Join-Path $out $exe)" ($src + $files) 2>&1 | Where-Object { "$_" -match ' error ' }
    if ($LASTEXITCODE -ne 0) { $o | ForEach-Object { Write-Host $_ }; throw "No compila $exe" }
}

if ($Probes) {
    $probeFile = Join-Path $PSScriptRoot 'Probes.cs'
    Build 'OrderProbe.exe' 'Stackshot.E2E.OrderProbe' 'exe' @($pattern, $probeFile)
    Build 'RawDxgiProbe.exe' 'Stackshot.E2E.RawDxgiProbe' 'exe' @($pattern, $probeFile)
    Build 'GdiProbe.exe' 'Stackshot.E2E.GdiProbe' 'exe' @($pattern, $probeFile)
    $fails = 0
    foreach ($run in @(@('OrderProbe.exe', 'newest'), @('OrderProbe.exe', 'pick'), @('RawDxgiProbe.exe', ''), @('GdiProbe.exe', ''))) {
        & (Join-Path $out $run[0]) $run[1]
        if ($LASTEXITCODE -ne 0) { $fails++ }
    }
    exit $fails
}

Build 'E2E.exe' 'Stackshot.E2E.Program2' 'winexe' @($pattern, (Join-Path $PSScriptRoot 'E2E.cs'))
# name width height seconds [sys] [mic] [suspend] [cancel] [q=<quality>] [countdown=<s>]
$scenarios = @(
    'cine-silent 640 360 10',
    'cine-sys 640 360 10 sys',
    'cine-720p-long 1280 720 60 sys',
    'cine-overload 640 360 60 sys suspend',
    'cine-cancel 640 360 5 sys cancel',
    'cine-countdown 640 360 10 sys countdown=3',
    'cine-fullscreen 1920 1080 30 sys full',
    'standard 640 360 6 sys q=0',
    'high 640 360 6 sys q=2',
    'maximum 640 360 6 sys q=1'
)
# Recordings, work files and reports go to a private data folder, never the user's %LOCALAPPDATA%\Stackshot.
$env:STACKSHOT_TEST_DATA = Join-Path $out 'data'
New-Item -ItemType Directory -Force $env:STACKSHOT_TEST_DATA | Out-Null
$fails = 0
foreach ($s in $scenarios) {
    $name = $s.Split(' ')[0]
    if ($Only -and -not $name.StartsWith($Only)) { continue }
    $p = Start-Process -FilePath (Join-Path $out 'E2E.exe') -ArgumentList $s -PassThru -Wait
    Write-Host ("{0,-16} exit {1}" -f $name, $p.ExitCode)
    Get-Content (Join-Path $out "out-$name\e2e.txt") | Select-String 'fidelity|sequence|backwards at|historyMisses|stopReason|report.passed|left|FAIL|PASS' | ForEach-Object { Write-Host "  $_" }
    if ($p.ExitCode -ne 0) { $fails++ }
}
exit $fails
