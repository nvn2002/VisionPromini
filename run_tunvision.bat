@echo off
setlocal
if exist "%~dp0TunVision\TunVision.UI.exe" (
    cd /d "%~dp0TunVision"
    start "" TunVision.UI.exe
    goto :eof
)
if exist "%~dp0TunVision\TunVision.UI.dll" (
    cd /d "%~dp0TunVision"
    start "" dotnet TunVision.UI.dll
    goto :eof
)
if exist "%~dp0TunVision\TunVision.UI\bin\Release\net8.0-windows\win-x64\TunVision.UI.exe" (
    cd /d "%~dp0TunVision\TunVision.UI\bin\Release\net8.0-windows\win-x64"
    start "" TunVision.UI.exe
    goto :eof
)
if exist "%~dp0TunVision\TunVision.UI\bin\Release\net8.0-windows\win-x64\TunVision.UI.dll" (
    cd /d "%~dp0TunVision\TunVision.UI\bin\Release\net8.0-windows\win-x64"
    start "" dotnet TunVision.UI.dll
    goto :eof
)
echo TunVision.UI binary not found!
pause
