@echo off
title NeuralFX Hub Launcher
cd /d "%~dp0"

if exist "Hub\NeuralFX.Hub.exe" (
    start "" "Hub\NeuralFX.Hub.exe"
    exit /b 0
)

if exist "NeuralFX.Hub.exe" (
    start "" "NeuralFX.Hub.exe"
    exit /b 0
)

echo [ERROR] No se encontro NeuralFX.Hub.exe en el directorio del mod.
pause
