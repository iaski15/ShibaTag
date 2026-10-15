using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ShibaTag;

/// <summary>One audio file: its current tags, what the file name says they should be, and list-row state.</summary>
public sealed class Song
{
    public required string Path { get; init; }
    public string Folder { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public uint Track { get; init; }
    public bool HasCover { get; init; }
    public TimeSpan Duration { get; init; }
    public int Bitrate { get; init; }

    // Proposed values (null = leave as is), filled by Music.Propose.
    public string? NewTitle { get; set; }
    public string? NewArtist { get; set; }
    public string? NewAlbum { get; set; }
    public uint? NewTrack { get; set; }
    public byte[]? NewCover { get; set; }                                // JPEG from Music.FindCoverAsync
    public System.Windows.Media.ImageSource? NewCoverThumb { get; set; } // its preview in the list
    public bool HasChanges => NewTitle != null || NewArtist != null || NewAlbum != null || NewTrack != null || NewCover != null;

    public bool Apply { get; set; }          // tag view: write this row
    public bool Recycle { get; set; }        // duplicate view: move this copy to the Recycle Bin
    public bool IsKeeper { get; set; }       // duplicate view: the copy kept by default
    public string Group { get; set; } = "";  // duplicate view: group header text

    public string FileName => System.IO.Path.GetFileName(Path);
    public string TrackText => Track == 0 ? "" : Track.ToString();
    public string NewTrackText => NewTrack?.ToString() ?? "";
    public string CoverText => HasCover ? "yes" : "no";
    public string LengthText => $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}";
    public string BitrateText => Bitrate > 0 ? $"{Bitrate}" : "?";
    public bool IsLowBitrate => Bitrate is > 0 and < Music.LowBitrate;
}

public static class Music
{
    public const int LowBitrate = 128;
    private static readonly string[] Extensions = [".mp3", ".m4a"];

    /// <summary>Reads every MP3/M4A under <paramref name="root"/>. Unreadable files are counted, not listed.</summary>
    public static List<Song> Scan(string root, out int unreadable)
    {
        var songs = new List<Song>();
        unreadable = 0;
        var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            try { songs.Add(Read(f, root)); }
            catch { unreadable++; }
        }
        return songs;
    }

    public static Song Read(string path, string root = "")
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        var folder = Path.GetRelativePath(root.Length > 0 ? root : Path.GetDirectoryName(path)!, Path.GetDirectoryName(path)!);
        return new Song
        {
            Path = path,
            Folder = folder == "." ? "" : folder,
            Title = tag.Title ?? "",
            Artist = tag.JoinedPerformers ?? "",
            Album = tag.Album ?? "",
            Track = tag.Track,
            HasCover = tag.Pictures.Length > 0,
            Duration = file.Properties.Duration,
            Bitrate = file.Properties.AudioBitrate,
        };
    }

    /// <summary>
    /// What ShibaDownloader's file name says: "01 - Artist - Title" inside an album/playlist folder
    /// (album = folder name, track = number) or "Artist - Title" for a single. Null if neither.
    /// </summary>
    public static (string Artist, string Title, string? Album, uint Track)? ParseName(string path)
    {
        // Spotify joins artists with ", "; tags read (and search) better with a plain space.
        var name = Path.GetFileNameWithoutExtension(path).Replace(' ', ' ');
        // The artist never contains " - " (ShibaDownloader joins artists with ", "); the title may.
        var m = Regex.Match(name, @"^(\d{1,3}) - (.+?) - (.+)$");
        if (m.Success)
            return (m.Groups[2].Value.Trim(), Clean(m.Groups[3].Value),
                    Path.GetFileName(Path.GetDirectoryName(path)), uint.Parse(m.Groups[1].Value));
        m = Regex.Match(name, @"^(.+?) - (.+)$");
        return m.Success ? (m.Groups[1].Value.Trim(), Clean(m.Groups[2].Value), null, 0) : null;

        // ShibaDownloader cuts long names at 80 chars, which can leave a dangling " -".
        static string Clean(string s) => s.Trim().TrimEnd('-', ' ');
    }

    /// <summary>Fills the New* fields for tags that are empty and the file name can supply. Existing tags are kept:
    /// file names are lossy (ShibaDownloader strips characters like / : ? and truncates).</summary>
    public static void Propose(Song s)
    {
        s.NewTitle = s.NewArtist = s.NewAlbum = null;
        s.NewTrack = null;
        if (ParseName(s.Path) is not { } p) return;
        if (s.Title.Length == 0 && p.Title.Length > 0) s.NewTitle = p.Title;
        if (s.Artist.Length == 0 && p.Artist.Length > 0) s.NewArtist = p.Artist;
        if (s.Album.Length == 0 && !string.IsNullOrEmpty(p.Album)) s.NewAlbum = p.Album;
        if (s.Track == 0 && p.Track > 0) s.NewTrack = p.Track;
        s.Apply = s.HasChanges;
    }

    public static void Write(Song s)
    {
        using var file = TagLib.File.Create(s.Path);
        // Untagged files have no tag block to write into; create the format's native one.
        file.GetTag(file is TagLib.Mpeg4.File ? TagLib.TagTypes.Apple : TagLib.TagTypes.Id3v2, true);
        var tag = file.Tag;
        if (s.NewTitle != null) tag.Title = s.NewTitle;
        if (s.NewArtist != null) tag.Performers = [s.NewArtist];
        if (s.NewAlbum != null) tag.Album = s.NewAlbum;
        if (s.NewTrack != null) tag.Track = s.NewTrack.Value;
        if (s.NewCover != null)
            tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(s.NewCover)) { Type = TagLib.PictureType.FrontCover, MimeType = "image/jpeg" }];
        file.Save();
    }

    // ---- covers (iTunes Search API: free, no key, ~20 requests/minute) ----

    /// <summary>What a cover lookup searches for: songs in an album/playlist folder share one album lookup,
    /// a single (no album) gets its own song lookup.</summary>
    public static (string Artist, string? Album, string Title) CoverQuery(Song s)
    {
        var album = s.Album.Length > 0 ? s.Album : ParseName(s.Path)?.Album;
        return (EffectiveArtist(s), string.IsNullOrEmpty(album) ? null : album, EffectiveTitle(s));
    }

    /// <summary>Front cover JPEG (600x600) for an album, or for a single song when album is null. Null = no match.
    /// Throws HttpRequestException (403/429 when iTunes rate-limits).</summary>
    public static async Task<byte[]?> FindCoverAsync(HttpClient http, string artist, string? album, string title)
    {
        var term = Uri.EscapeDataString($"{MainArtist(artist)} {album ?? NormTitle(title)}");
        var json = await http.GetStringAsync(
            $"https://itunes.apple.com/search?term={term}&entity={(album != null ? "album" : "song")}&limit=10").ConfigureAwait(false);
        var url = PickArtwork(json, artist, album, title);
        return url == null ? null : await http.GetByteArrayAsync(url).ConfigureAwait(false);
    }

    /// <summary>First search result whose artist matches and whose album (or, for a single, song title) matches;
    /// its artwork URL upsized from 100x100 to 600x600. Better no cover than a stranger's.</summary>
    public static string? PickArtwork(string json, string artist, string? album, string title)
    {
        var want = NormTitle(MainArtist(artist));
        if (want.Length == 0) return null;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var r in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            if (!NormTitle(Str(r, "artistName")).Contains(want)) continue;
            var ok = album != null
                ? Prefix(NormTitle(Str(r, "collectionName")), NormTitle(album)) // "Global Warming" ~ "Global Warming: Meltdown (Deluxe)"
                : NormTitle(Str(r, "trackName")) == NormTitle(title);
            var art = Str(r, "artworkUrl100");
            if (ok && art.Length > 0) return art.Replace("100x100bb", "600x600bb");
        }
        return null;

        static string Str(System.Text.Json.JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString()! : "";
        // either starts with the other: also covers album folder names ShibaDownloader cut at 80 chars
        static bool Prefix(string a, string b) => a.Length > 0 && b.Length > 0 && (a.StartsWith(b) || b.StartsWith(a));
    }

    /// <summary>
    /// Groups copies of the same song: same title (ignoring case, punctuation and "(feat. ...)") and either the
    /// same main artist or a length within 2 s. Each group's best copy (highest bitrate, then has cover) is the
    /// keeper; the rest come pre-selected for the Recycle Bin.
    /// </summary>
    public static List<List<Song>> FindDuplicates(IEnumerable<Song> songs)
    {
        var groups = new List<List<Song>>();
        // ponytail: compares against each group's first song, O(n * groups); fine for a music folder.
        foreach (var s in songs)
        {
            var g = groups.FirstOrDefault(g => SameSong(g[0], s));
            if (g is null) groups.Add([s]); else g.Add(s);
        }
        groups.RemoveAll(g => g.Count < 2);
        foreach (var g in groups)
        {
            var keeper = g.OrderByDescending(s => s.Bitrate).ThenByDescending(s => s.HasCover).First();
            var header = $"{EffectiveArtist(keeper)} - {EffectiveTitle(keeper)}  ·  {g.Count} copies";
            foreach (var s in g) { s.IsKeeper = s == keeper; s.Recycle = s != keeper; s.Group = header; }
        }
        return groups;
    }

    public static bool SameSong(Song a, Song b)
    {
        var t = NormTitle(EffectiveTitle(a));
        if (t.Length == 0 || t != NormTitle(EffectiveTitle(b))) return false;
        var artistA = MainArtist(EffectiveArtist(a));
        return (artistA.Length > 0 && artistA == MainArtist(EffectiveArtist(b)))
            || Math.Abs((a.Duration - b.Duration).TotalSeconds) <= 2;
    }

    private static string EffectiveTitle(Song s) =>
        s.Title.Length > 0 ? s.Title : ParseName(s.Path)?.Title ?? Path.GetFileNameWithoutExtension(s.Path);

    private static string EffectiveArtist(Song s) => s.Artist.Length > 0 ? s.Artist : ParseName(s.Path)?.Artist ?? "";

    private static string NormTitle(string s) =>
        Regex.Replace(Regex.Replace(s.ToLowerInvariant(), @"[\(\[][^\)\]]*[\)\]]?", " "), @"[^\p{L}\p{N}]+", " ").Trim();

    private static string MainArtist(string s) =>
        Regex.Split(s.ToLowerInvariant(), @",|&|\bfeat\.?|\bft\.?|;|/")[0].Trim();

    public static void Recycle(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
}
