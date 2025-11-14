# Self-elevating debug deploy script for the PowerToys Run plugin.
# No external dependencies (gsudo not required).

param(
 [ValidateSet('x64','ARM64')]
 [string]$Arch = 'x64',
 [switch]$Release,
 [string]$PowerToysPath = 'C:\Program Files\PowerToys'
)

function Resolve-PowerToysExe {
 param([string]$BasePath)
 $candidates = @()
 if ($BasePath) {
 if ($BasePath -like '*.exe' -and (Test-Path $BasePath)) { return $BasePath }
 $candidates += (Join-Path $BasePath 'PowerToys.exe')
 }
 $candidates += @(
 (Join-Path $env:ProgramFiles 'PowerToys\PowerToys.exe'),
 (Join-Path $env:ProgramFiles 'PowerToys (Preview)\PowerToys.exe'),
 (Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\PowerToys.exe')
 )
 # Registry lookup
 $regRoots = @(
 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
 )
 foreach ($root in $regRoots) {
 try {
 $items = Get-ChildItem $root -ErrorAction SilentlyContinue
 foreach ($i in $items) {
 try {
 $props = Get-ItemProperty $i.PSPath -ErrorAction SilentlyContinue
 if ($props.DisplayName -and ($props.DisplayName -like '*PowerToys*')) {
 if ($props.InstallLocation) {
 $candidates += (Join-Path $props.InstallLocation 'PowerToys.exe')
 }
 if ($props.DisplayIcon -and ($props.DisplayIcon -like '*.exe')) {
 $candidates += $props.DisplayIcon
 }
 }
 }
 catch {}
 }
 }
 catch {}
 }
 foreach ($c in $candidates | Where-Object { $_ } | Select-Object -Unique) { if (Test-Path $c) { return $c } }
 return $null
}

# Re-run as administrator if not elevated
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
 $argsList = @(
 '-NoProfile',
 '-ExecutionPolicy','Bypass',
 '-File', ('"' + $PSCommandPath + '"'),
 '-Arch', $Arch
 )
 if ($Release) { $argsList += '-Release' }
 if ($PowerToysPath) { $argsList += @('-PowerToysPath', ('"' + $PowerToysPath + '"')) }

 Start-Process -FilePath (Get-Process -Id $PID | Select-Object -ExpandProperty Path) -ArgumentList $argsList -Verb RunAs
 exit
}

Push-Location
Set-Location $PSScriptRoot

try {
 # Stop PowerToys (ignore errors if not running)
 Get-Process PowerToys* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

 $projectName = 'PwrMassCode'
 $safeProjectName = 'PwrMassCode'
 $config = if ($Release) { 'Release' } else { 'Debug' }

 $buildDir = Join-Path -Path (Join-Path -Path (Join-Path -Path '.' -ChildPath 'bin') -ChildPath $Arch) -ChildPath (Join-Path -Path $config -ChildPath 'net9.0-windows')
 if (-not (Test-Path $buildDir)) {
 Write-Host "Build output not found at $buildDir. Trying alternate arch..." -ForegroundColor Yellow
 $altArch = if ($Arch -eq 'x64') { 'ARM64' } else { 'x64' }
 $buildDir = Join-Path -Path (Join-Path -Path (Join-Path -Path '.' -ChildPath 'bin') -ChildPath $altArch) -ChildPath (Join-Path -Path $config -ChildPath 'net9.0-windows')
 }

 if (-not (Test-Path $buildDir)) {
 throw "Build output not found. Build the project first (Configuration=$config, Arch=$Arch)."
 }

 $dest = Join-Path $env:LOCALAPPDATA "Microsoft\PowerToys\PowerToys Run\Plugins\$projectName"
 $files = @(
 "Community.PowerToys.Run.Plugin.$safeProjectName.deps.json",
 "Community.PowerToys.Run.Plugin.$safeProjectName.dll",
 'plugin.json',
 'Images'
 )

 Set-Location $buildDir
 New-Item -ItemType Directory -Path $dest -Force -ErrorAction SilentlyContinue | Out-Null

 foreach ($f in $files) {
 Copy-Item $f $dest -Force -Recurse -ErrorAction Stop
 }

 # Find and relaunch PowerToys
 $exe = Resolve-PowerToysExe -BasePath $PowerToysPath
 if (-not $exe) {
 Write-Warning "Could not locate PowerToys.exe automatically. Start PowerToys manually or re-run with -PowerToysPath 'C:\\Path\\To\\PowerToys'"
 }
 else {
 & $exe
 }

 Write-Host "Deployed plugin to: $dest" -ForegroundColor Green
}
finally {
 Pop-Location
}
