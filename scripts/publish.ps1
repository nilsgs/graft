param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/graft/graft.csproj"
$publishDir = Join-Path $repoRoot "artifacts/publish/graft/win-x64"
$installDir = Join-Path $env:USERPROFILE "bin"
$installPath = Join-Path $installDir "graft.exe"

dotnet publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    --output $publishDir

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $publishDir "graft.exe") $installPath -Force

Write-Host "Current user profile: $env:USERPROFILE"
Write-Host "Installed graft to $installPath"
