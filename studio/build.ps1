$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (!(Test-Path $csc)) {
    throw "找不到 .NET Framework 4.x 的 csc.exe"
}

$common = @(
    "/nologo",
    "/codepage:65001",
    "/optimize+",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll"
)

$studioSources = @(
    "FanCore.cs",
    "EditorModel.cs",
    "StudioTheme.cs",
    "DarkNumeric.cs",
    "DarkScrollBar.cs",
    "LegalDocuments.cs",
    "LegalDialog.cs",
    "MotorLibraryControl.cs",
    "TimelineControl.cs",
    "InspectorPanel.cs",
    "TransportBar.cs",
    "MidiImportDialog.cs",
    "MainForm.cs",
    "Studio.cs"
) | ForEach-Object { Join-Path $root $_ }

$studioOut = Join-Path $root "fan_studio.exe"
$coreOut = Join-Path $root "fan_core_test.exe"
$studioIcon = Join-Path $root "assets\fan_studio.ico"

& $csc @common /target:winexe "/win32icon:$studioIcon" "/out:$studioOut" @studioSources
if ($LASTEXITCODE -ne 0) { throw "fan_studio.exe 编译失败" }

& $csc @common /target:exe "/out:$coreOut" `
    (Join-Path $root "FanCore.cs") (Join-Path $root "CoreTest.cs")
if ($LASTEXITCODE -ne 0) { throw "fan_core_test.exe 编译失败" }

Write-Host "OK: fan_studio.exe / fan_core_test.exe"
