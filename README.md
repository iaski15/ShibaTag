# ShibaTag

A small Windows app that tidies up the music folders **ShibaDownloader** makes: it fills in missing tags using the file names, and finds duplicate copies. It uses the same shiba look as ShibaDownloader.

## Install

Download `ShibaTag-Setup-<version>.exe` from [Releases](https://github.com/iaski15/ShibaTag/releases) and run it. It installs just for you (no admin needed) to `%LOCALAPPDATA%\Programs\ShibaTag`, adds ShibaTag to the Start menu (a desktop shortcut is optional), and adds an entry in *Installed apps* where you can uninstall it. Running a newer setup updates in place; `/silent` installs without any windows.

Prefer not to install? `ShibaTag.exe` from the same release is the portable version and runs from anywhere.

## Usage

1. Open ShibaTag from the Start menu and press **Choose folder**, or drop a folder on the window or onto the exe. Subfolders are scanned too.
2. **Tags** tab: every MP3/M4A is listed with its current title, artist, album, track number, cover (yes/no), length and bitrate. Bitrates under 128 kbps are shown in red.
   - Orange values are what will be written. They come from the file name:
     - `01 - Artist - Title.mp3` inside a folder → title, artist, track = 01, album = folder name (an album or playlist).
     - `Artist - Title.mp3` → title and artist only (a single).
   - Only **empty** tags get filled in. Existing tags are never overwritten, because file names lose information: ShibaDownloader removes characters like `/ : ?` and cuts names off at 80 characters.
   - **Find covers** looks up missing covers on the iTunes Search API (free, no account). All songs in an album folder share one lookup; each single gets its own. A cover is only used when the artist matches and the album name (or, for a single, the song title) matches. Found covers appear as a thumbnail in the Cover column; hover over one for a bigger preview. They are written along with the tags.
     - Playlist folders usually find nothing, because the folder name isn't an album that iTunes knows. Compilations ("Various Artists") and songs that aren't on iTunes don't get a cover either.
     - iTunes allows about 20 lookups a minute. If it says to slow down, ShibaTag stops and keeps what it found so far; press **Find covers** again a minute later to continue.
   - Untick any rows you want to skip, then press **Write tags to ticked files** and confirm. Nothing is written before you confirm.
3. **Duplicates** tab: copies of the same song are grouped. Two files count as the same song when their titles match (ignoring case, punctuation and `(feat. …)`) and either the main artist matches or the lengths are within 2 seconds. The copy with the highest bitrate is marked **best** and left unticked; the other copies are ticked. Press **Move ticked copies to the Recycle Bin** and confirm. Files are only ever moved to the Recycle Bin, never deleted outright.

Also: `ShibaTag.exe "D:\Music\Some Album"` opens with that folder already scanned.

## Build

```powershell
.\build.ps1
```

This needs the .NET 10 SDK and produces two files in `release\`:

| File | What it is |
| --- | --- |
| `ShibaTag-Setup-<version>.exe` | The installer (net48, which ships with Windows). The app is embedded inside it. |
| `ShibaTag.exe` | The portable app (~62 MB, includes the .NET runtime). It uses the same publish settings as ShibaDownloader. |

The version lives in `Directory.Build.props`. The installer in `Setup\` is a copy of ShibaDownloader's. The only library dependency is [TagLibSharp](https://github.com/mono/taglib-sharp), for reading and writing tags.

## Self-test

```powershell
.\release\ShibaTag.exe --selftest | Write-Output
```

- Checks file-name parsing, and checks cover matching against a canned iTunes response.
- Copies 3 real MP3s from `%USERPROFILE%\Music\Global Warming` into `%TEMP%`, plus one copy saved as a single. It writes tags to the copies, reads them back and checks that the planted duplicate is found.
- Does one live iTunes cover lookup and embeds the result in a temp copy. This prints `SKIP` instead of `FAIL` when you're offline or iTunes is rate-limiting.
- Your original files are only read, and their SHA-256 hashes are compared before and after.
- To use another folder: `--selftest "D:\Music\Album"`.
- Exit code 0 means everything passed. The `| Write-Output` is needed because it's a GUI exe.

## Not included

- **Audio fingerprinting (AcoustID / Chromaprint).** This would need the native `fpcalc.exe` (or a C# Chromaprint port plus an audio decoding pipeline) and a personal AcoustID API key. That's too much for a small tagger. ShibaDownloader's files already carry artist and title in their names, so file-name tagging and the iTunes cover lookup work without it.
- Overwriting existing tags, album-artist, and other tag fields.
