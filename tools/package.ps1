param([switch]$Deploy, [switch]$SkipNativeBuild, [string]$ManagedDLLPath, [switch]$SkipPipelineRefresh)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    if (!$SkipNativeBuild) { & ./Native/build.ps1 }
    if (!(Test-Path -LiteralPath 'Native/out/dlss5-feed.addon64')) { throw 'Build the native bridge before packaging.' }
    $managedArgs = @()
    if ($ManagedDLLPath) { $managedArgs += "-p:ManagedDLLPath=$ManagedDLLPath" }
    & dotnet build NeuralFX.Hub/NeuralFX.Hub.csproj -c Release -p:SkipDeploy=true @managedArgs --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed.' }
    & dotnet test NeuralFX.Tests/NeuralFX.Tests.csproj -c Release @managedArgs --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $package = Join-Path $taskRoot ('artifacts/releases/NeuralFX-2.1.0-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    # Un unico ejecutable, sin DLL sueltas: asi puede vivir dentro de Addons/Mods, que es
    # donde el usuario espera encontrarlo. CS1 escanea *.dll de esa carpeta recursivamente y
    # trata de cargarlas como ensamblados del juego; sin ninguna, no hay nada que cargar.
    & dotnet publish NeuralFX.Hub/NeuralFX.Hub.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none @managedArgs -o (Join-Path $package 'App') --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Hub publish failed.' }
    & dotnet publish tools/NeuralFX.PackageInstall/NeuralFX.PackageInstall.csproj -c Release -r win-x64 --self-contained false @managedArgs -o (Join-Path $package 'Hub') --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Package installer publish failed.' }
    Copy-Item -LiteralPath 'NeuralFX.Mod/bin/Release/net35/NeuralFX.dll' -Destination $package
    foreach ($file in @('README.md','LICENSE','NOTICE','Iniciar-NeuralFX-Hub.bat','Install-NeuralFX.ps1')) { Copy-Item -LiteralPath $file -Destination $package }
    Copy-Item -LiteralPath 'Native/out/capabilities.json' -Destination $package
    Copy-Item -LiteralPath 'docs' -Destination (Join-Path $package 'docs') -Recurse
    Copy-Item -LiteralPath 'licenses' -Destination (Join-Path $package 'licenses') -Recurse
    $manifest = [ordered]@{}
    Get-ChildItem -LiteralPath $package -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($package.Length).TrimStart('\', '/').Replace('\','/')
        $manifest[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'deployment-manifest.json') -Encoding utf8
    Compress-Archive -LiteralPath $package -DestinationPath ($package + '.zip')
    if ($Deploy) {
        & (Join-Path $package 'Install-NeuralFX.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Package install failed.' }
        # Install-NeuralFX solo pone el mod y el Hub. El addon nativo de la carpeta del juego
        # lo escribia unicamente el boton del Hub, asi que cada cambio del addon terminaba en
        # "abre el Hub y pulsa reinstalar". Aqui se refresca por el mismo camino transaccional,
        # y solo si de verdad hace falta.
        if (!$SkipPipelineRefresh) {
            & dotnet build tools/NeuralFX.PipelineInstall/NeuralFX.PipelineInstall.csproj -c Release @managedArgs --nologo -v:q
            if ($LASTEXITCODE -ne 0) { throw 'Pipeline installer build failed.' }
            # Se invoca el ejecutable, no "dotnet run": ese reenvia sus propias opciones al
            # programa y la primera version se trago un --nologo como si fuera argumento.
            & 'tools/NeuralFX.PipelineInstall/bin/Release/net8.0-windows/NeuralFX.PipelineInstall.exe' --si-hace-falta --addon 'Native/out/dlss5-feed.addon64'
            if ($LASTEXITCODE -ne 0) { throw "Pipeline refresh failed ($LASTEXITCODE). El estado anterior sigue recuperable desde el Hub." }
        }
    }
    Write-Output "Package: $package.zip"
} finally { Pop-Location }
