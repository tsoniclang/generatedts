#!/bin/bash
# Library mode test - ensures --lib mode produces identical output to normal mode
# when using full BCL as library contract

set -euo pipefail

echo "================================================"
echo "Library Mode Test"
echo "================================================"
echo ""

# Clean previous runs
echo "[1/4] Cleaning previous test runs..."
rm -rf .tests/lib/baseline .tests/lib/library-mode
mkdir -p .tests/lib

# Run 1: Generate full BCL normally (baseline)
echo "[2/4] Running normal generation (baseline)..."
dotnet run --project src/tsbindgen/tsbindgen.csproj -- \
    generate -d ~/dotnet/shared/Microsoft.NETCore.App/10.0.0-rc.1.25451.107 \
    -o .tests/lib/baseline --strict > .tests/lib/baseline-output.txt 2>&1

# Check baseline succeeded
if [ $? -ne 0 ]; then
    echo "❌ FAILED: Baseline generation failed"
    cat .tests/lib/baseline-output.txt
    exit 1
fi

echo "          ✓ Baseline generation succeeded"

# Run 2: Generate with --lib pointing to baseline
echo "[3/4] Running library mode generation (--lib baseline)..."
dotnet run --project src/tsbindgen/tsbindgen.csproj -- \
    generate -d ~/dotnet/shared/Microsoft.NETCore.App/10.0.0-rc.1.25451.107 \
    -o .tests/lib/library-mode \
    --lib .tests/lib/baseline \
    --strict > .tests/lib/library-mode-output.txt 2>&1

# Check library mode succeeded
if [ $? -ne 0 ]; then
    echo "❌ FAILED: Library mode generation failed"
    cat .tests/lib/library-mode-output.txt
    exit 1
fi

echo "          ✓ Library mode generation succeeded"

# Check for LIB001-003 errors
echo "[4/4] Checking for library mode validation errors..."
if grep -q "LIB00[123]" .tests/lib/library-mode-output.txt; then
    echo "❌ FAILED: Library mode validation errors detected"
    echo ""
    echo "LIB001-003 errors found:"
    grep "LIB00[123]" .tests/lib/library-mode-output.txt
    echo ""
    exit 1
fi

echo "          ✓ No library mode validation errors"

# Compare outputs
echo "          Comparing outputs (byte-for-byte)..."
if diff -r .tests/lib/baseline .tests/lib/library-mode > /dev/null 2>&1; then
    echo "          ✓ Outputs are identical (byte-for-byte)"
    echo ""
    echo "================================================"
    echo "✓ LIBRARY MODE VERIFIED"
    echo "================================================"
    echo ""
    echo "Summary:"
    echo "  - Normal mode generation succeeded"
    echo "  - Library mode generation succeeded"
    echo "  - No LIB001-003 validation errors"
    echo "  - Outputs are byte-for-byte identical"
    echo "  - Library mode contract filtering is transparent"
    echo ""
    exit 0
else
    echo "❌ FAILED: Outputs differ between normal and library mode"
    echo ""
    echo "This indicates library mode is filtering or changing output incorrectly."
    echo "When using full BCL as library contract, output should be identical."
    echo ""
    echo "Run 'diff -r .tests/lib/baseline .tests/lib/library-mode' to see differences"
    echo ""

    # Show summary of differences
    echo "Difference summary:"
    diff -r .tests/lib/baseline .tests/lib/library-mode | head -50
    echo ""

    exit 1
fi
