#!/usr/bin/env bash
# Installs fish on a Linux CI runner, so the FishTests that source the generated
# conf.d hook into a real fish run there instead of skipping.
set -euo pipefail

if command -v fish > /dev/null 2>&1; then
    fish --version
    exit 0
fi

sudo apt-get update -qq
sudo apt-get install -y -qq fish
fish --version
