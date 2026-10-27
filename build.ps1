# Builds HashbrownAutoUtilities.dll with the .NET Framework compiler (no SDK needed)
# against the game's own Mono/.NET 3.5 assemblies. Pass -Install to copy into BepInEx\plugins.
param(
    [string]$Game = "S:\SteamLibrary\steamapps\common\Project Highrise",
    [switch]$Install
)
$ErrorActionPreference = "Stop"
$w = $PSScriptRoot
$m = Join-Path $Game "Game_Data\Managed"
$b = Join-Path $Game "BepInEx\core"
if (-not (Test-Path "$b\BepInEx.dll")) { $b = Join-Path $w "_tools\BepInEx_x86\BepInEx\core" }
New-Item -ItemType Directory -Force "$w\build" | Out-Null
$refs = @("mscorlib","System","System.Core","UnityEngine","UnityEngine.CoreModule","UnityEngine.InputModule","UnityEngine.IMGUIModule","UnityEngine.TextRenderingModule","UnityEngine.UIModule","UnityEngine.UI","Assembly-CSharp","Assembly-CSharp-firstpass") | ForEach-Object { "/r:$m\$_.dll" }
$refs += "/r:$b\BepInEx.dll", "/r:$b\0Harmony.dll"
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /noconfig /nostdlib+ /target:library /optimize+ "/out:$w\build\HashbrownAutoUtilities.dll" $refs "$w\src\*.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $w\build\HashbrownAutoUtilities.dll"
if ($Install) {
    $p = Join-Path $Game "BepInEx\plugins"
    New-Item -ItemType Directory -Force $p | Out-Null
    Copy-Item "$w\build\HashbrownAutoUtilities.dll" $p -Force
    Write-Host "Installed to $p"
}
