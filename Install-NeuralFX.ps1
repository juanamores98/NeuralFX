$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Hub/NeuralFX.PackageInstall.exe'
if (!(Test-Path -LiteralPath $installer)) { throw 'Ejecuta este script desde el paquete ZIP extraído, no desde la carpeta Mods.' }
$target = Join-Path $env:LOCALAPPDATA 'Colossal Order/Cities_Skylines/Addons/Mods/NeuralFX'
& $installer $PSScriptRoot $target
if ($LASTEXITCODE -ne 0) { throw 'No se pudo instalar el paquete. Revisa el mensaje anterior.' }
Write-Output 'Paquete instalado. Abre Iniciar-NeuralFX-Hub.bat para configurar el pipeline.'
