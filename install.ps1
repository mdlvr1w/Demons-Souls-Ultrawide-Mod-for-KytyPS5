# First-time setup for the Demon's Souls 3440x1440 ultrawide patch.
# Remembers the emulator folder and the game folder, copies nothing into the game.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Patch = Join-Path $Root 'ultrawide-3440x1440.json'
$PathsFile = Join-Path $Root 'local-paths.txt'
if (!(Test-Path -LiteralPath $Patch)) { throw "missing $Patch" }

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()

function Choose-Folder([string]$description, [string]$initial) {
	$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
	$dialog.Description = $description
	$dialog.UseDescriptionForTitle = $true
	if ($initial -and (Test-Path -LiteralPath $initial)) {
		$dialog.SelectedPath = $initial
	}
	if ($dialog.ShowDialog() -ne 'OK') { return $null }
	return $dialog.SelectedPath
}

function Test-Kyty([string]$folder) {
	return (Test-Path -LiteralPath (Join-Path $folder 'kyty_emulator.exe')) -and
		(Test-Path -LiteralPath (Join-Path $folder 'run-windows.ps1'))
}

function Test-Game([string]$folder) {
	return Test-Path -LiteralPath (Join-Path $folder 'eboot.bin')
}

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($screen.Width -ne 3440 -or $screen.Height -ne 1440) {
	$answer = [System.Windows.Forms.MessageBox]::Show(
		"This patch is built for a 3440x1440 screen. This monitor is $($screen.Width)x$($screen.Height).`r`n`r`nContinue anyway?",
		"Demon's Souls ultrawide",
		[System.Windows.Forms.MessageBoxButtons]::YesNo,
		[System.Windows.Forms.MessageBoxIcon]::Warning)
	if ($answer -ne 'Yes') { exit 1 }
}

$kytyGuess = ''
if (Test-Path -LiteralPath $PathsFile) {
	foreach ($line in Get-Content -LiteralPath $PathsFile) {
		if ($line -like 'kyty=*') { $kytyGuess = $line.Substring(5) }
	}
}
$kyty = Choose-Folder 'Choose the KytyPS5 folder (the one with kyty_emulator.exe)' $kytyGuess
if (!$kyty) { exit 1 }
if (!(Test-Kyty $kyty)) {
	[System.Windows.Forms.MessageBox]::Show(
		"That folder needs kyty_emulator.exe and run-windows.ps1.`r`n`r`nDownload a Windows build of KytyPS5 from its Releases page, unpack it, and choose that folder.",
		"Demon's Souls ultrawide")
	exit 1
}

$gameGuess = ''
$remembered = Join-Path $kyty 'game-path.txt'
if (Test-Path -LiteralPath $remembered) {
	$gameGuess = "$(Get-Content -LiteralPath $remembered -Raw)".Trim()
}
$game = Choose-Folder 'Choose the game folder (the one with eboot.bin and sce_sys)' $gameGuess
if (!$game) { exit 1 }
if (!(Test-Game $game)) {
	[System.Windows.Forms.MessageBox]::Show("No eboot.bin in that folder.", "Demon's Souls ultrawide")
	exit 1
}

$paramPath = Join-Path $game 'sce_sys\param.json'
if (Test-Path -LiteralPath $paramPath) {
	$param = Get-Content -LiteralPath $paramPath -Raw -Encoding UTF8 | ConvertFrom-Json
	$id = "$($param.titleId) $($param.contentVersion)"
	if ($id -ne 'PPSA01341 01.007.000') {
		$answer = [System.Windows.Forms.MessageBox]::Show(
			"This patch matches Demon's Souls PPSA01341 version 01.007.000.`r`nThis folder is $id.`r`n`r`nThe offsets will not line up on another version. Continue anyway?",
			"Demon's Souls ultrawide",
			[System.Windows.Forms.MessageBoxButtons]::YesNo,
			[System.Windows.Forms.MessageBoxIcon]::Warning)
		if ($answer -ne 'Yes') { exit 1 }
	}
}

$patches = Join-Path $kyty 'patches'
New-Item -ItemType Directory -Force -Path $patches | Out-Null
Copy-Item -LiteralPath $Patch -Destination (Join-Path $patches 'ultrawide-3440x1440.json') -Force

@(
	"kyty=$kyty"
	"game=$game"
) | Set-Content -LiteralPath $PathsFile -Encoding UTF8

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop "Demon's Souls Ultrawide.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = 'powershell.exe'
$shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $Root 'play.ps1')`""
$shortcut.WorkingDirectory = $kyty
$shortcut.Description = "Demon's Souls, 3440x1440 gameplay, 16:9 HUD"
$shortcut.Save()

[System.Windows.Forms.MessageBox]::Show(
	"Installed.`r`n`r`nA shortcut named Demon's Souls Ultrawide is on the desktop. It starts the game with this patch.`r`n`r`nThe game files were not changed.",
	"Demon's Souls ultrawide")
