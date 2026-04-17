#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Auto-detect Unity: pick the oldest installed version from Unity Hub
find_unity() {
    local hub_dir="/Applications/Unity/Hub/Editor"
    if [ ! -d "$hub_dir" ]; then
        return 1
    fi
    # Sort versions and pick the oldest
    local oldest
    oldest=$(ls -1 "$hub_dir" | sort -V | head -1)
    if [ -z "$oldest" ]; then
        return 1
    fi
    echo "$hub_dir/$oldest/Unity.app/Contents/MacOS/Unity"
}

if [ -z "$UNITY_PATH" ]; then
    UNITY_PATH=$(find_unity)
    if [ -z "$UNITY_PATH" ] || [ ! -x "$UNITY_PATH" ]; then
        echo "Error: Could not find Unity installation. Set UNITY_PATH manually."
        exit 1
    fi
fi

PROJECT_PATH="${PROJECT_PATH:-$ROOT/Samples~/Meet}"

usage() {
    echo "Usage: $0 <command> [options]"
    echo ""
    echo "Commands:"
    echo "  test    Run Unity tests"
    echo "  build   Build Unity project"
    echo ""
    echo "Run '$0 <command> --help' for command-specific options."
    echo ""
    echo "Environment variables:"
    echo "  UNITY_PATH    Path to Unity binary (default: auto-detect latest from Unity Hub)"
    echo "  PROJECT_PATH  Path to Unity project (default: Samples~/Meet)"
}

usage_test() {
    echo "Usage: $0 test [options]"
    echo ""
    echo "Options:"
    echo "  -f, --filter FILTER      Unity test filter (passed to -testFilter)"
    echo "  -m, --mode MODE          Test mode: EditMode, PlayMode, or both (default: both)"
    echo "  -n, --iterations N       Run the test set N times, counting pass/fail per run (default: 1)"
    echo "  -h, --help               Show this help"
    echo ""
    echo "Environment variables:"
    echo "  OUTPUT_DIR    Directory for test results (default: Logs~)"
}

usage_build() {
    echo "Usage: $0 build <platform>"
    echo ""
    echo "Platforms:"
    echo "  macos     Build for macOS (StandaloneOSX)"
    echo "  ios       Build for iOS"
    echo "  android   Build for Android"
    echo ""
    echo "Options:"
    echo "  -h, --help   Show this help"
}

cmd_test() {
    local test_filter=""
    local modes="EditMode PlayMode"
    local iterations=1

    while [[ $# -gt 0 ]]; do
        case $1 in
            -f|--filter) test_filter="$2"; shift 2 ;;
            -m|--mode) modes="$2"; shift 2 ;;
            -n|--iterations) iterations="$2"; shift 2 ;;
            -h|--help) usage_test; exit 0 ;;
            *) echo "Unknown option: $1"; usage_test; exit 1 ;;
        esac
    done

    if ! [[ "$iterations" =~ ^[0-9]+$ ]] || [ "$iterations" -lt 1 ]; then
        echo "Error: --iterations must be a positive integer (got: $iterations)"
        exit 1
    fi

    local output_dir="${OUTPUT_DIR:-$ROOT/Logs~}"
    local unity_log="$HOME/Library/Logs/Unity/Editor.log"

    echo "Unity: $UNITY_PATH"
    mkdir -p "$output_dir"

    local overall_exit=0

    for mode in $modes; do
        local mode_passed=0
        local mode_failed=0

        for (( i=1; i<=iterations; i++ )); do
            local xml_path html_path
            if [ "$iterations" -gt 1 ]; then
                xml_path="$output_dir/${mode}-test-results-${i}.xml"
                html_path="$output_dir/${mode}-test-results-${i}.html"
            else
                xml_path="$output_dir/${mode}-test-results.xml"
                html_path="$output_dir/${mode}-test-results.html"
            fi

            local unity_args=(-runTests -projectPath "$PROJECT_PATH" -batchmode -testPlatform "$mode" -testResults "$xml_path")
            if [ -n "$test_filter" ]; then
                unity_args+=(-testFilter "$test_filter")
            fi

            echo "========================================="
            if [ "$iterations" -gt 1 ]; then
                echo "Running $mode tests (iteration $i/$iterations)..."
            else
                echo "Running $mode tests..."
            fi
            if [ -n "$test_filter" ]; then
                echo "Filter: $test_filter"
            fi
            echo "========================================="
            "$UNITY_PATH" "${unity_args[@]}"
            local test_exit=$?

            if [ ! -f "$xml_path" ]; then
                echo "$mode iter $i: ERROR - test results XML not found at $xml_path"
                echo "Unity editor log: $unity_log"
                mode_failed=$(( mode_failed + 1 ))
                continue
            fi

            if [ $test_exit -eq 0 ]; then
                mode_passed=$(( mode_passed + 1 ))
                if [ "$iterations" -gt 1 ]; then
                    echo "$mode iter $i: PASSED (pass: $mode_passed, fail: $mode_failed)"
                else
                    echo "$mode: PASSED"
                fi
            else
                mode_failed=$(( mode_failed + 1 ))
                if [ "$iterations" -gt 1 ]; then
                    echo "$mode iter $i: FAILED (pass: $mode_passed, fail: $mode_failed)"
                else
                    echo "$mode: FAILED"
                fi
            fi

            # Print failure details to console
            python3 "$SCRIPT_DIR/unity_test_results_utils.py" "$xml_path" -f console

            # Generate HTML report
            python3 "$SCRIPT_DIR/unity_test_results_utils.py" "$xml_path" -o "$html_path"
            echo ""
        done

        if [ "$iterations" -gt 1 ]; then
            echo "-----------------------------------------"
            echo "$mode summary: $mode_passed/$iterations passed, $mode_failed failed"
            echo "-----------------------------------------"
            echo ""
        fi

        if [ $mode_failed -gt 0 ]; then
            overall_exit=1
        fi
    done

    echo "========================================="
    if [ $overall_exit -eq 0 ]; then
        echo "Overall: ALL TESTS PASSED"
    else
        echo "Overall: SOME TESTS FAILED"
        echo "Unity editor log: $unity_log"
    fi
    echo "========================================="

    exit $overall_exit
}

cmd_build() {
    if [[ $# -eq 0 || "$1" == "-h" || "$1" == "--help" ]]; then
        usage_build
        exit 0
    fi

    local platform="$1"
    shift

    while [[ $# -gt 0 ]]; do
        case $1 in
            -h|--help) usage_build; exit 0 ;;
            *) echo "Unknown option: $1"; usage_build; exit 1 ;;
        esac
    done

    local build_target execute_method
    case "$platform" in
        macos)
            build_target="OSXUniversal"
            execute_method="LiveKit.Editor.BuildScript.BuildMac"
            ;;
        ios)
            build_target="iOS"
            execute_method="LiveKit.Editor.BuildScript.BuildIOS"
            ;;
        android)
            build_target="Android"
            execute_method="LiveKit.Editor.BuildScript.BuildAndroid"
            ;;
        *)
            echo "Error: Unknown platform '$platform'"
            echo ""
            usage_build
            exit 1
            ;;
    esac

    echo "Unity: $UNITY_PATH"
    echo "========================================="
    echo "Building for $platform..."
    echo "========================================="

    "$UNITY_PATH" \
        -quit -batchmode \
        -projectPath "$PROJECT_PATH" \
        -buildTarget "$build_target" \
        -executeMethod "$execute_method" \
        -logFile -

    local exit_code=$?

    echo "========================================="
    if [ $exit_code -eq 0 ]; then
        echo "Build SUCCEEDED for $platform"
    else
        echo "Build FAILED for $platform (exit code: $exit_code)"
    fi
    echo "========================================="

    exit $exit_code
}

# Main
COMMAND="${1:-}"
shift 2>/dev/null || true

case "$COMMAND" in
    test)  cmd_test "$@" ;;
    build) cmd_build "$@" ;;
    -h|--help) usage; exit 0 ;;
    "")    usage; exit 0 ;;
    *)     echo "Unknown command: $COMMAND"; echo ""; usage; exit 1 ;;
esac
