#!/usr/bin/env bash

set -euo pipefail

configuration="Release"
runtime_identifier=""
version_suffix=""

usage() {
    cat <<'EOF'
Usage: ./scripts/publish.sh [options]

Options:
  --configuration <value>      Build configuration. Default: Release
  --runtime-identifier <rid>   Windows runtime identifier: win-x64 or win-arm64
  --version-suffix <value>     Optional prerelease suffix, for example: dev
  --help                       Show this help text
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
        --runtime-identifier)
            runtime_identifier="${2:?missing value for --runtime-identifier}"
            shift 2
            ;;
        --runtime-identifier=*)
            runtime_identifier="${1#*=}"
            shift
            ;;
        --version-suffix)
            version_suffix="${2:?missing value for --version-suffix}"
            shift 2
            ;;
        --version-suffix=*)
            version_suffix="${1#*=}"
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

case "$runtime_identifier" in
    "" )
        architecture="$(uname -m)"
        case "$architecture" in
            arm64|aarch64)
                runtime_identifier="win-arm64"
                ;;
            *)
                runtime_identifier="win-x64"
                ;;
        esac
        ;;
    win-x64|win-arm64)
        ;;
    *)
        printf 'Unsupported runtime identifier: %s\n' "$runtime_identifier" >&2
        exit 1
        ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
project_path="$repo_root/src/graft/graft.csproj"
publish_dir="$repo_root/artifacts/publish/graft/$runtime_identifier"

convert_windows_path() {
    local windows_path="$1"

    if command -v cygpath >/dev/null 2>&1; then
        cygpath -u "$windows_path"
        return
    fi

    if command -v wslpath >/dev/null 2>&1; then
        wslpath "$windows_path"
        return
    fi

    printf '%s' "$windows_path"
}

resolve_userprofile() {
    if [[ -n "${USERPROFILE:-}" ]]; then
        if [[ "$USERPROFILE" == /* ]]; then
            printf '%s' "$USERPROFILE"
        else
            convert_windows_path "$USERPROFILE"
        fi
        return
    fi

    if command -v cmd.exe >/dev/null 2>&1; then
        local windows_profile
        windows_profile="$(cmd.exe /C echo %USERPROFILE% 2>/dev/null | tr -d '\r')"
        if [[ -n "$windows_profile" ]]; then
            convert_windows_path "$windows_profile"
            return
        fi
    fi

    printf 'USERPROFILE is not set and could not be resolved automatically.\n' >&2
    exit 1
}

user_profile="$(resolve_userprofile)"
install_dir="$user_profile/.graft/bin"
install_path="$install_dir/graft.exe"

publish_args=(
    publish
    "$project_path"
    -c "$configuration"
    -r "$runtime_identifier"
    --self-contained true
    /p:PublishSingleFile=true
    --output "$publish_dir"
)

if [[ -n "$version_suffix" ]]; then
    publish_args+=("/p:VersionSuffix=$version_suffix")
fi

dotnet "${publish_args[@]}"

mkdir -p "$install_dir"
cp "$publish_dir/graft.exe" "$install_path"

printf 'Current user profile: %s\n' "$user_profile"
printf 'Published runtime: %s\n' "$runtime_identifier"
if [[ -n "$version_suffix" ]]; then
    printf 'Published version suffix: %s\n' "$version_suffix"
fi
printf 'Installed graft to %s\n' "$install_path"
