@echo off
chcp 65001 >nul
title Transfer Window Planner Launcher
color 0B

echo ================================
echo   Transfer Window Planner
echo   Quick Launcher
echo ================================
echo.

REM Запуск через PowerShell скрипт
powershell.exe -ExecutionPolicy Bypass -File "%~dp0Launch.ps1"

pause
