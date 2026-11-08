This application is a Linux port of Lazy Duchess's CC-Merger, which can be found at this link: https://github.com/LazyDuchess/CC-Merger

# CCMerger for Linux

A Linux port of [CC Merger](https://github.com/LazyDuchess/CC-Merger) by Lazy Duchess, which merges Sims 2 `.package` files into fewer, bigger ones. Ported with her permission.

Same window, same options and same output format as the Windows version. The interface uses [Avalonia](https://avaloniaui.net/) instead of WinForms, on .NET 10.

## Installing (AppImage)

The AppImage is a single file that runs on most Linux distributions. You don't need to install .NET or anything else.

1. Download `CCMerger-<version>-x86_64.AppImage` from the [Releases](../../releases) page.
2. Make it executable, using one of these:
   - **File manager:** right click the file, *Properties*, *Permissions*, and tick *Allow executing file as program* (the wording changes between desktops).
   - **Terminal:**
     ```bash
     chmod +x CCMerger-*-x86_64.AppImage
     ```
3. Run it with a double click, or from the terminal:
   ```bash
   ./CCMerger-*-x86_64.AppImage
   ```

Tip: keep the file somewhere permanent, like `~/Applications`.

### Adding it to your application menu (optional, with Gear Lever)

[Gear Lever](https://github.com/mijorus/gearlever) is an app that manages AppImages for you: it makes them executable, adds them to your application menu with their icon, and can remove them later. It's available on Flathub (also in the Discover / GNOME Software store as "Gear Lever").

1. Install it: `flatpak install flathub it.mijorus.gearlever` (or search for "Gear Lever" in your software store).
2. Open Gear Lever and drag the CCMerger AppImage into its window. You can also right click the AppImage and choose *Open with Gear Lever*.
3. Click the button to unlock the file, then **Move to the app menu** (the labels can differ slightly between versions).
4. CCMerger now shows up in your application menu with its icon, and you can open it from there. To uninstall it, open Gear Lever and remove it from the list.

[AppImageLauncher](https://github.com/TheAssassin/AppImageLauncher) does something similar if you prefer it.

### Using it

1. Click **Browse** (or paste the path) and pick the folder with the CC you want to merge. Sub folders are included.
2. Set the limits, or leave the defaults (100 MB and 1000 files per package). If your game crashes with higher values, use the defaults or try smaller ones.
3. Click **Merge** and choose where to save. Saving as `mergedcc.package` creates `mergedcc0.package`, `mergedcc1.package`, and so on.
4. Your original files are never changed or deleted. A `CCMerger.log` is written next to the merged packages.

Your settings are remembered in `~/.config/CCMerger/settings.json`.

If you play through Wine or Proton, your `Downloads` folder is inside the Wine prefix, for example `~/.wine/drive_c/users/<you>/Documents/EA Games/The Sims™ 2 Ultimate Collection/Downloads`.

### If it doesn't start

- **Complains about FUSE or `libfuse.so.2`:** install `libfuse2` (Debian/Ubuntu/Mint: `sudo apt install libfuse2`; on Ubuntu 24.04 and newer the package is `libfuse2t64`; Fedora: `sudo dnf install fuse-libs`; Arch: `sudo pacman -S fuse2`). Or skip FUSE entirely:
  ```bash
  ./CCMerger-*-x86_64.AppImage --appimage-extract-and-run
  ```
- **Font or `libfontconfig` errors:** install `fontconfig` (`sudo apt install libfontconfig1`).
- **The Browse button does nothing:** it uses your desktop's file chooser (`xdg-desktop-portal`). Install the portal for your desktop (`xdg-desktop-portal-gtk`, `xdg-desktop-portal-kde`...) or just paste the folder path into the text box.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). From this folder:

```bash
dotnet publish CCMerger.Linux/CCMerger.Linux.csproj -c Release -r linux-x64 --self-contained true -o publish
./publish/CCMerger
```

Use `-r linux-arm64` on ARM machines. To just try it while developing: `dotnet run --project CCMerger.Linux`.

## What changed from the Windows version

- WinForms, Delimon.Win32.IO and WindowsAPICodePack (taskbar progress) were dropped.
- Settings live in `~/.config/CCMerger/settings.json` instead of `App.config`.
- Packages are opened one at a time, so thousands of files don't hit the Linux open-files limit.
- `.package` is matched case-insensitively and files are processed in a stable, sorted order (Linux returns directory entries in arbitrary order, and the order decides which duplicate resource wins in game).
- Merged packages are written to temporary files and renamed at the end; a failed or cancelled merge leaves nothing half written, and re-merging into a folder that already holds `mergedcc0.package` can't truncate a file that is still being read.
- Resources are identified by type + group + instance + instance2 (the original ignored instance2, which could drop a resource).
- Truncated / damaged packages are detected when read and reported in `CCMerger.log`, rather than failing half way through writing.
- The progress bar works (in the original it was stuck at 0), and `CCMerger.log` reports the real file count.

## Checking a merge (optional)

The scripts in `tools/` are not part of the AppImage. They are separate files in this repository that only need Python 3 (no extra packages). Keep the files of that folder together, because they share `dbpf.py`. Download the repository (or just the `tools/` folder) to use them.

### verify_merge.py

Checks that a merge didn't lose or change anything. It never modifies any file.

```bash
python3 tools/verify_merge.py ORIGINAL_DIR OUTPUT_DIR
```

- `ORIGINAL_DIR` is the folder with the original packages. Run the merge on a **copy** of your CC folder and keep the originals there.
- `OUTPUT_DIR` is where the `mergedcc0.package`, `mergedcc1.package`... files were saved. It can be the same folder; the merged files are ignored when reading the originals.
- If you saved with another name, for example `mycc.package`, add `--prefix mycc`.

It reads every original package and every merged package and compares them resource by resource (type, group and instance, plus a SHA-1 of the data), and also checks the compression (DIR) records and the headers of the merged files.

- `PASS` means every resource is present and byte for byte identical, and nothing extra was added.
- `FAIL` is followed by what is missing or different (the first 10 of each kind).
- Originals that are broken or truncated are listed as `unreadable`. That's expected: the merge skips them too, and they are reported in `CCMerger.log`.

This only proves the contents were preserved. It can't tell you whether the game will load them, so test in game too. On very big folders it can take a while.

### Other scripts

- `ccmerge_ref.py SOURCE_DIR OUTPUT.package` is the same merge algorithm written in Python, usable as a command line merger.
- `make_test_packages.py OUTPUT_DIR` creates fake packages for testing.

## License

No license is specified for this project.

`CCMerger.Linux/Dbpf/DbpfPackage.cs` is derived from package code that comes from [FreeSO](https://github.com/riperiperi/FreeSO), which is licensed under the [Mozilla Public License 2.0](https://www.mozilla.org/MPL/2.0/). That file keeps its MPL 2.0 notice (see the top of the file).

## Credits

CCMerger by Lazy Duchess. Linux port by Sales. Package code originally from [FreeSO](https://github.com/riperiperi/FreeSO) and [SimUnity2](https://github.com/LazyDuchess/SimUnity2).
