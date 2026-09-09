param([switch]$Deploy, [switch]$SkipNativeBuild, [string]$ManagedDLLPath)
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
    & dotnet publish NeuralFX.Hub/NeuralFX.Hub.csproj -c Release -r win-x64 --self-contained false @managedArgs -o (Join-Path $package 'Hub') --nologo -v:q
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
    }
    Write-Output "Package: $package.zip"
} finally { Pop-Location }
