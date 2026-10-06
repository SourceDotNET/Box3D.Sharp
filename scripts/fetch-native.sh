#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
repo=https://github.com/SourceDotNET/Box3D.Source

commit=$(git -C "$root/native/box3d" rev-parse HEAD)
tag=$(git ls-remote --tags "$repo.git" | awk -v commit="$commit" '$1 == commit { name = $2; sub("^refs/tags/", "", name); sub("\\^\\{\\}$", "", name); print name; exit }')
if [ -z "$tag" ]; then
  echo "No Box3D.Source tag points at submodule commit $commit, tag and release it first." >&2
  exit 1
fi

if [ $# -gt 0 ]; then
  rids=("$@")
else
  rids=(win-x64 win-arm64 linux-x64 linux-arm64 linux-musl-x64 linux-musl-arm64 osx-x64 osx-arm64)
fi

if command -v sha256sum > /dev/null; then
  sha256=(sha256sum)
else
  sha256=(shasum -a 256)
fi

download=$(mktemp -d)
trap 'rm -rf "$download"' EXIT

curl -fsSL -o "$download/SHA256SUMS" "$repo/releases/download/$tag/SHA256SUMS"
for rid in "${rids[@]}"; do
  zip="box3d-$rid.zip"
  curl -fsSL -o "$download/$zip" "$repo/releases/download/$tag/$zip"
  (cd "$download" && grep " $zip\$" SHA256SUMS | "${sha256[@]}" -c - > /dev/null)
  rm -rf "$root/artifacts/native/$rid"
  mkdir -p "$root/artifacts/native/$rid"
  unzip -oq "$download/$zip" -d "$root/artifacts/native/$rid"
  echo "$rid: $tag"
done
