# Demon's Souls Ultrawide — Linux 1.0.0 (experimental)

The native Linux edition of the mod for **chenxiao07/KytyPS5 v20261013** and
**Demon's Souls PPSA01341, version 01.007.000**. This is a separate Linux
release. The Windows releases and their files remain available unchanged.

This package contains **our mod only**: the Python/PySide6 launcher, ultrawide
patch generator and example patch, original native integration changes, build
helper, menu-entry installer and documentation. It contains no emulator binary,
complete emulator source file, game file, game artwork, save, shader seed, shader
cache, pipeline binary, driver cache or third-party dependency bundle.

The Linux UI, camera, inventory and 5.1 paths are integrated into a native Kyty
build. They are not Windows DLLs and do not work by loading the Windows adapters.
You must build your own copy of the exact Kyty release with the included patch.
This download is a source mod kit, not a prebuilt emulator or a one-file executable.

## Features

- Ultrawide world rendering without stretching; centered 16:9 interface.
- Original-style artwork home, separate Settings and Manage savegames pages,
  General / Picture / Audio / Camera / Shaders tabs, Save / Back navigation,
  Play / Quit game, and custom window controls.
- 50–100% 3D render scaling, with full-size UI/output.
- Camera distance hotkeys: Page Up / Page Down by default, four farther steps.
- Signature-checked inventory preview camera correction.
- Stereo or experimental directional 5.1 using the original fifth-order
  ACN/SN3D ambisonic decoder, with native LFE retained.
- Sharded native shader/pipeline precompilation and a progress bar.
- Save export/import; replacing a save asks for confirmation and keeps a backup.
- Closing the launcher leaves its separately supervised game running.

The launcher reads artwork and title metadata from the user's selected game.
No artwork is included in the package.

## Requirements

- Linux x86-64 with AVX2/FMA/BMI2 (x86-64-v3), a Vulkan-capable driver,
  and suitable hardware for the emulator/game.
- Your own unpacked PPSA01341 / 01.007.000 game folder containing `eboot.bin`
  and `sce_sys/param.json`.
- Your own source checkout of `chenxiao07/KytyPS5`, tag `v20261013`, commit
  `3e6ee3ec0bcef90afeb280c08514fd7355c483b5`, including its pinned submodules.
- Clang, LLD, CMake, Ninja, Python 3.11+, `glslangValidator`, Git, and the
  development libraries required by bundled SDL2 (audio, X11/Wayland, udev).
- PySide6 (`requirements.txt`) and Bubblewrap (`bwrap`).
- `pactl` for audio-device discovery. KDE's `kscreen-doctor` is used for
  supported display modes when available; current screen size is a fallback.

Tested locally on Arch Linux / KDE / XWayland, Clang 23, Ryzen 9 9950X3D,
RTX 5090 and NVIDIA driver 615.78.08. Other distributions, GPUs and compiler
versions have not been validated. The exact UI shader variants are checked
before they are transformed; an unrecognized variant is left untouched.

## Build your own Kyty, then start the mod

Extract the Linux archive into its own writable directory. Keep your game and
Kyty checkout separate from this mod directory. The build helper changes only
the Kyty checkout you explicitly give it; keep that checkout separate from
any existing Windows setup or source tree containing your own edits.

Download the emulator source yourself:

```bash
git clone --branch v20261013 --recurse-submodules \
  https://github.com/chenxiao07/KytyPS5.git /your/path/KytyPS5-linux
```

Install the build prerequisites for your distribution. The exact upstream
release's [Linux build instructions](https://github.com/chenxiao07/KytyPS5/blob/v20261013/README.md#building-on-linux)
list the SDL development packages. This mod builds the emulator and shader
precompiler without the upstream Qt launcher, so Qt6 development packages for
that launcher are not required.

From the extracted mod directory:

```bash
./build-kyty.sh /your/path/KytyPS5-linux
./run-custom-launcher.sh
```

The helper verifies the exact source commit, checks/applies the integration
patch, initializes pinned submodules, and builds native binaries with Clang,
Release optimization, ThinLTO, exceptions enabled and `-march=x86-64-v3`.
Set `KYTY_BUILD_JOBS=4` before the build if memory is limited.

Install PySide6 through your distribution, or use a virtual environment:

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
PYTHON="$PWD/.venv/bin/python" ./run-custom-launcher.sh
```

In the launcher, choose Settings → General:

1. **Kyty:** `/your/path/KytyPS5-linux/_Build/linux`.
2. **Game:** the unpacked game directory.
3. Choose resolution, language and audio, then Save. If Play opened initial
   setup automatically, the button says **Save and run**.

For an optional start menu entry, run `python3 install-menu-entry.py`, or
`.venv/bin/python install-menu-entry.py` when using the virtual environment.
The generated entry remembers the Python interpreter used by the installer.

## Saves, caches and safety of the original installation

All mod runtime data is kept in `runtime/` beside this launcher:

- `_SaveData/`: Linux saves and import backups.
- `profiles/`: generated patches and aspect-specific pipeline caches.
- `logs/`: game/precompile diagnostics.
- `sessions/`: private local session state.
- `custom-launcher-settings.json`: preferences.

The game is mounted read-only with Bubblewrap. Only the mod directory is
writable to a game/precompile process, apart from device/process interfaces.
Nothing is installed into the game directory. The existing Windows setup does
not need to be moved, patched or overwritten. A separate Linux save is used;
import a ZIP exported by the Windows mod through Manage savegames if wanted.

Driver pipelines are separated by the native executable fingerprint and UI
output aspect. Windows pipeline caches and GPU binaries are not portable to
the Linux driver. You may supply a base shader seed file you generated from
your own game as the active profile's `game.seeds` to avoid re-extraction; these
files contain game shader code and are intentionally not distributed here.
Without an imported seed file the precompiler creates one from your own game.
The recorded specialization hints and pass-state definitions are read from
your separately downloaded Kyty source tree, not bundled in this mod kit.

Full native precompile can take an hour or more. It splits the work into small
NVIDIA-safe batches, reuses/merges completed batches, and writes shader prefetch
inputs at the end. The launcher can close while compilation continues.

For 5.1, select a six-channel output/profile in your desktop sound settings
before starting the game, then choose **5.1** in the mod's Audio tab. Restart
the game after changing audio mode. Use the receiver without stereo upmixing
when checking the directional result.

## Validation and known limits

Locally observed: the native game reached its first-run language screen at
60 fps; the ultrawide patch applied; the UI was visually centered at 3440×1440;
a live camera step applied; all 36 ambisonic coefficient channels were seen;
Linux exposed 48 kHz float32 six-channel streams with front/rear energy; and a
full base+hinted native precompile completed 684 batches.

Automated checks cover patch parity with the Windows 3440×1440 patch, SPIR-V
validation of the whitelisted UI variants, camera bounds and preview record
signatures, surround decoder directions/LFE/isolation, save archive handling,
launcher navigation and independent process lifetime.

**Experimental:** full gameplay and inventory appearance across items/aspects
have not been fully checked. The inventory calibration is the original
3440×1440 correction; other aspects interpolate it. Physical speaker placement,
localization and perceived surround quality still need listening tests. A
six-channel software stream alone does not prove acoustic correctness. Newer
Kyty/game builds and different generated shader variants may require updates.

## Source and licensing

The launcher and original mod-owned code use the repository's MIT license.
`kyty-linux-mod.patch` contains only the native integration changes and the
small context needed to apply them to Kyty; its Kyty-derived hunks are supplied
under GPL-2.0, matching that exact Kyty source release. The GPL-2.0 license text
is included as `COPYING-INTEGRATION-GPL-2.0.txt`. The resulting emulator and its
third-party dependencies retain their own upstream licenses. No complete Kyty
program source file or compiled emulator is included in this package.

This is an unofficial community mod, unaffiliated with Sony, Bluepoint Games,
FromSoftware or the Kyty developers. Game trademarks and assets belong to their
respective owners. Development of the Linux port was AI-assisted; local test
results and remaining limitations are stated above.
