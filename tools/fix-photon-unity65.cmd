@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0fix-photon-unity65.ps1"
if errorlevel 1 (
    echo.
    echo Photon compatibility patch failed.
    pause
    exit /b 1
)
echo.
echo Photon compatibility patch completed.
pause

