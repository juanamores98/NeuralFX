@echo off
title NeuralFX Hub Launcher
cd /d "%~dp0"

set "MOD=%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\NeuralFX\App\NeuralFX.Hub.exe"
if exist "%MOD%" (
    start "" "%MOD%"
    exit /b 0
)

if exist "App\NeuralFX.Hub.exe" (
    start "" "%~dp0App\NeuralFX.Hub.exe"
    exit /b 0
)

rem Ubicacion anterior, para una instalacion que aun no se haya actualizado.
if exist "%LOCALAPPDATA%\NeuralFX\Hub\NeuralFX.Hub.exe" (
    start "" "%LOCALAPPDATA%\NeuralFX\Hub\NeuralFX.Hub.exe"
    exit /b 0
)

echo [ERROR] No se encontro NeuralFX.Hub.exe. Ejecuta Install-NeuralFX.ps1 desde el paquete extraido.
pause
