@echo off
setlocal
title Captain of Industry - Dedicated Client Uninstaller
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-client.ps1" -Uninstall %*
set "INSTALL_RESULT=%ERRORLEVEL%"
echo.
if not "%INSTALL_RESULT%"=="0" echo Removal did not complete. Read the message above.
pause
exit /b %INSTALL_RESULT%
