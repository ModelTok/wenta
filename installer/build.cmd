@echo off
rem ------------------------------------------------------------------
rem Build WentaZwcad.msi (issue #36).
rem
rem Prerequisites:
rem   1. zwcad-plugin\build.cmd has run, so zwcad-plugin\bin contains
rem      WentaZwcad.dll, Wenta.CUIX and example-generic.json.
rem   2. The WiX 7 dotnet tool:  dotnet tool install --global wix
rem
rem Usage:  build.cmd [version]        (default version below)
rem ------------------------------------------------------------------
setlocal

set VERSION=%1
if "%VERSION%"=="" set VERSION=0.1.0

set ROOT=%~dp0
set BIN=%ROOT%..\zwcad-plugin\bin
set OUT=%ROOT%bin

for %%F in (WentaZwcad.dll Wenta.CUIX example-generic.json) do (
  if not exist "%BIN%\%%F" (
    echo MISSING: %BIN%\%%F  -- run zwcad-plugin\build.cmd first
    exit /b 1
  )
)

if not exist "%OUT%" mkdir "%OUT%"

wix build ^
  -arch x64 ^
  -d ProductVersion=%VERSION% ^
  -d BinDir="%BIN%" ^
  -o "%OUT%\WentaZwcad-%VERSION%.msi" ^
  "%ROOT%Wenta.wxs"
if errorlevel 1 ( echo BUILD FAILED: msi & exit /b 1 )

echo BUILD OK: %OUT%\WentaZwcad-%VERSION%.msi
endlocal
