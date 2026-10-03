@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Uninstall.ps1"
if errorlevel 1 echo Uninstallation failed. Read the error above.
pause
