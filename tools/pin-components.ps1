$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$catalogPath = Join-Path $taskRoot 'NeuralFX.Hub/Assets/components.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.UserAgent.ParseAdd('NeuralFX/2.0')
try {
    foreach ($component in $catalog) {
        if ($component.DownloadUrl) {
            $bytes = $client.GetByteArrayAsync([string]$component.DownloadUrl).GetAwaiter().GetResult()
            $component.ExpectedSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            $files = @{}
            if ($component.DownloadType -eq 'ZipExtract') {
                $archive = [System.IO.Compression.ZipArchive]::new([System.IO.MemoryStream]::new($bytes), [System.IO.Compression.ZipArchiveMode]::Read)
                try {
                    foreach ($pair in $component.PackageFiles.PSObject.Properties) {
                        $matches = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $pair.Name -or $_.FullName.Replace('\', '/').EndsWith('/' + $pair.Name, [System.StringComparison]::OrdinalIgnoreCase) })
                        if ($matches.Count -ne 1) { throw "Ambiguous or missing $($pair.Name)" }
                        $inputStream = $matches[0].Open()
                        try { $files[$pair.Value] = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($inputStream)).ToLowerInvariant() }
                        finally { $inputStream.Dispose() }
                    }
                } finally { $archive.Dispose() }
            } elseif ($component.DownloadType -eq 'ReShadeExeExtract') {
                $stage = Join-Path ([IO.Path]::GetTempPath()) ('NeuralFX_Pin_' + [Guid]::NewGuid().ToString('N'))
                New-Item -ItemType Directory -Path $stage | Out-Null
                try {
                    $setup = Join-Path $stage 'setup.exe'
                    [IO.File]::WriteAllBytes($setup, $bytes)
                    & "$env:SystemRoot/System32/tar.exe" -xf $setup -C $stage ReShade64.dll
                    if ($LASTEXITCODE -ne 0) { throw 'ReShade extraction failed' }
                    $files[$component.TargetRelativePath] = (Get-FileHash -LiteralPath (Join-Path $stage 'ReShade64.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
                } finally {
                    # Remove only the two files this invocation created in its private staging directory.
                    foreach ($name in @('setup.exe', 'ReShade64.dll')) {
                        $path = Join-Path $stage $name
                        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
                    }
                    Remove-Item -LiteralPath $stage
                }
            } else { $files[$component.TargetRelativePath] = $component.ExpectedSha256 }
            $component | Add-Member -NotePropertyName ExpectedFileSha256 -NotePropertyValue $files -Force
            Write-Output "$($component.Id): $($bytes.Length) bytes $($component.ExpectedSha256)"
        }
    }
    $catalog | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $catalogPath -Encoding utf8
} finally { $client.Dispose() }
