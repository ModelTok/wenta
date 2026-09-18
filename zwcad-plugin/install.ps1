# Install WentaZwcad.dll for auto-load in ZWCAD 2021 (HKLM demand loading)
#
# Requires elevation: right-click -> Run with PowerShell (as admin), or from an
# elevated prompt:  powershell -ExecutionPolicy Bypass -File install.ps1
#
# What it does:
#   1. copies WentaZwcad.dll (wenta C# core compiled in), Wenta.CUIX (ribbon)
#      and example-generic.json (open zeta-catalog) -> C:\ProgramData\WentaZwcad\
#   2. creates HKLM\SOFTWARE\ZWSOFT\ZWCAD\2021\en-US\Applications\WentaZwcad
#      with LOADCTRLS=14 (startup + on-command + manual), MANAGED=1
#
# The ribbon CUIX is loaded with MENULOAD (once) or via the ribbon install
# step in full_test.scr; ZWCAD then remembers it per profile.

$ErrorActionPreference = 'Stop'

$bin = Join-Path $PSScriptRoot 'bin'
$installDir = 'C:\ProgramData\WentaZwcad'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'must run elevated (HKLM write required)'
}

foreach ($f in 'WentaZwcad.dll', 'Wenta.CUIX', 'example-generic.json') {
    $src = Join-Path $bin $f
    if (-not (Test-Path $src)) { throw "build output missing: $src (run build.cmd)" }
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
foreach ($f in 'WentaZwcad.dll', 'Wenta.CUIX', 'example-generic.json') {
    Copy-Item (Join-Path $bin $f) (Join-Path $installDir $f) -Force
}
Write-Output ("installed: " + (Get-ChildItem $installDir | ForEach-Object { $_.Name }) -join ', ')

$appKey = 'HKLM:\SOFTWARE\ZWSOFT\ZWCAD\2021\en-US\Applications\WentaZwcad'
New-Item -Path $appKey -Force | Out-Null
Set-ItemProperty $appKey -Name 'DESCRIPTION' -Value 'Wenta duct plugin (ductwork sizing)'
Set-ItemProperty $appKey -Name 'LOADCTRLS'   -Value 14   # 2=startup 4=on-command 8=manual
Set-ItemProperty $appKey -Name 'LOADER'      -Value (Join-Path $installDir 'WentaZwcad.dll')
Set-ItemProperty $appKey -Name 'MANAGED'     -Value 1    # .NET assembly

Write-Output 'registry entry:'
Get-ItemProperty $appKey | Format-List DESCRIPTION, LOADCTRLS, LOADER, MANAGED
Write-Output 'ribbon: run _.MENULOAD C:\ProgramData\WentaZwcad\Wenta.CUIX once inside ZWCAD'
