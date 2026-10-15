using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace ShibaTag;

/// <summary>
/// `ShibaTag.exe --selftest [music folder]` - checks name parsing, then copies a few real files to %TEMP%,
/// writes tags to the copies, reads them back and finds a planted duplicate. The source files are only read
/// (their hashes are compared before and after). Prints PASS/FAIL lines; exit code 0 = all passed.
/// Pipe it to see the output: `.\ShibaTag.exe --selftest | Write-Output`.
/// </summary>
public static class SelfTest
{
    private static int _failed;

    public static int Run(string? source)
    {
        source ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Global Warming");

        // ---- file name parsing (no files touched) ----
        var p = Music.ParseName(@"C:\M\Global Warming\01 - Pitbull, Sensato - Global Warming (feat. Sensato).mp3");
        Check("album track parsed", p is { Artist: "Pitbull, Sensato", Title: "Global Warming (feat. Sensato)", Album: "Global Warming", Track: 1 });
        p = Music.ParseName(@"C:\M\X\04 - Pitbull - Back in Time - featured in Men In Black 3.mp3");
        Check("title keeps its own ' - '", p is { Artist: "Pitbull", Title: "Back in Time - featured in Men In Black 3", Track: 4 });
        p = Music.ParseName(@"C:\M\X\17 - Pitbull, Marc Anthony - Rain Over Me (feat. Marc Anthony) -.mp3");
        Check("truncated name loses dangling ' -'", p?.Title == "Rain Over Me (feat. Marc Anthony)");
        p = Music.ParseName(@"C:\M\Singles\50 Cent - In Da Club.m4a");
        Check("single parsed, no album/track", p is { Artist: "50 Cent", Title: "In Da Club", Album: null, Track: 0 });
        Check("no-break space becomes a plain space", Music.ParseName("C:\\M\\A\\01 - Pitbull,\u00A0Sensato - X.mp3")?.Artist == "Pitbull, Sensato");
        Check("unparseable name ignored", Music.ParseName(@"C:\M\random video.mp3") is null);

        // ---- cover matching on a canned iTunes response (no network) ----
        const string json = """
            {"resultCount":3,"results":[
              {"artistName":"Someone Else","collectionName":"Global Warming","trackName":"Global Warming","artworkUrl100":"https://x/wrong/100x100bb.jpg"},
              {"artistName":"Pitbull","collectionName":"Global Warming: Meltdown (Deluxe Version)","trackName":"Feel This Moment (feat. Christina Aguilera)","artworkUrl100":"https://x/gw/100x100bb.jpg"},
              {"artistName":"Pitbull","collectionName":"Climate Change","trackName":"Global Warming (feat. Sensato)","artworkUrl100":"https://x/cc/100x100bb.jpg"}]}
            """;
        Check("album cover: artist must match, folder name is a prefix, upsized",
              Music.PickArtwork(json, "Pitbull, Sensato", "Global Warming", "x") == "https://x/gw/600x600bb.jpg");
        Check("single cover: matched by song title", Music.PickArtwork(json, "Pitbull", null, "Global Warming (feat. Sensato)") == "https://x/cc/600x600bb.jpg");
        Check("no cover for an unknown album", Music.PickArtwork(json, "Pitbull", "Some Playlist", "x") is null);
        Check("no cover for an unknown artist", Music.PickArtwork(json, "Nobody", "Global Warming", "x") is null);

        // ---- real files, on temp copies ----
        var originals = Directory.Exists(source) ? Directory.GetFiles(source, "*.mp3").Order().ToList() : [];
        if (originals.Count == 0)
        {
            Check($"test files found in {source}", false);
            return Done();
        }
        var picks = originals.Where(f => Path.GetFileName(f) is var n && (n.StartsWith("01 - ") || n.StartsWith("04 - ") || n.StartsWith("17 - "))).ToList();
        if (picks.Count == 0) picks = originals.Take(3).ToList();
        var hashesBefore = picks.Select(Hash).ToList();

        var tmp = Path.Combine(Path.GetTempPath(), "ShibaTag-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        var album = Path.Combine(tmp, Path.GetFileName(source));
        Directory.CreateDirectory(album);
        try
        {
            foreach (var f in picks) File.Copy(f, Path.Combine(album, Path.GetFileName(f)));
            // A loose single copy of the first song: parses as "Artist - Title" and must show up as a duplicate.
            var first = Music.ParseName(picks[0])!.Value;
            var single = Path.Combine(tmp, $"{first.Artist} - {first.Title}.mp3");
            File.Copy(picks[0], single);
            // Start the copies untagged (as ShibaDownloader saves them), whatever state the originals are in.
            foreach (var f in Directory.GetFiles(tmp, "*.mp3", SearchOption.AllDirectories))
            {
                using var t = TagLib.File.Create(f);
                t.RemoveTags(TagLib.TagTypes.AllTags);
                t.Save();
            }

            var before = Music.Scan(tmp, out var bad);
            Check($"scan found {picks.Count + 1} files", before.Count == picks.Count + 1 && bad == 0);
            Check("duration and bitrate read", before.All(s => s.Duration.TotalSeconds > 10 && s.Bitrate > 0));
            foreach (var s in before) Music.Propose(s);
            foreach (var s in before.Where(s => s.HasChanges)) Music.Write(s);

            var after = Music.Scan(tmp, out _);
            foreach (var (was, now) in before.Zip(after))
            {
                var want = Music.ParseName(was.Path)!.Value;
                var ok = now.Title == (was.Title.Length > 0 ? was.Title : want.Title)
                      && now.Artist == (was.Artist.Length > 0 ? was.Artist : want.Artist)
                      && now.Album == (was.Album.Length > 0 || want.Album is null ? was.Album : want.Album)
                      && now.Track == (was.Track > 0 ? was.Track : want.Track)
                      && Math.Abs((now.Duration - was.Duration).TotalSeconds) < 1;
                Check($"tags written+read back: {now.FileName} -> {now.Track} | {now.Artist} | {now.Title} | {now.Album}", ok);
            }
            foreach (var s in after) Music.Propose(s);
            Check("second pass proposes nothing", after.All(s => !s.HasChanges));

            var dups = Music.FindDuplicates(after);
            Check("planted duplicate found", dups.Count == 1 && dups[0].Count == 2 && dups[0].Any(s => s.Path == single));
            Check("one keeper per group", dups.All(g => g.Count(s => s.IsKeeper) == 1 && g.Count(s => s.Recycle) == g.Count - 1));

            // Live iTunes lookup for the album copy; SKIP (not FAIL) when offline or rate-limited.
            var albumSong = after.First(s => s.Track > 0);
            var q = Music.CoverQuery(albumSong);
            byte[]? jpg = null;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                jpg = Task.Run(() => Music.FindCoverAsync(http, q.Artist, q.Album, q.Title)).GetAwaiter().GetResult();
                Check($"iTunes cover found for album \"{q.Album}\" ({jpg?.Length / 1024} KB)", jpg is { Length: > 1000 });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Console.WriteLine($"SKIP  iTunes cover lookup: {ex.Message}");
            }
            if (jpg != null)
            {
                albumSong.NewCover = jpg;
                Music.Write(albumSong);
                var reread = Music.Read(albumSong.Path);
                Check("cover embedded and tags kept", reread.HasCover && reread.Title == albumSong.Title && reread.Track == albumSong.Track);
            }
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
        Check("originals untouched", picks.Select(Hash).SequenceEqual(hashesBefore));
        return Done();
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Check(string what, bool ok)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) _failed++;
    }

    private static int Done()
    {
        Console.WriteLine(_failed == 0 ? "selftest OK" : $"selftest FAILED ({_failed})");
        return _failed == 0 ? 0 : 1;
    }
}
