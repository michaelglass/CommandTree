#!/usr/bin/env bash
# Installs a pinned Jujutsu (jj) release on a Linux x86_64 CI runner and puts it on
# PATH for later steps, so the jj arm of build/CommandTree.targets is tested in CI.
# The tarball is verified against its sha256 before anything in it runs.
set -euo pipefail

version="0.45.1"
sha256="f35438350b5d61963aac5dd74ede510b31d6b9690769d1a6268cf058cc825f72"
asset="jj-v${version}-x86_64-unknown-linux-musl.tar.gz"
url="https://github.com/jj-vcs/jj/releases/download/v${version}/${asset}"

dest="${RUNNER_TEMP:-/tmp}/jj-${version}"
mkdir -p "$dest"

curl --fail --silent --show-error --location --output "$dest/$asset" "$url"
echo "${sha256}  $dest/$asset" | sha256sum --check --strict
tar -xzf "$dest/$asset" -C "$dest" ./jj

"$dest/jj" --version

if [ -n "${GITHUB_PATH:-}" ]; then
    echo "$dest" >> "$GITHUB_PATH"
fi
