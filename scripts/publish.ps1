param(
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier,
    [string]$VersionSuffix
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/graft/graft.csproj"
$installDir = Join-Path $env:USERPROFILE ".graft\bin"
$installPath = Join-Path $installDir "graft.exe"

if (-not $RuntimeIdentifier) {
    $RuntimeIdentifier = switch ($env:PROCESSOR_ARCHITECTURE) {
        "ARM64"  { "win-arm64" }
        default  { "win-x64" }
    }
}

$publishDir = Join-Path $repoRoot "artifacts/publish/graft/$RuntimeIdentifier"

$publishArguments = @(
    "publish"
    $projectPath
    "-c"
    $Configuration
    "-r"
    $RuntimeIdentifier
    "--self-contained"
    "true"
    "/p:PublishSingleFile=true"
    "--output"
    $publishDir
)

if ($VersionSuffix) {
    $publishArguments += "/p:VersionSuffix=$VersionSuffix"
}

dotnet @publishArguments

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $publishDir "graft.exe") $installPath -Force

$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
$pathParts = if ($userPath) { $userPath -split ";" } else { @() }
if (-not ($pathParts | Where-Object { $_ -ieq $installDir })) {
    $newUserPath = if ($userPath) { "$userPath;$installDir" } else { $installDir }
    [Environment]::SetEnvironmentVariable("PATH", $newUserPath, "User")
    if ($env:PATH -notmatch [regex]::Escape($installDir)) {
        $env:PATH = "$env:PATH;$installDir"
    }
    Write-Host "Added $installDir to user PATH."
    Write-Host "Restart your shell for the change to take effect in new sessions."
}

Write-Host "Current user profile: $env:USERPROFILE"
Write-Host "Published runtime: $RuntimeIdentifier"
if ($VersionSuffix) {
    Write-Host "Published version suffix: $VersionSuffix"
}
Write-Host "Installed graft to $installPath"
