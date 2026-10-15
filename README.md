# ShibaTag

A small Windows app that tidies up the music folders **ShibaDownloader** makes: it fills in missing tags using the file names, and finds duplicate copies. It uses the same shiba look as ShibaDownloader.

## Usage

1. Run `ShibaTag.exe` and press **Choose folder**, or drop a folder on the window or onto the exe. Subfolders are scanned too.
2. **Tags** tab: every MP3/M4A is listed with its current title, artist, album, track number, cover (yes/no), length and bitrate. Bitrates under 128 kbps are shown in red.
   - Orange values are what will be written. They come from the file name:
     - `01 - Artist - Title.mp3` inside a folder → title, artist, track = 01, album = folder name (an album or playlist).
     - `Artist - Title.mp3` → title and artist only (a single).
   - Only **empty** tags get filled in. Existing tags are never overwritten, because file names lose information: ShibaDownloader removes characters like `/ : ?` and cuts names off at 80 characters.
   - Untick any rows you want to skip, then press **Write tags to ticked files** and confirm. Nothing is written before you confirm.
3. **Duplicates** tab: copies of the same song are grouped. Two files count as the same song when their titles match (ignoring case, punctuation and `(feat. …)`) and either the main artist matches or the lengths are within 2 seconds. The copy with the highest bitrate is marked **best** and left unticked; the other copies are ticked. Press **Move ticked copies to the Recycle Bin** and confirm. Files are only ever moved to the Recycle Bin, never deleted outright.

Also: `ShibaTag.exe "D:\Music\Some Album"` opens with that folder already scanned.

## Build

```powershell
dotnet publish -c Release
```

This produces one self-contained file, `release\ShibaTag.exe` (~62 MB, includes the .NET runtime). It uses the same publish settings as ShibaDownloader. The only dependency is [TagLibSharp](https://github.com/mono/taglib-sharp) for reading and writing tags.

## Self-test

```powershell
.\release\ShibaTag.exe --selftest | Write-Output
```

- Checks file-name parsing.
- Copies 3 real MP3s from `%USERPROFILE%\Music\Global Warming` into `%TEMP%`, plus one copy saved as a single. It writes tags to the copies, reads them back and checks that the planted duplicate is found.
- Your original files are only read, and their SHA-256 hashes are compared before and after.
- To use another folder: `--selftest "D:\Music\Album"`.
- Exit code 0 means everything passed. The `| Write-Output` is needed because it's a GUI exe.

## Not included

- **Audio fingerprinting (AcoustID / Chromaprint) and cover art lookup.** These would need the native `fpcalc.exe` (or a C# Chromaprint port plus an audio decoding pipeline) and a personal AcoustID API key. Cover art would then need a second lookup (MusicBrainz → Cover Art Archive). That's too much for a small tagger. ShibaDownloader's files already have their names, so the file-name tagging covers them.
- Overwriting existing tags, album-artist, and other tag fields.
