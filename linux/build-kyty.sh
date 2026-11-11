#!/usr/bin/env bash
# Apply only this mod's integration changes to a separately downloaded Kyty tree.
set -euo pipefail
mod_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ $# != 1 ]]; then
  echo "Usage: $0 /path/to/KytyPS5-source" >&2
  exit 2
fi
kyty_root="$(cd -- "$1" && pwd)"
expected=3e6ee3ec0bcef90afeb280c08514fd7355c483b5
[[ "$(git -C "$kyty_root" rev-parse HEAD)" == "$expected" ]] || {
  echo 'This integration targets chenxiao07/KytyPS5 v20261013 (3e6ee3e) only.' >&2
  exit 1
}
for tool in clang clang++ cmake ninja glslangValidator python3; do
  command -v "$tool" >/dev/null || { echo "Missing build dependency: $tool" >&2; exit 1; }
done
patch="$mod_root/kyty-linux-mod.patch"
if git -C "$kyty_root" apply --reverse --check "$patch" 2>/dev/null; then
  echo 'The matching Linux mod integration is already applied.'
else
  git -C "$kyty_root" apply --check "$patch"
  git -C "$kyty_root" apply "$patch"
fi
git -C "$kyty_root" submodule update --init --recursive
cmake -S "$kyty_root" -B "$kyty_root/_Build/linux" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++ \
  -DKYTY_BUILD_LAUNCHER=OFF \
  '-DCMAKE_C_FLAGS_RELEASE=-O3 -DNDEBUG -march=x86-64-v3' \
  '-DCMAKE_CXX_FLAGS_RELEASE=-O3 -DNDEBUG -march=x86-64-v3 -flto=thin -fexceptions' \
  -DCMAKE_EXE_LINKER_FLAGS=-flto=thin
cmake --build "$kyty_root/_Build/linux" --target kyty_emulator kyty_shader_precompile \
  --parallel "${KYTY_BUILD_JOBS:-8}"
echo "Build finished. Select $kyty_root/_Build/linux in the mod launcher's Kyty field."
