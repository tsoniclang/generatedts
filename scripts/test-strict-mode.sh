#!/bin/bash
# Regression test for strict mode - ensures no non-whitelisted warnings

set -e  # Exit on error

echo "================================================"
echo "Strict Mode Regression Test"
echo "================================================"
echo ""

# Run strict mode validation
echo "[1/2] Running strict mode validation..."
output=$(dotnet run --project src/tsbindgen/tsbindgen.csproj -- \
    generate-bcl --out-dir .tests/strict-test --strict-mode 2>&1)

# Check exit code
exit_code=$?

if [ $exit_code -ne 0 ]; then
    echo "❌ FAILED: Strict mode validation failed"
    echo ""
    echo "Output:"
    echo "$output"
    exit 1
fi

echo "✓ Strict mode validation passed"
echo ""

# Check validation output
echo "[2/2] Checking diagnostic counts..."

# Extract counts from output
errors=$(echo "$output" | grep -o "Validation complete - [0-9]* errors" | grep -o "[0-9]*" || echo "0")
warnings=$(echo "$output" | grep -o "Validation complete - [0-9]* errors, [0-9]* warnings" | grep -o "[0-9]*" | tail -1 || echo "unknown")
info_count=$(echo "$output" | grep -o "[0-9]* info" | grep -o "[0-9]*" || echo "unknown")

echo "  Errors:   $errors"
echo "  Warnings: $warnings"
echo "  Info:     $info_count"
echo ""

# Verify error count is zero
if [ "$errors" != "0" ]; then
    echo "❌ FAILED: Expected 0 errors, got $errors"
    exit 1
fi

echo "✓ Zero errors verified"
echo ""

# Check for non-whitelisted warnings
non_whitelisted=$(echo "$output" | grep "Strict mode enforced:" || true)

if [ -n "$non_whitelisted" ]; then
    echo "❌ FAILED: Non-whitelisted warnings detected"
    echo "$non_whitelisted"
    exit 1
fi

echo "✓ No non-whitelisted warnings detected"
echo ""

echo "================================================"
echo "✓ ALL TESTS PASSED"
echo "================================================"
echo ""
echo "Summary:"
echo "  - Strict mode validation passes"
echo "  - Zero errors (ERROR level diagnostics)"
echo "  - Only whitelisted warnings allowed (TBG120 for PR D)"
echo "  - INFO diagnostics don't count toward warning totals"
echo ""
