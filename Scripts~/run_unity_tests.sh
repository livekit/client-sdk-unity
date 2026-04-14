#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/2022.3.20f1/Unity.app/Contents/MacOS/Unity}"
PROJECT_PATH="${PROJECT_PATH:-$SCRIPT_DIR/../Samples~/Meet}"
OUTPUT_DIR="${OUTPUT_DIR:-$HOME/dev/unity/logs}"
UNITY_LOG="$HOME/Library/Logs/Unity/Editor.log"

usage() {
    echo "Usage: $0 [options]"
    echo ""
    echo "Options:"
    echo "  -f, --filter FILTER   Unity test filter (passed to -testFilter)"
    echo "  -m, --mode MODE       Test mode: EditMode, PlayMode, or both (default: both)"
    echo "  -h, --help            Show this help"
    echo ""
    echo "Environment variables:"
    echo "  UNITY_PATH    Path to Unity binary"
    echo "  PROJECT_PATH  Path to Unity project"
    echo "  OUTPUT_DIR    Directory for test results"
}

TEST_FILTER=""
MODES="EditMode PlayMode"

while [[ $# -gt 0 ]]; do
    case $1 in
        -f|--filter) TEST_FILTER="$2"; shift 2 ;;
        -m|--mode) MODES="$2"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1"; usage; exit 1 ;;
    esac
done

mkdir -p "$OUTPUT_DIR"

OVERALL_EXIT=0

for MODE in $MODES; do
    XML_PATH="$OUTPUT_DIR/${MODE}-test-results.xml"
    HTML_PATH="$OUTPUT_DIR/${MODE}-test-results.html"

    UNITY_ARGS=(-runTests -projectPath "$PROJECT_PATH" -batchmode -testPlatform "$MODE" -testResults "$XML_PATH")
    if [ -n "$TEST_FILTER" ]; then
        UNITY_ARGS+=(-testFilter "$TEST_FILTER")
    fi

    echo "========================================="
    echo "Running $MODE tests..."
    if [ -n "$TEST_FILTER" ]; then
        echo "Filter: $TEST_FILTER"
    fi
    echo "========================================="
    "$UNITY_PATH" "${UNITY_ARGS[@]}"
    TEST_EXIT=$?

    if [ ! -f "$XML_PATH" ]; then
        echo "$MODE: ERROR - test results XML not found at $XML_PATH"
        echo "Unity editor log: $UNITY_LOG"
        OVERALL_EXIT=1
        continue
    fi

    if [ $TEST_EXIT -eq 0 ]; then
        echo "$MODE: PASSED"
    else
        echo "$MODE: FAILED"
        OVERALL_EXIT=1
    fi

    # Print failure details to console
    python3 "$SCRIPT_DIR/unity_test_results_utils.py" "$XML_PATH" -f console

    # Generate HTML report
    python3 "$SCRIPT_DIR/unity_test_results_utils.py" "$XML_PATH" -o "$HTML_PATH"
    echo ""
done

echo "========================================="
if [ $OVERALL_EXIT -eq 0 ]; then
    echo "Overall: ALL TESTS PASSED"
else
    echo "Overall: SOME TESTS FAILED"
    echo "Unity editor log: $UNITY_LOG"
fi
echo "========================================="

exit $OVERALL_EXIT
