# Builds the player-facing release zip: BepInEx 5 (x86) + the mod DLL + install notes.
# Usage: powershell -ExecutionPolicy Bypass -File package.ps1 -Version 1.0.0
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = "Stop"
$w = $PSScriptRoot
$bep = Join-Path $w "_tools\BepInEx_x86"
if (-not (Test-Path "$bep\winhttp.dll")) { throw "BepInEx x86 not found at $bep" }

& powershell -ExecutionPolicy Bypass -File "$w\build.ps1"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$stage = Join-Path $w "dist\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\BepInEx\plugins" | Out-Null
Copy-Item "$bep\*" $stage -Recurse -Force
Copy-Item "$w\build\HashbrownAutoUtilities.dll" "$stage\BepInEx\plugins\" -Force

@"
Auto Utilities $Version for Project Highrise - a Side Order Games mod

INSTALL
1. In Steam, right-click Project Highrise > Manage > Browse local files.
2. Copy everything in this zip into that folder, so winhttp.dll and the
   BepInEx folder sit next to Game.exe.
3. Start the game.

CONTROLS
  F7                    Run a pass now (works while paused)
  Shift+F7              Turn automatic passes on/off
  Extend to all floors  Button in a utility closet's info panel

SETTINGS
  BepInEx\config\com.thehashbrown.projecthighrise.autoutilities.cfg
  (created after the first launch)

UNINSTALL
  This mod only: delete BepInEx\plugins\HashbrownAutoUtilities.dll
  All mods:      delete winhttp.dll

Source, help and updates:
  https://github.com/SideOrderGames/ProjectHighrise-AutoUtilities

Includes BepInEx (https://github.com/BepInEx/BepInEx), LGPL-2.1.
Auto Utilities is MIT licensed, Copyright (c) 2026 TheHashbrown LLC.
"@ | Set-Content "$stage\AutoUtilities-README.txt" -Encoding ASCII

$zip = Join-Path $w "dist\AutoUtilities-v$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Write-Host "Packaged $zip"
