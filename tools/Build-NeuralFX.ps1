param([Parameter(Mandatory)][string]$ManagedDLLPath, [switch]$Package)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
foreach ($name in @('UnityEngine.dll','Assembly-CSharp.dll','ColossalManaged.dll','ICities.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $ManagedDLLPath $name))) { throw "Missing game assembly: $name" }
}
Push-Location $taskRoot
try {
    & ./Native/build.ps1 -SmokeHarness
    & dotnet build NeuralFX.Mod/NeuralFX.Mod.csproj -c Release "-p:ManagedDLLPath=$ManagedDLLPath" -p:SkipDeploy=true
    if ($LASTEXITCODE -ne 0) { throw 'Mod build with actual game assemblies failed.' }
    & dotnet test NeuralFX.Tests/NeuralFX.Tests.csproj -c Release "-p:ManagedDLLPath=$ManagedDLLPath"
    if ($LASTEXITCODE -ne 0) { throw 'Hub/IPC validation failed.' }
    & dotnet run --project tests/ModContracts/ModContracts.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Mod contract validation failed.' }
    if ($Package) { & ./tools/package.ps1 -SkipNativeBuild -ManagedDLLPath $ManagedDLLPath }
} finally { Pop-Location }
