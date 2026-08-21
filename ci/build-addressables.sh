#!/usr/bin/env bash
#
# Builds the addressable content headlessly and exits nonzero if it failed.
#
# Minigame.Chests loads from a remote path, so a player build no longer carries that content: it
# has to be built here and served from wherever RemoteLoadPath points. Core is local and comes out
# of the same build.
#
# Deliberately free of any CI provider, the same shape as run-tests.sh: a pipeline only has to
# check out the repo, supply a licensed Unity, and call this.
#
# Usage:
#   ci/build-addressables.sh
#
# Environment:
#   UNITY         path to the Unity binary; auto-detected from the Hub layout if unset
#   RESULTS_DIR   where the editor log lands; defaults to <project>/ci-results
#
# Output lands under ServerData/<BuildTarget>/ (remote groups and the remote catalog) and under
# Library/com.unity.addressables/aa/<platform>/ (local groups). Serve the first over HTTP:
#
#   python3 -m http.server 8080 --directory ServerData
#
# The editor must not be holding the project lock. Batch mode fails outright if it is.

set -uo pipefail

PROJECT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RESULTS_DIR="${RESULTS_DIR:-$PROJECT_PATH/ci-results}"
LOG="$RESULTS_DIR/Addressables.log"

UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT_PATH/ProjectSettings/ProjectVersion.txt" | tr -d '\r\n')"
if [[ -z "$UNITY_VERSION" ]]; then
    echo "Could not read the editor version from ProjectSettings/ProjectVersion.txt" >&2
    exit 1
fi

find_unity() {
    if [[ -n "${UNITY:-}" ]]; then
        echo "$UNITY"
        return
    fi

    local candidates=(
        "/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
        "$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"
        "/opt/unity/editors/$UNITY_VERSION/Editor/Unity"
        "/opt/unity/Editor/Unity"
    )

    local candidate
    for candidate in "${candidates[@]}"; do
        [[ -x "$candidate" ]] && echo "$candidate" && return
    done
}

UNITY_BIN="$(find_unity)"
if [[ -z "$UNITY_BIN" || ! -x "$UNITY_BIN" ]]; then
    echo "No Unity $UNITY_VERSION binary found. Set UNITY to its path." >&2
    exit 1
fi

mkdir -p "$RESULTS_DIR"

echo "==> Addressables content"

# No -quit alongside -executeMethod: the method exits the editor itself, with the code that says
# whether the build actually succeeded rather than whether the editor managed to start.
"$UNITY_BIN" -batchmode -nographics -projectPath "$PROJECT_PATH" \
    -executeMethod Company.ChestGame.Editor.AddressablesContentBuild.BuildFromCommandLine \
    -logFile "$LOG"
status=$?

if [[ $status -ne 0 ]]; then
    echo "    build failed; see $LOG"
    echo "FAILED"
    exit $status
fi

echo "    built ($LOG)"
if [[ -d "$PROJECT_PATH/ServerData" ]]; then
    echo "    remote content under ServerData/:"
    find "$PROJECT_PATH/ServerData" -type f -exec basename {} \; | sort | sed 's/^/      /'
else
    echo "    no ServerData/ produced; every group is on a local path"
fi

echo "OK"
