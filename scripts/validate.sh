#!/usr/bin/env bash

# Keep this script behaviorally aligned with scripts/validate.ps1.
# When validation scenarios or expectations change, update both scripts together.

set -euo pipefail

configuration="Debug"
keep_artifacts=0
skip_build=0

usage() {
    cat <<'EOF'
Usage: ./scripts/validate.sh [options]

Options:
  --configuration <value>   Build configuration: Debug or Release. Default: Debug
  --keep-artifacts          Preserve the validation scratch directory
  --skip-build              Skip restore/build and use existing output
  --help                    Show this help text
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --configuration)
            configuration="${2:?missing value for --configuration}"
            shift 2
            ;;
        --configuration=*)
            configuration="${1#*=}"
            shift
            ;;
        --keep-artifacts)
            keep_artifacts=1
            shift
            ;;
        --skip-build)
            skip_build=1
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown argument: %s\n\n' "$1" >&2
            usage >&2
            exit 1
            ;;
    esac
done

case "$configuration" in
    Debug|Release)
        ;;
    *)
        printf 'Unsupported configuration: %s\n' "$configuration" >&2
        exit 1
        ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
project_path="$repo_root/src/graft/graft.csproj"
nuget_config_path="$repo_root/NuGet.config"
dotnet_exe="$(command -v dotnet)"
git_exe="$(command -v git)"
scratch_root="$(mktemp -d "${TMPDIR:-/tmp}/graft-validation-XXXXXX")"
assembly_path="$repo_root/src/graft/bin/$configuration/net10.0/graft.dll"
dotnet_home="$repo_root/.dotnet"

original_path="${PATH:-}"
original_dotnet_cli_home="${DOTNET_CLI_HOME-__UNSET__}"
original_dotnet_skip_first_time="${DOTNET_SKIP_FIRST_TIME_EXPERIENCE-__UNSET__}"
original_dotnet_telemetry_opt_out="${DOTNET_CLI_TELEMETRY_OPTOUT-__UNSET__}"

declare -a results_names=()
declare -a results_passed=()
declare -a results_messages=()
has_failures=0
scenario_error=""

restore_environment() {
    PATH="$original_path"
    export PATH

    if [[ "$original_dotnet_cli_home" == "__UNSET__" ]]; then
        unset DOTNET_CLI_HOME
    else
        DOTNET_CLI_HOME="$original_dotnet_cli_home"
        export DOTNET_CLI_HOME
    fi

    if [[ "$original_dotnet_skip_first_time" == "__UNSET__" ]]; then
        unset DOTNET_SKIP_FIRST_TIME_EXPERIENCE
    else
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE="$original_dotnet_skip_first_time"
        export DOTNET_SKIP_FIRST_TIME_EXPERIENCE
    fi

    if [[ "$original_dotnet_telemetry_opt_out" == "__UNSET__" ]]; then
        unset DOTNET_CLI_TELEMETRY_OPTOUT
    else
        DOTNET_CLI_TELEMETRY_OPTOUT="$original_dotnet_telemetry_opt_out"
        export DOTNET_CLI_TELEMETRY_OPTOUT
    fi
}

cleanup() {
    restore_environment

    if [[ $keep_artifacts -eq 1 || $has_failures -eq 1 ]]; then
        if [[ -d "$scratch_root" ]]; then
            printf 'Validation scratch directory preserved at %s\n' "$scratch_root"
        fi
        return
    fi

    if [[ -d "$scratch_root" ]]; then
        rm -rf "$scratch_root"
    fi
}

trap cleanup EXIT

fail() {
    scenario_error="$1"
    return 1
}

assert_true() {
    local condition="$1"
    local message="$2"

    if [[ "$condition" != "0" ]]; then
        fail "$message"
        return 1
    fi
}

assert_equals() {
    local actual="$1"
    local expected="$2"
    local message="$3"

    if [[ "$actual" != "$expected" ]]; then
        fail "$message"
        return 1
    fi
}

assert_contains() {
    local haystack="$1"
    local needle="$2"
    local message="$3"

    if [[ "$haystack" != *"$needle"* ]]; then
        fail "$message"
        return 1
    fi
}

assert_not_contains() {
    local haystack="$1"
    local needle="$2"
    local message="$3"

    if [[ "$haystack" == *"$needle"* ]]; then
        fail "$message"
        return 1
    fi
}

assert_path_missing() {
    local path="$1"
    local message="$2"

    if [[ -e "$path" ]]; then
        fail "$message"
        return 1
    fi
}

normalize_whitespace() {
    printf '%s' "$1" | tr '\r\n\t' '   ' | sed 's/  */ /g; s/^ //; s/ $//'
}

invoke_git() {
    local working_directory="$1"
    shift

    local output
    local exit_code

    set +e
    output="$("$git_exe" -C "$working_directory" "$@" 2>&1)"
    exit_code=$?
    set -e

    if [[ $exit_code -ne 0 ]]; then
        fail "git $* failed with exit code $exit_code.
$output"
        return 1
    fi

    printf '%s' "$output"
}

invoke_graft() {
    local working_directory="$1"
    local expected_exit_code="$2"
    shift 2

    local output
    local exit_code

    set +e
    output="$(
        cd "$working_directory" &&
        "$dotnet_exe" "$assembly_path" "$@" 2>&1
    )"
    exit_code=$?
    set -e

    if [[ $exit_code -ne $expected_exit_code ]]; then
        fail "graft $* exited with $exit_code, expected $expected_exit_code.
$output"
        return 1
    fi

    printf '%s' "$output"
}

invoke_graft_with_input() {
    local working_directory="$1"
    local expected_exit_code="$2"
    local input_text="$3"
    shift 3

    local output
    local exit_code

    set +e
    output="$(
        cd "$working_directory" &&
        printf '%b' "$input_text" | "$dotnet_exe" "$assembly_path" "$@" 2>&1
    )"
    exit_code=$?
    set -e

    if [[ $exit_code -ne $expected_exit_code ]]; then
        fail "graft $* exited with $exit_code, expected $expected_exit_code.
$output"
        return 1
    fi

    printf '%s' "$output"
}

new_repository() {
    local name="$1"
    local path="$scratch_root/$name"

    mkdir -p "$path"

    invoke_git "$path" init --quiet -b main >/dev/null || return 1
    invoke_git "$path" config user.email graft@example.com >/dev/null || return 1
    invoke_git "$path" config user.name "graft validation" >/dev/null || return 1

    printf 'base\n' > "$path/README.txt"
    invoke_git "$path" add . >/dev/null || return 1
    invoke_git "$path" commit --quiet -m init >/dev/null || return 1

    printf '%s' "$path"
}

get_git_sha() {
    local working_directory="$1"
    local reference="$2"

    invoke_git "$working_directory" rev-parse "$reference"
}

get_worktree_path() {
    local working_directory="$1"
    local branch_name="$2"
    local output
    local path

    output="$(invoke_git "$working_directory" worktree list --porcelain)" || return 1

    path="$(
        printf '%s\n' "$output" | awk -v branch="$branch_name" '
            BEGIN {
                path = ""
                current_branch = ""
            }
            /^worktree / {
                path = substr($0, 10)
                next
            }
            /^branch refs\/heads\// {
                current_branch = substr($0, 19)
                next
            }
            /^$/ {
                if (current_branch == branch) {
                    print path
                    found = 1
                    exit 0
                }

                path = ""
                current_branch = ""
            }
            END {
                if (current_branch == branch) {
                    print path
                    exit 0
                }

                exit(found ? 0 : 1)
            }'
    )"

    if [[ -z "$path" ]]; then
        fail "Could not find worktree path for branch '$branch_name'."
        return 1
    fi

    printf '%s' "$path"
}

get_hash_prefix() {
    local value="$1"
    local hex_length="$2"

    printf '%s' "$value" | sha256sum | cut -c1-"$hex_length"
}

get_expected_managed_folder_name() {
    local repository_name="$1"
    local branch_name="$2"
    local repo_hash
    local branch_hash

    repo_hash="$(get_hash_prefix "$repository_name" 8)"
    branch_hash="$(get_hash_prefix "$branch_name" 8)"
    printf '%s%s' "$repo_hash" "$branch_hash"
}

run_scenario() {
    local name="$1"
    local function_name="$2"

    printf 'Running scenario: %s\n' "$name"
    scenario_error=""

    if "$function_name"; then
        results_names+=("$name")
        results_passed+=("1")
        results_messages+=("ok")
    else
        results_names+=("$name")
        results_passed+=("0")
        results_messages+=("$scenario_error")
        has_failures=1
    fi
}

scenario_create_uses_head_by_default_and_main_when_requested() {
    local repo
    local main_sha
    local dev_sha
    local default_output
    local default_sha
    local from_main_output
    local from_main_sha

    repo="$(new_repository "create-local")" || return 1

    invoke_git "$repo" switch --quiet -c dev >/dev/null || return 1
    invoke_git "$repo" commit --quiet --allow-empty -m dev >/dev/null || return 1

    main_sha="$(get_git_sha "$repo" main)" || return 1
    dev_sha="$(get_git_sha "$repo" HEAD)" || return 1

    default_output="$(invoke_graft "$repo" 0 create feature/default-head)" || return 1
    default_sha="$(get_git_sha "$repo" refs/heads/feature/default-head)" || return 1
    assert_equals "$default_sha" "$dev_sha" "Expected default create to use the current HEAD." || return 1
    assert_contains "$default_output" "wt.exe was not found" "Expected validation runs to surface the missing wt.exe warning." || return 1

    from_main_output="$(invoke_graft "$repo" 0 create feature/from-main -l)" || return 1
    from_main_sha="$(get_git_sha "$repo" refs/heads/feature/from-main)" || return 1
    assert_equals "$from_main_sha" "$main_sha" "Expected --from-local-main to use local main." || return 1
    assert_contains "$from_main_output" "Creating new branch 'feature/from-main' from main..." "Expected create output to mention main as the selected base." || return 1
}

scenario_create_resolves_origin_main_and_validates_flag_combinations() {
    local repo
    local main_sha
    local origin_main_sha
    local from_origin_output
    local from_origin_sha
    local fallback_output
    local fallback_sha
    local warning_output
    local normalized_warning_output
    local parse_failure_output
    local normalized_parse_failure_output

    repo="$(new_repository "create-origin-source")" || return 1
    main_sha="$(get_git_sha "$repo" main)" || return 1

    invoke_git "$repo" update-ref refs/remotes/origin/main "$main_sha" >/dev/null || return 1
    invoke_git "$repo" switch --quiet -c dev main >/dev/null || return 1
    invoke_git "$repo" branch -D main >/dev/null || return 1

    origin_main_sha="$(get_git_sha "$repo" refs/remotes/origin/main)" || return 1

    from_origin_output="$(invoke_graft "$repo" 0 create feature/from-origin -o)" || return 1
    from_origin_sha="$(get_git_sha "$repo" refs/heads/feature/from-origin)" || return 1
    assert_equals "$from_origin_sha" "$origin_main_sha" "Expected --from-origin-main / -o to use origin/main." || return 1
    assert_contains "$from_origin_output" "from origin/main" "Expected create output to mention origin/main." || return 1

    fallback_output="$(invoke_graft "$repo" 0 create feature/from-main-fallback --from-local-main)" || return 1
    fallback_sha="$(get_git_sha "$repo" refs/heads/feature/from-main-fallback)" || return 1
    assert_equals "$fallback_sha" "$origin_main_sha" "Expected --from-local-main to fall back to origin/main." || return 1
    assert_contains "$fallback_output" "from origin/main" "Expected fallback output to mention origin/main." || return 1

    invoke_git "$repo" branch feature/existing origin/main >/dev/null || return 1
    warning_output="$(invoke_graft "$repo" 0 create feature/existing --from-local-main)" || return 1
    normalized_warning_output="$(normalize_whitespace "$warning_output")"
    assert_contains "$normalized_warning_output" "Ignoring --from-local-main because branch 'feature/existing' already exists." "Expected existing-branch create to warn when --from-local-main is ignored." || return 1

    parse_failure_output="$(invoke_graft "$repo" 1 create feature/invalid -l -o)" || return 1
    normalized_parse_failure_output="$(normalize_whitespace "$parse_failure_output")"
    assert_contains "$normalized_parse_failure_output" "--from-local-main and --from-origin-main cannot be used together." "Expected mutual exclusion validation for create flags." || return 1
}

scenario_create_uses_short_fixed_length_managed_worktree_folder_names() {
    local repo_name
    local repo
    local branch_name
    local worktree_path
    local directory_name
    local expected_directory_name
    local list_output

    repo_name="This.Is.A.LongRepo"
    repo="$(new_repository "$repo_name")" || return 1

    invoke_git "$repo" switch --quiet -c dev >/dev/null || return 1
    invoke_git "$repo" commit --quiet --allow-empty -m dev >/dev/null || return 1

    branch_name="feature/segment-segment-segment-segment-segment-segment-segment-segment-segment-segment"
    invoke_graft "$repo" 0 create "$branch_name" >/dev/null || return 1

    worktree_path="$(get_worktree_path "$repo" "$branch_name")" || return 1
    directory_name="$(basename "$worktree_path")"
    expected_directory_name="$(get_expected_managed_folder_name "$repo_name" "$branch_name")"

    assert_true "$([[ "$directory_name" == "$expected_directory_name" ]]; printf '%s' "$?")" "Expected the managed worktree directory name to use the short hash-based format." || return 1
    assert_true "$(( ${#directory_name} == ${#expected_directory_name} ? 0 : 1 ))" "Expected the managed worktree directory name to stay fixed-length." || return 1

    list_output="$(invoke_graft "$repo" 0 list)" || return 1
    list_output="$(printf '%s' "$list_output" | tr -d '[:space:]')"
    assert_contains "$list_output" "$branch_name" "Expected list output to preserve the full branch name with fixed-length folder names." || return 1

    invoke_graft "$repo" 0 remove "$branch_name" >/dev/null || return 1
    assert_path_missing "$worktree_path" "Expected remove to delete a worktree created with a fixed-length folder name." || return 1
}

scenario_create_leaves_room_for_deep_paths_inside_the_worktree() {
    local repo
    local relative_path
    local source_file_path
    local branch_name
    local worktree_path
    local worktree_file_path

    repo="$(new_repository "deep-path")" || return 1

    relative_path="segment-01-padding/segment-02-padding/segment-03-padding/segment-04-padding/segment-05-padding/segment-06-padding/segment-07-padding/deep-file.txt"
    source_file_path="$repo/$relative_path"
    mkdir -p "$(dirname "$source_file_path")"
    printf 'deep\n' > "$source_file_path"
    invoke_git "$repo" add . >/dev/null || return 1
    invoke_git "$repo" commit --quiet -m "add deep file" >/dev/null || return 1

    invoke_git "$repo" switch --quiet -c dev >/dev/null || return 1
    invoke_git "$repo" commit --quiet --allow-empty -m dev >/dev/null || return 1

    branch_name="feature/deep-deep-deep-deep-deep-deep-deep-deep-deep-deep-deep-deep"
    invoke_graft "$repo" 0 create "$branch_name" >/dev/null || return 1

    worktree_path="$(get_worktree_path "$repo" "$branch_name")" || return 1
    worktree_file_path="$worktree_path/$relative_path"

    assert_true "$([[ -f "$worktree_file_path" ]]; printf '%s' "$?")" "Expected create to populate deep tracked files inside the new worktree." || return 1
    assert_true "$(( ${#worktree_file_path} < 260 ? 0 : 1 ))" "Expected the fixed-length worktree path to leave room for a deep tracked file path." || return 1
}

scenario_list_and_remove_manage_created_worktrees() {
    local repo
    local list_output
    local remove_path
    local dirty_path

    repo="$(new_repository "list-remove")" || return 1

    invoke_git "$repo" switch --quiet -c dev >/dev/null || return 1
    invoke_git "$repo" commit --quiet --allow-empty -m dev >/dev/null || return 1

    invoke_graft "$repo" 0 create feature/listable >/dev/null || return 1
    invoke_graft "$repo" 0 create feature/remove-me >/dev/null || return 1
    invoke_graft "$repo" 0 create feature/remove-dirty >/dev/null || return 1

    list_output="$(invoke_graft "$repo" 0 list)" || return 1
    assert_contains "$list_output" "feature/listable" "Expected list output to include feature/listable." || return 1
    assert_contains "$list_output" "feature/remove-me" "Expected list output to include feature/remove-me." || return 1
    assert_contains "$list_output" "yes" "Expected list output to mark managed worktrees." || return 1

    remove_path="$(get_worktree_path "$repo" feature/remove-me)" || return 1
    invoke_graft "$repo" 0 remove feature/remove-me >/dev/null || return 1
    assert_path_missing "$remove_path" "Expected remove to delete the selected worktree path." || return 1

    dirty_path="$(get_worktree_path "$repo" feature/remove-dirty)" || return 1
    printf 'dirty\n' > "$dirty_path/dirty.txt"
    invoke_graft "$repo" 0 remove feature/remove-dirty --force >/dev/null || return 1
    assert_path_missing "$dirty_path" "Expected force remove to delete a dirty worktree path." || return 1
}

scenario_cleanup_removes_all_candidates_non_interactively() {
    local repo
    local cleanup_output
    local worktree_list

    repo="$(new_repository "cleanup")" || return 1

    invoke_git "$repo" switch --quiet -c dev >/dev/null || return 1
    invoke_git "$repo" commit --quiet --allow-empty -m dev >/dev/null || return 1

    invoke_graft "$repo" 0 create feature/cleanup-one >/dev/null || return 1
    invoke_graft "$repo" 0 create feature/cleanup-two >/dev/null || return 1

    cleanup_output="$(invoke_graft "$repo" 0 cleanup --all --yes)" || return 1
    assert_contains "$cleanup_output" "Removed worktree:" "Expected cleanup to report removed worktrees." || return 1

    worktree_list="$(invoke_git "$repo" worktree list --porcelain)" || return 1
    assert_not_contains "$worktree_list" "feature/cleanup-one" "Expected cleanup to remove feature/cleanup-one." || return 1
    assert_not_contains "$worktree_list" "feature/cleanup-two" "Expected cleanup to remove feature/cleanup-two." || return 1
}

scenario_prune_removes_stale_worktree_metadata() {
    local repo
    local prune_path
    local worktree_list

    repo="$(new_repository "prune")" || return 1

    invoke_git "$repo" switch --quiet -c dev >/dev/null || return 1
    invoke_git "$repo" commit --quiet --allow-empty -m dev >/dev/null || return 1

    invoke_graft "$repo" 0 create feature/prune-me >/dev/null || return 1

    prune_path="$(get_worktree_path "$repo" feature/prune-me)" || return 1
    rm -rf "$prune_path"

    invoke_graft "$repo" 0 prune >/dev/null || return 1
    worktree_list="$(invoke_git "$repo" worktree list --porcelain)" || return 1
    assert_not_contains "$worktree_list" "feature/prune-me" "Expected prune to remove stale worktree metadata." || return 1
}

scenario_navigate_works_from_repo_and_shared_root() {
    local repo
    local shared_root
    local managed_root
    local unreadable_path
    local navigate_from_repo_output
    local navigate_from_shared_root_output

    repo="$(new_repository "navigate")" || return 1

    invoke_git "$repo" switch --quiet -c dev >/dev/null || return 1
    invoke_git "$repo" commit --quiet --allow-empty -m dev >/dev/null || return 1

    invoke_graft "$repo" 0 create feature/navigate >/dev/null || return 1

    navigate_from_repo_output="$(invoke_graft_with_input "$repo" 0 "1\n" navigate)" || return 1
    assert_contains "$navigate_from_repo_output" "wt.exe was not found" "Expected navigate from repo mode to open the selected worktree." || return 1

    shared_root="$(dirname "$repo")"
    managed_root="$shared_root/.worktrees"
    mkdir -p "$managed_root/invalid-entry"

    unreadable_path="$managed_root/unreadable-entry"
    mkdir -p "$unreadable_path"
    chmod 000 "$unreadable_path"

    navigate_from_shared_root_output="$(invoke_graft_with_input "$shared_root" 0 "1\n" navigate)" || {
        chmod 700 "$unreadable_path"
        return 1
    }

    chmod 700 "$unreadable_path"

    assert_contains "$navigate_from_shared_root_output" "Reading managed worktrees..." "Expected navigate shared-root mode to read managed worktrees." || return 1
    assert_contains "$navigate_from_shared_root_output" "wt.exe was not found" "Expected navigate shared-root mode to open the selected worktree." || return 1
}

append_unique_path_entry() {
    local entry="$1"
    local existing

    [[ -n "$entry" ]] || return 0
    for existing in "${validation_path_entries[@]:-}"; do
        if [[ "$existing" == "$entry" ]]; then
            return 0
        fi
    done

    validation_path_entries+=("$entry")
}

find_wt_directories() {
    local path_entry
    local -a path_entries=()

    IFS=':' read -r -a path_entries <<< "$PATH"
    for path_entry in "${path_entries[@]}"; do
        [[ -n "$path_entry" ]] || continue
        if [[ -x "$path_entry/wt.exe" || -x "$path_entry/wt" ]]; then
            wt_directories+=("$path_entry")
        fi
    done
}

is_wt_directory() {
    local candidate="$1"
    local directory

    for directory in "${wt_directories[@]:-}"; do
        if [[ "$directory" == "$candidate" ]]; then
            return 0
        fi
    done

    return 1
}

DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
DOTNET_CLI_HOME="$dotnet_home"
DOTNET_CLI_TELEMETRY_OPTOUT="1"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE DOTNET_CLI_HOME DOTNET_CLI_TELEMETRY_OPTOUT

mkdir -p "$scratch_root"

if [[ $skip_build -eq 0 ]]; then
    "$dotnet_exe" restore "$project_path" -nologo --configfile "$nuget_config_path" -p:RuntimeIdentifiers=
    "$dotnet_exe" build "$project_path" -c "$configuration" -nologo --configfile "$nuget_config_path" --no-restore -p:RuntimeIdentifiers=
fi

if [[ ! -f "$assembly_path" ]]; then
    printf 'Expected build output at %s.\n' "$assembly_path" >&2
    exit 1
fi

declare -a wt_directories=()
declare -a validation_path_entries=()
find_wt_directories

append_unique_path_entry "$(dirname "$dotnet_exe")"
append_unique_path_entry "$(dirname "$git_exe")"

IFS=':' read -r -a original_path_entries <<< "$original_path"
for path_entry in "${original_path_entries[@]}"; do
    if [[ -n "$path_entry" ]] && ! is_wt_directory "$path_entry"; then
        append_unique_path_entry "$path_entry"
    fi
done

PATH="$(IFS=:; printf '%s' "${validation_path_entries[*]}")"
export PATH

run_scenario "create uses HEAD by default and main when requested" scenario_create_uses_head_by_default_and_main_when_requested
run_scenario "create resolves origin/main and validates flag combinations" scenario_create_resolves_origin_main_and_validates_flag_combinations
run_scenario "create uses short fixed-length managed worktree folder names" scenario_create_uses_short_fixed_length_managed_worktree_folder_names
run_scenario "create leaves room for deep paths inside the worktree" scenario_create_leaves_room_for_deep_paths_inside_the_worktree
run_scenario "list and remove manage created worktrees" scenario_list_and_remove_manage_created_worktrees
run_scenario "cleanup removes all candidates non-interactively" scenario_cleanup_removes_all_candidates_non_interactively
run_scenario "prune removes stale worktree metadata" scenario_prune_removes_stale_worktree_metadata
run_scenario "navigate works from repo and shared root" scenario_navigate_works_from_repo_and_shared_root

printf '\nValidation summary:\n'
for index in "${!results_names[@]}"; do
    if [[ "${results_passed[$index]}" == "1" ]]; then
        status="PASS"
    else
        status="FAIL"
    fi

    printf '[%s] %s\n' "$status" "${results_names[$index]}"
    if [[ "${results_passed[$index]}" != "1" ]]; then
        printf '%s\n' "${results_messages[$index]}"
    fi
done

if [[ $has_failures -eq 1 ]]; then
    printf '\nValidation failed. Preserving scratch directory at %s.\n' "$scratch_root" >&2
    exit 1
fi

printf '\nAll validation scenarios passed.\n'
