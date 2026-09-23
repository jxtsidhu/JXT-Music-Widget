@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
set EXE=JXT MUSIC WIDGET.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set CSC=%FW%\csc.exe
if not exist "%CSC%" (
  echo Could not find the .NET Framework compiler. Enable ".NET Framework 4.x" in Windows Features.
  pause & exit /b 1
)

echo Closing any running copy...
taskkill /f /im "%EXE%" >nul 2>&1
timeout /t 1 >nul

set ICO=
if exist "icon.ico" set ICO=/win32icon:"icon.ico"
set WM=%WINDIR%\System32\WinMetadata
set BASE=/nologo /nowarn:4014 /target:winexe /optimize+ /out:"%EXE%" %ICO% /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%FW%\System.Runtime.WindowsRuntime.dll"
if exist "%FW%\System.Runtime.dll" set BASE=%BASE% /r:"%FW%\System.Runtime.dll"

echo Building JXT MUSIC WIDGET...
"%CSC%" %BASE% /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Storage.winmd" JXTMusicWidget.cs
if not errorlevel 1 goto ok

rem Fallback: use the Windows SDK metadata if it is installed
set SDK=
for /d %%D in ("%ProgramFiles(x86)%\Windows Kits\10\UnionMetadata\*") do if exist "%%D\Windows.winmd" set SDK=%%D\Windows.winmd
if defined SDK (
  echo Retrying with Windows SDK metadata...
  "%CSC%" %BASE% /r:"!SDK!" JXTMusicWidget.cs
  if not errorlevel 1 goto ok
)
echo.
echo ===== BUILD FAILED - please copy the error lines above and send them =====
pause & exit /b 1

:ok
echo.
echo Build OK. Opening JXT MUSIC WIDGET - it also lives in the tray (click ^ near the clock if hidden).
start "" "%EXE%"
timeout /t 3 >nul
