$ErrorActionPreference = 'Stop'
$upstream = Join-Path $PSScriptRoot 'upstream'
function Checkout-Sdk([string]$name, [string]$url, [string]$commit) {
    $target = Join-Path $PSScriptRoot "sdk/$name"
    if (!(Test-Path -LiteralPath $target)) {
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        & git -C $target init -q
        & git -C $target remote add origin $url
        & git -C $target sparse-checkout set --no-cone '/include/**' '/lib/Windows_x86_64/x64/nvsdk_ngx_d.lib' '/LICENSE*'
        & git -C $target fetch --depth 1 --filter=blob:none origin $commit
        if ($LASTEXITCODE -ne 0) { throw "Fetch failed: $name" }
        & git -C $target checkout -q --detach FETCH_HEAD
    }
    if ((& git -C $target rev-parse HEAD) -ne $commit) { throw "Wrong SDK revision: $name" }
    return $target
}
$ngx = Checkout-Sdk 'ngx' 'https://github.com/NVIDIA/DLSS.git' 'a291cc7d2cc642a51566f3dfd5376f635cd1b284'
$vulkan = Checkout-Sdk 'vulkan' 'https://github.com/KhronosGroup/Vulkan-Headers.git' 'ee2ec5fd83dafce291024683b50dc89219333076'
New-Item -ItemType Directory -Path (Join-Path $upstream 'external/ngx/libs') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $ngx 'include') -Filter 'nvsdk_ngx*.h' | Copy-Item -Destination (Join-Path $upstream 'external/ngx') -Force
Copy-Item -LiteralPath (Join-Path $ngx 'lib/Windows_x86_64/x64/nvsdk_ngx_d.lib') -Destination (Join-Path $upstream 'external/ngx/libs') -Force
foreach ($folder in @('vulkan', 'vk_video')) {
    New-Item -ItemType Directory -Path (Join-Path $upstream "external/vulkan/$folder") -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $vulkan "include/$folder") -Filter '*.h' | Copy-Item -Destination (Join-Path $upstream "external/vulkan/$folder") -Force
}
