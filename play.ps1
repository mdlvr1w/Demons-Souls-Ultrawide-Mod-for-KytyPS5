# Starts Demon's Souls at 3440x1440 with the ultrawide patch.
# Run install.ps1 first. Does not pass -AspectFit (that is the black-bar mode).
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Patch = Join-Path $Root 'ultrawide-3440x1440.json'
$PathsFile = Join-Path $Root 'local-paths.txt'
if (!(Test-Path -LiteralPath $Patch)) { throw "missing $Patch" }
if (!(Test-Path -LiteralPath $PathsFile)) {
	& (Join-Path $Root 'install.ps1')
	if (!(Test-Path -LiteralPath $PathsFile)) { exit 1 }
}

$kyty = ''
$game = ''
foreach ($line in Get-Content -LiteralPath $PathsFile) {
	if ($line -like 'kyty=*') { $kyty = $line.Substring(5).Trim() }
	if ($line -like 'game=*') { $game = $line.Substring(5).Trim() }
}
$launcher = Join-Path $kyty 'run-windows.ps1'
if (!(Test-Path -LiteralPath $launcher) -or !(Test-Path -LiteralPath (Join-Path $game 'eboot.bin'))) {
	Write-Host 'Saved folders are missing. Running setup again.'
	& (Join-Path $Root 'install.ps1')
	exit 0
}

$running = Get-Process -Name 'kyty_emulator' -ErrorAction SilentlyContinue
if ($running) {
	Write-Host 'kyty_emulator is already running. Close it, then start again.'
	exit 1
}

& $launcher -Game $game -Width 3440 -Height 1440 -Fullscreen -Patch $Patch -Follow
exit $LASTEXITCODE
