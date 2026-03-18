#!/usr/bin/env bash
# backport-rebootstrap.sh
#
# Backports a merged 1xx rebootstrap PR to non-1xx feature band branches,
# skipping bands that were released (since they get their own rebootstrap).
#
# Prerequisites:
#   - gh CLI (authenticated)
#   - git, curl, jq
#
# Usage:
#   ./backport-rebootstrap.sh --pr <NUMBER> --dotnet-repo <PATH> [--list-targets] [--dry-run]
#   ./backport-rebootstrap.sh --pr 5417 --dotnet-repo ~/repos/dotnet --list-targets
#   ./backport-rebootstrap.sh --pr 5417 --dotnet-repo ~/repos/dotnet

set -euo pipefail

# ─── Defaults ───────────────────────────────────────────────────────────────
PR_NUMBER=""
REPO="dotnet/dotnet"
DOTNET_REPO_PATH=""
DRY_RUN=false
LIST_TARGETS=false
FORK=""
UPSTREAM_REMOTE=""

# ─── Parse arguments ────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case $1 in
        --pr)              PR_NUMBER="$2";         shift 2 ;;
        --repo)            REPO="$2";              shift 2 ;;
        --dotnet-repo)     DOTNET_REPO_PATH="$2";  shift 2 ;;
        --dry-run)         DRY_RUN=true;           shift   ;;
        --list-targets)    LIST_TARGETS=true;       shift   ;;
        --fork)            FORK="$2";              shift 2 ;;
        --upstream-remote) UPSTREAM_REMOTE="$2";   shift 2 ;;
        -h|--help)
            sed -n '2,12p' "$0" | sed 's/^# \?//'
            exit 0
            ;;
        *) echo "Error: Unknown option: $1"; exit 1 ;;
    esac
done

# ─── Validate required arguments ────────────────────────────────────────────
if [[ -z "$PR_NUMBER" ]]; then
    echo "Error: --pr <PR_NUMBER> is required"
    exit 1
fi

if [[ -z "$DOTNET_REPO_PATH" ]]; then
    echo "Error: --dotnet-repo <PATH> is required"
    exit 1
fi

if [[ ! -d "$DOTNET_REPO_PATH/.git" ]]; then
    echo "Error: $DOTNET_REPO_PATH is not a git repository"
    exit 1
fi

DOTNET_REPO_PATH="$(cd "$DOTNET_REPO_PATH" && pwd)"

# ─── Detect upstream remote ─────────────────────────────────────────────────
# The local clone may use "origin", "upstream", or another name for the
# dotnet/dotnet remote.  Auto-detect if not specified.
if [[ -z "$UPSTREAM_REMOTE" ]]; then
    UPSTREAM_REMOTE=$(cd "$DOTNET_REPO_PATH" && git remote -v \
        | grep -i 'dotnet/dotnet' \
        | grep '(fetch)' \
        | head -1 \
        | awk '{print $1}')
    if [[ -z "$UPSTREAM_REMOTE" ]]; then
        # Fallback: try "upstream", then "origin"
        if cd "$DOTNET_REPO_PATH" && git remote get-url upstream &>/dev/null; then
            UPSTREAM_REMOTE="upstream"
        else
            UPSTREAM_REMOTE="origin"
        fi
    fi
fi

echo "Using upstream remote: $UPSTREAM_REMOTE"

# ─── Step 1: Get PR information ─────────────────────────────────────────────
echo "==> Fetching PR #${PR_NUMBER} info from ${REPO}..."

PR_JSON="$(gh pr view "$PR_NUMBER" --repo "$REPO" --json baseRefName,title,state,mergeCommit,commits)"

PR_STATE="$(echo "$PR_JSON" | jq -r '.state')"
if [[ "$PR_STATE" != "MERGED" ]]; then
    echo "Error: PR #${PR_NUMBER} is not merged (state: ${PR_STATE}). Only merged PRs can be backported."
    exit 1
fi

BASE_BRANCH="$(echo "$PR_JSON" | jq -r '.baseRefName')"
SOURCE_PR_TITLE="$(echo "$PR_JSON" | jq -r '.title')"
MERGE_COMMIT="$(echo "$PR_JSON" | jq -r '.mergeCommit.oid')"

# Filter out merge commits — only keep non-merge commits for cherry-picking.
# Merge commits (commits with 2+ parents) cannot be cherry-picked without -m
# and are not needed since the real changes are in the non-merge commits.
ALL_COMMIT_SHAS="$(echo "$PR_JSON" | jq -r '.commits[].oid')"

echo "  Base branch:  $BASE_BRANCH"
echo "  PR title:     $SOURCE_PR_TITLE"
echo "  Merge commit: $MERGE_COMMIT"
echo "  PR commits:   $(echo "$ALL_COMMIT_SHAS" | wc -l) commit(s)"

# ─── Step 2: Extract major.minor and source band ────────────────────────────
# Expected format: release/{major.minor}.{N}xx
if [[ ! "$BASE_BRANCH" =~ ^release/([0-9]+\.[0-9]+)\.([0-9]+)xx$ ]]; then
    echo "Error: Base branch '$BASE_BRANCH' does not match expected pattern 'release/{major.minor}.{N}xx'"
    exit 1
fi

MAJOR_MINOR="${BASH_REMATCH[1]}"
SOURCE_BAND="${BASH_REMATCH[2]}"

echo "  Major.Minor:  $MAJOR_MINOR"
echo "  Source band:   ${SOURCE_BAND}xx"

if [[ "$SOURCE_BAND" != "1" ]]; then
    echo "Warning: Source PR is on a ${SOURCE_BAND}xx branch, not 1xx. This skill is designed for 1xx rebootstrap backports."
    echo "Proceeding anyway — the script will backport to all non-${SOURCE_BAND}xx, non-released bands."
fi

# ─── Step 3: Fetch release metadata ─────────────────────────────────────────
echo ""
echo "==> Fetching release metadata for ${MAJOR_MINOR}..."

RELEASES_JSON_URL="https://builds.dotnet.microsoft.com/dotnet/release-metadata/${MAJOR_MINOR}/releases.json"
RELEASES_JSON="$(curl -sS "$RELEASES_JSON_URL")"

if [[ -z "$RELEASES_JSON" ]]; then
    echo "Error: Failed to fetch release metadata from $RELEASES_JSON_URL"
    exit 1
fi

LATEST_RELEASE_VERSION="$(echo "$RELEASES_JSON" | jq -r '.["latest-release"]')"
echo "  Latest release: $LATEST_RELEASE_VERSION"

# ─── Step 4: Determine released feature bands ───────────────────────────────
# Extract SDK versions from the sdks array of the latest release.
# Each SDK version like "10.0.105" has the band digit as the first char of the patch number.
RELEASED_SDK_VERSIONS="$(echo "$RELEASES_JSON" | jq -r '.releases[0].sdks[]?.version // empty')"

if [[ -z "$RELEASED_SDK_VERSIONS" ]]; then
    # Fall back to the single sdk entry if sdks array is empty/missing
    RELEASED_SDK_VERSIONS="$(echo "$RELEASES_JSON" | jq -r '.releases[0].sdk.version // empty')"
fi

RELEASED_BANDS=()
echo "  Released SDKs:"
while IFS= read -r sdk_version; do
    [[ -z "$sdk_version" ]] && continue
    # Extract the patch part (third segment) and strip last two digits to get band.
    # e.g. "105" → "1", "201" → "2", "1000" → "10"
    patch="$(echo "$sdk_version" | cut -d. -f3)"
    band="$(echo "$patch" | sed 's/[0-9][0-9]$//')"
    echo "    $sdk_version → ${band}xx"
    RELEASED_BANDS+=("$band")
done <<< "$RELEASED_SDK_VERSIONS"

if [[ ${#RELEASED_BANDS[@]} -eq 0 ]]; then
    echo "Error: Could not determine released feature bands from release metadata"
    exit 1
fi

echo "  Released bands: $(printf '%sxx ' "${RELEASED_BANDS[@]}")"

# ─── Step 5: List feature band branches ──────────────────────────────────────
echo ""
echo "==> Listing feature band branches for ${MAJOR_MINOR}..."

# Use gh API to list branches matching the pattern
ALL_BRANCHES="$(gh api "repos/${REPO}/branches" --paginate --jq '.[].name' | grep -E "^release/${MAJOR_MINOR}\.[0-9]+xx$" | sort)"

if [[ -z "$ALL_BRANCHES" ]]; then
    echo "Error: No feature band branches found matching 'release/${MAJOR_MINOR}.*xx'"
    exit 1
fi

echo "  Found branches:"
while IFS= read -r branch; do
    echo "    $branch"
done <<< "$ALL_BRANCHES"

# ─── Step 6: Compute target branches ────────────────────────────────────────
echo ""
echo "==> Computing target branches..."

TARGET_BRANCHES=()
SKIPPED_BRANCHES=()

while IFS= read -r branch; do
    # Extract band digit from branch name
    if [[ "$branch" =~ ^release/[0-9]+\.[0-9]+\.([0-9]+)xx$ ]]; then
        branch_band="${BASH_REMATCH[1]}"

        # Skip the source branch's band
        if [[ "$branch_band" == "$SOURCE_BAND" ]]; then
            SKIPPED_BRANCHES+=("$branch (source branch)")
            continue
        fi

        # Skip released bands
        is_released=false
        for released_band in "${RELEASED_BANDS[@]}"; do
            if [[ "$branch_band" == "$released_band" ]]; then
                is_released=true
                break
            fi
        done

        if $is_released; then
            SKIPPED_BRANCHES+=("$branch (band ${branch_band}xx was released)")
            continue
        fi

        TARGET_BRANCHES+=("$branch")
    fi
done <<< "$ALL_BRANCHES"

echo "  Skipped:"
for entry in "${SKIPPED_BRANCHES[@]}"; do
    echo "    ✗ $entry"
done

echo "  Targets:"
if [[ ${#TARGET_BRANCHES[@]} -eq 0 ]]; then
    echo "    (none — all feature band branches were released or are the source)"
    echo ""
    echo "No backport PRs needed."
    exit 0
fi

for branch in "${TARGET_BRANCHES[@]}"; do
    echo "    ✓ $branch"
done

# ─── If --list-targets, stop here ───────────────────────────────────────────
if $LIST_TARGETS; then
    echo ""
    echo "Target branches listed above. Run without --list-targets to create backport PRs."
    exit 0
fi

# ─── Step 7: Create backport PRs ────────────────────────────────────────────
echo ""
echo "==> Creating backport PRs..."

cd "$DOTNET_REPO_PATH"

# Determine the push remote
if [[ -n "$FORK" ]]; then
    PUSH_REMOTE=""
    # Find a remote that matches the fork URL
    while IFS= read -r line; do
        remote_name="$(echo "$line" | awk '{print $1}')"
        remote_url="$(echo "$line" | awk '{print $2}')"
        if echo "$remote_url" | grep -qi "${FORK}"; then
            PUSH_REMOTE="$remote_name"
            break
        fi
    done < <(git remote -v | grep '(push)')

    if [[ -z "$PUSH_REMOTE" ]]; then
        echo "  Adding fork remote: $FORK"
        git remote add fork "https://github.com/${FORK}.git"
        PUSH_REMOTE="fork"
    fi
    echo "  Push remote: $PUSH_REMOTE (fork: $FORK)"
else
    # Default to the first remote with push access that points to dotnet/dotnet,
    # or fall back to upstream
    PUSH_REMOTE=""
    while IFS= read -r line; do
        remote_name="$(echo "$line" | awk '{print $1}')"
        remote_url="$(echo "$line" | awk '{print $2}')"
        if echo "$remote_url" | grep -qi 'dotnet/dotnet' && [[ "$remote_url" != "no_push" ]]; then
            PUSH_REMOTE="$remote_name"
            break
        fi
    done < <(git remote -v | grep '(push)')

    if [[ -z "$PUSH_REMOTE" ]]; then
        PUSH_REMOTE="$UPSTREAM_REMOTE"
    fi
    echo "  Push remote: $PUSH_REMOTE"
fi

# Fetch the latest from upstream and the PR commits
git fetch "$UPSTREAM_REMOTE" --quiet
echo "  Fetching PR #${PR_NUMBER} commits..."
git fetch "$UPSTREAM_REMOTE" "pull/${PR_NUMBER}/head:_pr-${PR_NUMBER}-temp" --quiet 2>/dev/null || {
    echo "  Warning: Could not fetch PR ref directly. Trying merge commit..."
    git fetch "$UPSTREAM_REMOTE" "$MERGE_COMMIT" --quiet 2>/dev/null || true
}

# Filter out merge commits by checking parent count
COMMIT_ARRAY=()
while IFS= read -r sha; do
    [[ -z "$sha" ]] && continue
    parent_count="$(git cat-file -p "$sha" 2>/dev/null | grep -c '^parent ' || echo 0)"
    if [[ "$parent_count" -le 1 ]]; then
        COMMIT_ARRAY+=("$sha")
    else
        echo "  Skipping merge commit: $sha"
    fi
done <<< "$ALL_COMMIT_SHAS"

if [[ ${#COMMIT_ARRAY[@]} -eq 0 ]]; then
    echo "Error: No non-merge commits found to cherry-pick"
    exit 1
fi

echo "  Commits to cherry-pick: ${#COMMIT_ARRAY[@]}"

ORIGINAL_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "detached")"

RESULTS=()

for target_branch in "${TARGET_BRANCHES[@]}"; do
    backport_branch="backport/pr-${PR_NUMBER}-to-${target_branch}"

    echo ""
    echo "--- Backporting to ${target_branch} ---"

    if $DRY_RUN; then
        echo "  [DRY RUN] Would create branch: $backport_branch"
        echo "  [DRY RUN] Would cherry-pick ${#COMMIT_ARRAY[@]} commit(s)"
        echo "  [DRY RUN] Would push to ${PUSH_REMOTE}"
        echo "  [DRY RUN] Would create PR targeting ${target_branch}"
        RESULTS+=("$target_branch|DRY_RUN|would-create")
        continue
    fi

    # Check if a backport branch already exists on the push remote
    if git ls-remote --heads "$PUSH_REMOTE" "$backport_branch" 2>/dev/null | grep -q "$backport_branch"; then
        echo "  Backport branch '$backport_branch' already exists on remote. Skipping."
        RESULTS+=("$target_branch|SKIPPED|branch already exists")
        continue
    fi

    # Create the backport branch from the target
    git branch -D "$backport_branch" 2>/dev/null || true
    git checkout -b "$backport_branch" "${UPSTREAM_REMOTE}/${target_branch}" --quiet

    # Cherry-pick the commits
    cherry_pick_failed=false
    for commit_sha in "${COMMIT_ARRAY[@]}"; do
        if ! git cherry-pick "$commit_sha" --quiet 2>/dev/null; then
            echo "  ✗ Cherry-pick failed for commit $commit_sha"
            echo "    Conflicts in: $(git diff --name-only --diff-filter=U 2>/dev/null | tr '\n' ' ')"
            echo "    Aborting cherry-pick. You will need to resolve conflicts manually."
            echo ""
            echo "    To resolve manually:"
            echo "      cd $DOTNET_REPO_PATH"
            echo "      git checkout -b $backport_branch ${UPSTREAM_REMOTE}/${target_branch}"
            echo "      git cherry-pick $commit_sha"
            echo "      # resolve conflicts, then: git cherry-pick --continue"
            git cherry-pick --abort 2>/dev/null || true
            cherry_pick_failed=true
            break
        fi
    done

    if $cherry_pick_failed; then
        git checkout "$ORIGINAL_BRANCH" --quiet 2>/dev/null || git checkout - --quiet 2>/dev/null || true
        git branch -D "$backport_branch" 2>/dev/null || true
        RESULTS+=("$target_branch|FAILED|cherry-pick conflict")
        continue
    fi

    # Push the branch
    echo "  Pushing $backport_branch to $PUSH_REMOTE..."
    git push "$PUSH_REMOTE" "$backport_branch" --quiet

    # Create the PR — mirror the source PR title but with the target branch prefix
    PR_TITLE="[${target_branch}] ${SOURCE_PR_TITLE}"
    PR_BODY="Backport of the rebootstrap changes from #${PR_NUMBER} (${BASE_BRANCH}) to ${target_branch}.

This updates the bootstrap SDK and Arcade toolset references to match the latest release.

---
*Auto-generated by backport-rebootstrap script.*"

    if [[ -n "$FORK" ]]; then
        FORK_OWNER="$(echo "$FORK" | cut -d/ -f1)"
        HEAD_REF="${FORK_OWNER}:${backport_branch}"
    else
        HEAD_REF="$backport_branch"
    fi

    NEW_PR_URL="$(gh pr create \
        --repo "$REPO" \
        --base "$target_branch" \
        --head "$HEAD_REF" \
        --title "$PR_TITLE" \
        --body "$PR_BODY" 2>&1)" || {
        echo "  ✗ Failed to create PR: $NEW_PR_URL"
        RESULTS+=("$target_branch|FAILED|PR creation failed")
        git checkout "$ORIGINAL_BRANCH" --quiet 2>/dev/null || git checkout - --quiet 2>/dev/null || true
        continue
    }

    echo "  ✓ Created PR: $NEW_PR_URL"
    RESULTS+=("$target_branch|CREATED|$NEW_PR_URL")

    git checkout "$ORIGINAL_BRANCH" --quiet 2>/dev/null || git checkout - --quiet 2>/dev/null || true
done

# ─── Clean up temp branch ───────────────────────────────────────────────────
git branch -D "_pr-${PR_NUMBER}-temp" 2>/dev/null || true

# ─── Summary ────────────────────────────────────────────────────────────────
echo ""
echo "============================================"
echo "  Backport Rebootstrap Summary"
echo "============================================"
echo "Source PR:       #${PR_NUMBER} (${BASE_BRANCH})"
echo "Released bands:  $(printf '%sxx ' "${RELEASED_BANDS[@]}")"
echo ""
printf "%-30s %-12s %s\n" "Target Branch" "Status" "Details"
printf "%-30s %-12s %s\n" "─────────────" "──────" "───────"
for result in "${RESULTS[@]}"; do
    IFS='|' read -r branch status details <<< "$result"
    printf "%-30s %-12s %s\n" "$branch" "$status" "$details"
done
echo ""

# Return to original branch
cd "$DOTNET_REPO_PATH"
git checkout "$ORIGINAL_BRANCH" --quiet 2>/dev/null || true
