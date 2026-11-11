#!/usr/bin/env bash
set -euo pipefail
mod_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
mkdir -p "$mod_root/runtime/config" "$mod_root/runtime/cache" "$mod_root/runtime/tmp" "$mod_root/runtime/logs"
export XDG_CONFIG_HOME="$mod_root/runtime/config"
export XDG_CACHE_HOME="$mod_root/runtime/cache"
export TMPDIR="$mod_root/runtime/tmp"
cd -- "$mod_root"
exec "${PYTHON:-python3}" "$mod_root/custom-launcher/launcher.py" "$@"
