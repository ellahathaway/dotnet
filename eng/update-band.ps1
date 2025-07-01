param(
    [string]$remote,
    [string]$branch_1xx,
    [switch]$continue,
    [switch]$help
)

function Show-Usage {
    Write-Output "Usage: script.ps1 --remote <name> --1xx-branch <branch>"
    Write-Output ""
    Write-Output "  -remote <name>         Git remote to pull from (e.g., upstream)"
    Write-Output "  -branch_1xx <branch>   1xx branch name to merge in (e.g., main)"
    Write-Output "  -continue              Continue after manually fixing merge conflicts"
    Write-Output "  -help                  Show this help message and exit"
    Write-Output ""
}

function Attempt-Merge {
    $conflicts = git diff --check | Out-String
    $unmergedFiles = git ls-files -u

    if ($conflicts.Trim() -ne "" -or $unmergedFiles.Count -gt 0) {
        Write-Error "There are unresolved conflicts. Please resolve them before continuing."
        exit 1
    } else {
        Write-Output "Continuing with merge..."
        $mergeMsg = Get-Content ".git/MERGE_MSG" -TotalCount 1
        git commit -m "$mergeMsg"
    }
}

$deleted_repos = @(
    "aspire", "aspnetcore", "cecil", "command-line-api", "deployment-tools",
    "diagnostics", "efcore", "emsdk", "runtime", "source-build-externals",
    "sourcelink", "symreader", "windowsdesktop", "winforms", "wpf", "xdt"
)

if ($help -or !$PSBoundParameters.Keys.Count) {
    Show-Usage
    exit 0
}

if (-not $continue) {
    if (-not $remote) {
        Write-Error "Error: --remote is required."
        Show-Usage
        exit 1
    }
    if (-not $branch_1xx) {
        Write-Error "Error: --1xx-branch is required."
        Show-Usage
        exit 1
    }

    git diff --quiet
    $diffExit = $LASTEXITCODE

    git diff --cached --quiet
    $cachedExit = $LASTEXITCODE

    if ($diffExit -ne 0 -or $cachedExit -ne 0) {
        Write-Error "You have uncommitted changes. Please commit or stash them before continuing."
        exit 1
    }

    $mergeTarget = "$remote/$branch_1xx"
    $mergeResult = & git merge --no-commit --no-ff "$mergeTarget" 2>&1
    $mergeExitCode = $LASTEXITCODE

    if ($mergeExitCode -ne 0) {
        Write-Output "Cleaning excluded paths..."

        foreach ($repo in $deleted_repos) {
            $repoPath = Join-Path -Path "src" -ChildPath $repo
            if (Test-Path $repoPath -PathType Container) {
                git reset HEAD -- "$repoPath" 2>$null -ErrorAction SilentlyContinue
                git rm -rf --cached "$repoPath" 2>$null -ErrorAction SilentlyContinue
                Remove-Item -Recurse -Force "$repoPath" -ErrorAction SilentlyContinue
            }
        }

        Attempt-Merge
    }
} else {
    Attempt-Merge
}

Write-Output "Completed merge"
