[CmdletBinding(PositionalBinding=$false)]
Param(
    [Parameter(Mandatory = $true)]
    [Alias("remote")]
    [string]$Remote,

    [Parameter(Mandatory = $true)]
    [Alias("1xx-branch")]
    [string]$Branch1xx
)

function Get-Usage {
    Write-Output "  -Remote <name>           Git remote to pull from (e.g., upstream)"
    Write-Output "  -1xx-branch <branch>     1xx branch name to merge in (e.g., main)"
    Write-Output ""
    Write-Output "  Use -? or -Help to show this message."
}

if ($help) {
  Get-Usage
  exit 0
}

# List of deleted repos
$DeletedRepos = @(
    "aspire", "aspnetcore", "cecil", "command-line-api", "deployment-tools",
    "diagnostics", "efcore", "emsdk", "runtime", "source-build-externals",
    "sourcelink", "symreader", "windowsdesktop", "winforms", "wpf", "xdt"
)

# Attempt merge
$mergeSuccess = git merge --no-commit --no-ff "$Remote/$Branch1xx" 2>$null

if (-not $?) {
    Write-Output "Cleaning excluded paths..."

    foreach ($repo in $DeletedRepos) {
        foreach ($path in "src/$repo", "repo-projects/$repo.proj") {
            if (Test-Path $path) {
                git reset HEAD -- "$path" 2>$null
                git rm -rf --cached "$path" 2>$null
                Remove-Item -Recurse -Force "$path" -ErrorAction SilentlyContinue
            }
        }
    }

    $mergeMsg = Get-Content ".git/MERGE_MSG" -TotalCount 1
    git commit -m "$mergeMsg"
}

Write-Output "Completed merge"
