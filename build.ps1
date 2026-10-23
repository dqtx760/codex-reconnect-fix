#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectFile = Join-Path $PSScriptRoot 'CodexReconnectTool.csproj'
$distRoot = Join-Path $PSScriptRoot 'dist'
$publishDir = Join-Path $distRoot 'win-x64'
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$version = [string]$project.Project.PropertyGroup.Version

dotnet publish $projectFile -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw '编译失败，未生成发布压缩包。'
}

$exePath = Join-Path $publishDir 'CodexReconnectTool.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw '未找到编译后的程序。'
}
$readmePath = Join-Path $publishDir 'README.md'
$licensePath = Join-Path $publishDir 'LICENSE'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $readmePath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $licensePath -Force

$zipPath = Join-Path $distRoot "CodexReconnectTool-$version-win-x64.zip"
Compress-Archive -LiteralPath @($exePath, $readmePath, $licensePath) `
    -DestinationPath $zipPath -CompressionLevel Optimal -Force

$checksums = foreach ($path in @($exePath, $zipPath)) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, [System.IO.Path]::GetFileName($path)
}
$checksumPath = Join-Path $distRoot 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($checksumPath, [string[]]$checksums,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "程序：$exePath"
Write-Host "压缩包：$zipPath"
Write-Host "校验文件：$checksumPath"
