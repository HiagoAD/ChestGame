#!/usr/bin/env bash
#
# Runs the EditMode and PlayMode suites headlessly and exits nonzero if either fails.
#
# Deliberately free of any CI provider: a pipeline only has to check out the repo, supply a
# licensed Unity, and call this. Everything provider-specific (licence activation, caching,
# artifact upload) stays outside.
#
# Usage:
#   ci/run-tests.sh                 both suites
#   ci/run-tests.sh EditMode        one suite
#
# Environment:
#   UNITY         path to the Unity binary; auto-detected from the Hub layout if unset
#   RESULTS_DIR   where XML and logs land; defaults to <project>/ci-results
#
# The editor must not be holding the project lock. Batch mode fails outright if it is.

set -uo pipefail

PROJECT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RESULTS_DIR="${RESULTS_DIR:-$PROJECT_PATH/ci-results}"

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

# Pulls the counts off the NUnit <test-run> root so the summary does not depend on reading
# the log. Absent when the run failed before producing results, which is reported as such.
summarize() {
    local xml="$1" attribute="$2"
    [[ -f "$xml" ]] || return
    grep -m1 -o "$attribute=\"[0-9]*\"" "$xml" | head -1 | tr -d "$attribute=\""
}

run_suite() {
    local platform="$1"
    local xml="$RESULTS_DIR/${platform}.xml"
    local log="$RESULTS_DIR/${platform}.log"

    echo "==> $platform"
    "$UNITY_BIN" -batchmode -nographics -projectPath "$PROJECT_PATH" \
        -runTests -testPlatform "$platform" -testResults "$xml" -logFile "$log"
    local status=$?

    local total passed failed
    total="$(summarize "$xml" total)"
    passed="$(summarize "$xml" passed)"
    failed="$(summarize "$xml" failed)"

    if [[ -n "$total" ]]; then
        echo "    $total tests, $passed passed, $failed failed  ($xml)"
    else
        echo "    no results written; see $log"
    fi

    return $status
}

SUITES=("$@")
if [[ ${#SUITES[@]} -eq 0 ]]; then
    SUITES=(EditMode PlayMode)
fi

# Every suite runs even when an earlier one fails, so one invocation reports everything
# broken rather than only the first thing to break.
overall=0
for suite in "${SUITES[@]}"; do
    run_suite "$suite" || overall=1
done

if [[ $overall -ne 0 ]]; then
    echo "FAILED"
else
    echo "OK"
fi
exit $overall
