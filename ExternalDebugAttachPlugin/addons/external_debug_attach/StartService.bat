@echo off
title Debug Attach Service
echo ========================================
echo   Debug Attach Service for Godot C#
echo ========================================
echo.

:: Kill any existing instance first
taskkill /IM DebugAttachService.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

echo Starting service on port 47632...
echo.
"%~dp0bin\DebugAttachService.exe" --port 47632

echo.
echo Service exited. Press any key to close...
pause >nul
