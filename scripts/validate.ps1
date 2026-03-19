param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$KeepArtifacts,
    [switch]$SkipBuild
)

# Keep this script behaviorally aligned with scripts/validate.sh.
# When validation scenarios or expectations change, update both scripts together.

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/graft/graft.csproj"
$nuGetConfigPath = Join-Path $repoRoot "NuGet.config"
$dotnetExe = (Get-Command dotnet).Source
$gitExe = (Get-Command git).Source
$scratchRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("graft-validation-" + [System.Guid]::NewGuid().ToString("N"))
$assemblyPath = Join-Path $repoRoot "src/graft/bin/$Configuration/net10.0/graft.dll"
$dotnetHome = Join-Path $repoRoot ".dotnet"
$results = [System.Collections.Generic.List[object]]::new()

$originalPath = $env:PATH
$originalDotnetCliHome = $env:DOTNET_CLI_HOME
$originalDotnetSkipFirstTime = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$originalDotnetTelemetryOptOut = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$wtDirectories = @(Get-Command wt.exe -All -ErrorAction SilentlyContinue | ForEach-Object {
    Split-Path $_.Source
}) | Select-Object -Unique
$validationPathEntries = @(
    (Split-Path $dotnetExe)
    (Split-Path $gitExe)
    ($originalPath -split ";" | Where-Object { $_ -and $_ -notin $wtDirectories })
) | Select-Object -Unique

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-Git {
    param(
        [string]$WorkingDirectory,
        [string[]]$Arguments
    )

    $output = & $script:gitExe -C $WorkingDirectory @Arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $joinedArguments = [string]::Join(" ", $Arguments)
        throw "git $joinedArguments failed with exit code $exitCode.`n$($output.Trim())"
    }

    return $output.Trim()
}

function Invoke-Graft {
    param(
        [string]$WorkingDirectory,
        [string[]]$Arguments,
        [int]$ExpectedExitCode = 0,
        [string]$InputText = ""
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:dotnetExe
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $allArguments = @($script:assemblyPath) + $Arguments
    $startInfo.Arguments = [string]::Join(" ", ($allArguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + $_.Replace('"', '\"') + '"'
        }
        else {
            $_
        }
    }))

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $null = $process.Start()
    if (-not [string]::IsNullOrEmpty($InputText)) {
        $process.StandardInput.Write($InputText)
    }
    $process.StandardInput.Close()
    $standardOutput = $process.StandardOutput.ReadToEnd().Trim()
    $standardError = $process.StandardError.ReadToEnd().Trim()
    $process.WaitForExit()
    $exitCode = $process.ExitCode

    if ([string]::IsNullOrWhiteSpace($standardError)) {
        $output = $standardOutput
    }
    elseif ([string]::IsNullOrWhiteSpace($standardOutput)) {
        $output = $standardError
    }
    else {
        $output = $standardOutput + [System.Environment]::NewLine + $standardError
    }

    if ($exitCode -ne $ExpectedExitCode) {
        $joinedArguments = [string]::Join(" ", $Arguments)
        throw "graft $joinedArguments exited with $exitCode, expected $ExpectedExitCode.`n$output"
    }

    return $output.Trim()
}

function New-Repository {
    param([string]$Name)

    $path = Join-Path $script:scratchRoot $Name
    New-Item -ItemType Directory -Path $path | Out-Null

    Invoke-Git $path @("init", "--quiet", "-b", "main") | Out-Null
    Invoke-Git $path @("config", "user.email", "graft@example.com") | Out-Null
    Invoke-Git $path @("config", "user.name", "graft validation") | Out-Null

    Set-Content -Path (Join-Path $path "README.txt") -Value "base"
    Invoke-Git $path @("add", ".") | Out-Null
    Invoke-Git $path @("commit", "--quiet", "-m", "init") | Out-Null

    return $path
}

function Get-GitSha {
    param(
        [string]$WorkingDirectory,
        [string]$Reference
    )

    return Invoke-Git $WorkingDirectory @("rev-parse", $Reference)
}

function Get-WorktreePath {
    param(
        [string]$WorkingDirectory,
        [string]$BranchName
    )

    $normalizedOutput = (Invoke-Git $WorkingDirectory @("worktree", "list", "--porcelain")).Replace("`r`n", "`n").Trim()
    $blocks = [System.Text.RegularExpressions.Regex]::Split($normalizedOutput, "`n`n+")

    foreach ($block in $blocks) {
        $path = $null
        $branch = $null

        foreach ($line in $block.Split("`n", [System.StringSplitOptions]::RemoveEmptyEntries)) {
            if ($line.StartsWith("worktree ", [System.StringComparison]::Ordinal)) {
                $path = $line.Substring("worktree ".Length)
                continue
            }

            if ($line.StartsWith("branch refs/heads/", [System.StringComparison]::Ordinal)) {
                $branch = $line.Substring("branch refs/heads/".Length)
            }
        }

        if ($branch -eq $BranchName) {
            return $path
        }
    }

    throw "Could not find worktree path for branch '$BranchName'."
}

function Invoke-Scenario {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host "Running scenario: $Name"

    try {
        & $Action
        $script:results.Add([pscustomobject]@{
            Name = $Name
            Passed = $true
            Message = "ok"
        })
    }
    catch {
        $script:results.Add([pscustomobject]@{
            Name = $Name
            Passed = $false
            Message = $_.Exception.Message
        })
    }
}

try {
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_CLI_HOME = $dotnetHome
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

    New-Item -ItemType Directory -Path $scratchRoot | Out-Null

    if (-not $SkipBuild) {
        & $dotnetExe restore $projectPath -nologo --configfile $nuGetConfigPath -p:RuntimeIdentifiers=
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed."
        }

        & $dotnetExe build $projectPath -c $Configuration -nologo --configfile $nuGetConfigPath --no-restore -p:RuntimeIdentifiers=
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed."
        }
    }

    Assert-True (Test-Path $assemblyPath) "Expected build output at $assemblyPath."

    $env:PATH = $validationPathEntries -join ";"

    Invoke-Scenario "create uses HEAD by default and main when requested" {
        $repo = New-Repository "create-local"

        Invoke-Git $repo @("switch", "--quiet", "-c", "dev") | Out-Null
        Invoke-Git $repo @("commit", "--quiet", "--allow-empty", "-m", "dev") | Out-Null

        $mainSha = Get-GitSha $repo "main"
        $devSha = Get-GitSha $repo "HEAD"

        $defaultOutput = Invoke-Graft $repo @("create", "feature/default-head")
        $defaultSha = Get-GitSha $repo "refs/heads/feature/default-head"
        Assert-True ($defaultSha -eq $devSha) "Expected default create to use the current HEAD."
        Assert-True ($defaultOutput.Contains("wt.exe was not found")) "Expected validation runs to surface the missing wt.exe warning."

        $fromMainOutput = Invoke-Graft $repo @("create", "feature/from-main", "-l")
        $fromMainSha = Get-GitSha $repo "refs/heads/feature/from-main"
        Assert-True ($fromMainSha -eq $mainSha) "Expected --from-local-main to use local main."
        Assert-True ($fromMainOutput.Contains("Creating new branch 'feature/from-main' from main...")) "Expected create output to mention main as the selected base."
    }

    Invoke-Scenario "create resolves origin/main and validates flag combinations" {
        $repo = New-Repository "create-origin-source"
        $mainSha = Get-GitSha $repo "main"

        Invoke-Git $repo @("update-ref", "refs/remotes/origin/main", $mainSha) | Out-Null
        Invoke-Git $repo @("switch", "--quiet", "-c", "dev", "main") | Out-Null
        Invoke-Git $repo @("branch", "-D", "main") | Out-Null

        $originMainSha = Get-GitSha $repo "refs/remotes/origin/main"

        $fromOriginOutput = Invoke-Graft $repo @("create", "feature/from-origin", "-o")
        $fromOriginSha = Get-GitSha $repo "refs/heads/feature/from-origin"
        Assert-True ($fromOriginSha -eq $originMainSha) "Expected --from-origin-main / -o to use origin/main."
        Assert-True ($fromOriginOutput.Contains("from origin/main")) "Expected create output to mention origin/main."

        $fallbackOutput = Invoke-Graft $repo @("create", "feature/from-main-fallback", "--from-local-main")
        $fallbackSha = Get-GitSha $repo "refs/heads/feature/from-main-fallback"
        Assert-True ($fallbackSha -eq $originMainSha) "Expected --from-local-main to fall back to origin/main."
        Assert-True ($fallbackOutput.Contains("from origin/main")) "Expected fallback output to mention origin/main."

        Invoke-Git $repo @("branch", "feature/existing", "origin/main") | Out-Null
        $warningOutput = Invoke-Graft $repo @("create", "feature/existing", "--from-local-main")
        $normalizedWarningOutput = $warningOutput -replace '\s+', ' '
        Assert-True ($normalizedWarningOutput.Contains("Ignoring --from-local-main because branch 'feature/existing' already exists.")) "Expected existing-branch create to warn when --from-local-main is ignored."

        $parseFailureOutput = Invoke-Graft $repo @("create", "feature/invalid", "-l", "-o") 1
        $normalizedParseFailureOutput = $parseFailureOutput -replace '\s+', ' '
        Assert-True ($normalizedParseFailureOutput.Contains("--from-local-main and --from-origin-main cannot be used together.")) "Expected mutual exclusion validation for create flags."
    }

    Invoke-Scenario "list and remove manage created worktrees" {
        $repo = New-Repository "list-remove"

        Invoke-Git $repo @("switch", "--quiet", "-c", "dev") | Out-Null
        Invoke-Git $repo @("commit", "--quiet", "--allow-empty", "-m", "dev") | Out-Null

        Invoke-Graft $repo @("create", "feature/listable") | Out-Null
        Invoke-Graft $repo @("create", "feature/remove-me") | Out-Null
        Invoke-Graft $repo @("create", "feature/remove-dirty") | Out-Null

        $listOutput = Invoke-Graft $repo @("list")
        Assert-True ($listOutput.Contains("feature/listable")) "Expected list output to include feature/listable."
        Assert-True ($listOutput.Contains("feature/remove-me")) "Expected list output to include feature/remove-me."
        Assert-True ($listOutput.Contains("yes")) "Expected list output to mark managed worktrees."

        $removePath = Get-WorktreePath $repo "feature/remove-me"
        Invoke-Graft $repo @("remove", "feature/remove-me") | Out-Null
        Assert-True (-not (Test-Path $removePath)) "Expected remove to delete the selected worktree path."

        $dirtyPath = Get-WorktreePath $repo "feature/remove-dirty"
        Set-Content -Path (Join-Path $dirtyPath "dirty.txt") -Value "dirty"
        Invoke-Graft $repo @("remove", "feature/remove-dirty", "--force") | Out-Null
        Assert-True (-not (Test-Path $dirtyPath)) "Expected force remove to delete a dirty worktree path."
    }

    Invoke-Scenario "cleanup removes all candidates non-interactively" {
        $repo = New-Repository "cleanup"

        Invoke-Git $repo @("switch", "--quiet", "-c", "dev") | Out-Null
        Invoke-Git $repo @("commit", "--quiet", "--allow-empty", "-m", "dev") | Out-Null

        Invoke-Graft $repo @("create", "feature/cleanup-one") | Out-Null
        Invoke-Graft $repo @("create", "feature/cleanup-two") | Out-Null

        $cleanupOutput = Invoke-Graft $repo @("cleanup", "--all", "--yes")
        Assert-True ($cleanupOutput.Contains("Removed worktree:")) "Expected cleanup to report removed worktrees."

        $worktreeList = Invoke-Git $repo @("worktree", "list", "--porcelain")
        Assert-True (-not $worktreeList.Contains("feature/cleanup-one")) "Expected cleanup to remove feature/cleanup-one."
        Assert-True (-not $worktreeList.Contains("feature/cleanup-two")) "Expected cleanup to remove feature/cleanup-two."
    }

    Invoke-Scenario "prune removes stale worktree metadata" {
        $repo = New-Repository "prune"

        Invoke-Git $repo @("switch", "--quiet", "-c", "dev") | Out-Null
        Invoke-Git $repo @("commit", "--quiet", "--allow-empty", "-m", "dev") | Out-Null

        Invoke-Graft $repo @("create", "feature/prune-me") | Out-Null

        $prunePath = Get-WorktreePath $repo "feature/prune-me"
        Remove-Item -Recurse -Force $prunePath

        Invoke-Graft $repo @("prune") | Out-Null
        $worktreeList = Invoke-Git $repo @("worktree", "list", "--porcelain")
        Assert-True (-not $worktreeList.Contains("feature/prune-me")) "Expected prune to remove stale worktree metadata."
    }

    Invoke-Scenario "navigate works from repo and shared root" {
        $repo = New-Repository "navigate"

        Invoke-Git $repo @("switch", "--quiet", "-c", "dev") | Out-Null
        Invoke-Git $repo @("commit", "--quiet", "--allow-empty", "-m", "dev") | Out-Null

        Invoke-Graft $repo @("create", "feature/navigate") | Out-Null

        $navigateFromRepoOutput = Invoke-Graft $repo @("navigate") 0 "1`n"
        Assert-True ($navigateFromRepoOutput.Contains("wt.exe was not found")) "Expected navigate from repo mode to open the selected worktree."

        $sharedRoot = Split-Path -Parent $repo
        $managedRoot = Join-Path $sharedRoot ".worktrees"
        New-Item -ItemType Directory -Path (Join-Path $managedRoot "invalid-entry") | Out-Null

        $navigateFromSharedRootOutput = Invoke-Graft $sharedRoot @("navigate") 0 "1`n"
        Assert-True ($navigateFromSharedRootOutput.Contains("Reading managed worktrees...")) "Expected navigate shared-root mode to read managed worktrees."
        Assert-True ($navigateFromSharedRootOutput.Contains("wt.exe was not found")) "Expected navigate shared-root mode to open the selected worktree."
    }

    Write-Host ""
    Write-Host "Validation summary:"
    foreach ($result in $results) {
        $status = if ($result.Passed) { "PASS" } else { "FAIL" }
        Write-Host ("[{0}] {1}" -f $status, $result.Name)
        if (-not $result.Passed) {
            Write-Host $result.Message
        }
    }

    $hasFailures = $results.Exists([Predicate[object]]{
        param($result)
        return -not $result.Passed
    })

    if ($hasFailures) {
        throw "Validation failed. Preserving scratch directory at $scratchRoot."
    }

    Write-Host ""
    Write-Host "All validation scenarios passed."
}
finally {
    $env:PATH = $originalPath
    $env:DOTNET_CLI_HOME = $originalDotnetCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $originalDotnetSkipFirstTime
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $originalDotnetTelemetryOptOut

    if ($KeepArtifacts -or ($results.Count -gt 0 -and $results.Exists([Predicate[object]]{
        param($result)
        return -not $result.Passed
    }))) {
        if (Test-Path $scratchRoot) {
            Write-Host "Validation scratch directory preserved at $scratchRoot"
        }
    }
    elseif (Test-Path $scratchRoot) {
        Remove-Item -Recurse -Force $scratchRoot
    }
}
