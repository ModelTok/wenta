@echo off
rem ------------------------------------------------------------------
rem Build Wenta.Core (pure C# port of the wenta library) + the parity
rem test runner, with the same bare-csc toolchain used by the plugin.
rem Requires: VS2022 Build Tools (Roslyn csc) + .NET Framework 4.x.
rem ------------------------------------------------------------------
setlocal

set CSC="C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
rem Any VS 2022 edition (CI runners ship Enterprise, not BuildTools).
rem (no if-block here: the ")" in "Program Files (x86)" would close it.)
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist %CSC% goto :csc_found
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\Roslyn\csc.exe`) do set CSC="%%i"
:csc_found
if not exist %CSC% ( echo csc.exe not found - install VS 2022 Build Tools & exit /b 1 )
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set ROOT=%~dp0
set OUT=%ROOT%bin

if not exist "%OUT%" mkdir "%OUT%"

rem ---- Wenta.Core.dll ----
%CSC% ^
  -nologo -target:library -platform:anycpu -optimize+ -deterministic ^
  -out:"%OUT%\Wenta.Core.dll" ^
  -r:"%FW%\System.dll" ^
  -r:"%FW%\System.Core.dll" ^
  -r:"%FW%\System.Web.Extensions.dll" ^
  "%ROOT%Wenta.Core\Units.cs" ^
  "%ROOT%Wenta.Core\Fluid.cs" ^
  "%ROOT%Wenta.Core\Geometry.cs" ^
  "%ROOT%Wenta.Core\Physics.cs" ^
  "%ROOT%Wenta.Core\StandardSizes.cs" ^
  "%ROOT%Wenta.Core\FittingsLibrary.cs" ^
  "%ROOT%Wenta.Core\Elbow.cs" ^
  "%ROOT%Wenta.Core\Components.cs" ^
  "%ROOT%Wenta.Core\Network.cs" ^
  "%ROOT%Wenta.Core\Solver.cs" ^
  "%ROOT%Wenta.Core\Sizing.cs" ^
  "%ROOT%Wenta.Core\Catalog.cs" ^
  "%ROOT%Wenta.Core\Bom.cs" ^
  "%ROOT%Wenta.Core\Balancing.cs" ^
  "%ROOT%Wenta.Core\Room.cs" ^
  "%ROOT%Wenta.Core\ReCorrections.cs" ^
  "%ROOT%Wenta.Core\Standards.cs" ^
  "%ROOT%Wenta.Core\Settings.cs" ^
  "%ROOT%Wenta.Core\Results.cs" ^
  "%ROOT%Wenta.Core\Analysis.cs" ^
  "%ROOT%Wenta.Core\Marking.cs" ^
  "%ROOT%Wenta.Core\Fabrication.cs" ^
  "%ROOT%Wenta.Core\Development.cs" ^
  "%ROOT%Wenta.Core\Clash.cs" ^
  "%ROOT%Wenta.Core\Topology.cs" ^
  "%ROOT%Wenta.Core\Fan.cs" ^
  "%ROOT%Wenta.Core\Insulation.cs" ^
  "%ROOT%Wenta.Core\Sound.cs" ^
  "%ROOT%Wenta.Core\Electrical.cs" ^
  "%ROOT%Wenta.Core\NetworkJson.cs" ^
  "%ROOT%Wenta.Core\KnrMap.cs" ^
  "%ROOT%Wenta.Core\BomExport.cs"
if errorlevel 1 ( echo BUILD FAILED: Wenta.Core & exit /b 1 )

rem ---- Wenta.Core.Tests.exe ----
%CSC% ^
  -nologo -target:exe -platform:anycpu -optimize+ -deterministic ^
  -out:"%OUT%\Wenta.Core.Tests.exe" ^
  -r:"%FW%\System.dll" ^
  -r:"%FW%\System.Core.dll" ^
  -r:"%OUT%\Wenta.Core.dll" ^
  "%ROOT%Wenta.Core.Tests\Program.cs"
if errorlevel 1 ( echo BUILD FAILED: tests & exit /b 1 )

xcopy /y /e /i "%ROOT%Wenta.Core.Tests\vectors" "%OUT%\vectors" >nul
xcopy /y /i "%ROOT%catalogs\*.json" "%OUT%\catalogs" >nul

echo BUILD OK
echo RUN TESTS:   %OUT%\Wenta.Core.Tests.exe
endlocal
