#!/bin/sh
set -eu

hash_prefix() {
    value="$1"
    length="$2"
    printf '%s' "$value" | sha256sum | cut -c1-"$length"
}

init_repo() {
    path="$1"
    mkdir -p "$path"

    git -C "$path" init --quiet -b main
    git -C "$path" config user.email graft@example.com
    git -C "$path" config user.name "graft validation"

    printf 'base\n' > "$path/README.txt"
    git -C "$path" add .
    git -C "$path" commit --quiet -m init
}

worktree_path() {
    repo="$1"
    branch="$2"

    git -C "$repo" worktree list --porcelain | awk -v branch="$branch" '
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
            if (!found && current_branch == branch) {
                print path
                found = 1
                exit 0
            }

            exit(found ? 0 : 1)
        }'
}

expected_folder_name() {
    repo_name="$1"
    branch_name="$2"

    repo_hash="$(hash_prefix "$repo_name" 8)"
    branch_hash="$(hash_prefix "$branch_name" 8)"
    printf '%s%s\n' "$repo_hash" "$branch_hash"
}

managed_root() {
    printf '%s/.graft/worktrees\n' "$HOME"
}

command="${1:-}"

case "$command" in
    init-repo)
        init_repo "$2"
        ;;
    worktree-path)
        worktree_path "$2" "$3"
        ;;
    expected-folder-name)
        expected_folder_name "$2" "$3"
        ;;
    managed-root)
        managed_root "$2"
        ;;
    *)
        printf 'Usage: graft-test <init-repo|worktree-path|expected-folder-name|managed-root> ...\n' >&2
        exit 1
        ;;
esac
