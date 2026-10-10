@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start Server Manager.ps1" %*
if errorlevel 1 (
    echo.
    echo The server manager could not be opened. Read the error above.
    pause
    exit /b 1
)
exit /b 0
