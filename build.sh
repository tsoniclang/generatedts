#!/usr/bin/env bash
set -e

# Build script for tsbindgen
# Outputs to artifacts/ directory following .NET SDK conventions
#
# Usage:
#   ./build.sh              # Build for current platform only
#   ./build.sh --all-archs  # Build for all supported architectures (linux-x64, linux-arm64, osx-x64, osx-arm64)

ALL_ARCHS=false
if [[ "$1" == "--all-archs" ]]; then
  ALL_ARCHS=true
fi

echo "Building tsbindgen..."

# Clean previous artifacts
rm -rf artifacts/

if [[ "$ALL_ARCHS" == "true" ]]; then
  # Build for all supported architectures (exclude Windows)
  RIDS=("linux-x64" "linux-arm64" "osx-x64" "osx-arm64")

  for rid in "${RIDS[@]}"; do
    echo ""
    echo "Building for $rid..."
    dotnet publish src/tsbindgen/tsbindgen.csproj \
      --configuration Release \
      --runtime "$rid" \
      --self-contained false \
      --artifacts-path artifacts/
  done

  echo ""
  echo "Build complete for all architectures!"
  echo "Output: artifacts/publish/tsbindgen/release_*/"
else
  # Single-arch build (current platform)
  dotnet publish src/tsbindgen/tsbindgen.csproj \
    --configuration Release \
    --artifacts-path artifacts/

  echo ""
  echo "Build complete!"
  echo "Output: artifacts/publish/tsbindgen/release/"
fi
