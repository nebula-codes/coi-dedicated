@echo off
setlocal
title Captain of Industry - Dedicated Client Installer
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-client.ps1" %*
set "INSTALL_RESULT=%ERRORLEVEL%"
echo.
if not "%INSTALL_RESULT%"=="0" echo Installation did not complete. Read the message above.
pause
exit /b %INSTALL_RESULT%
