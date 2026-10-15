using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ShibaTag;

public partial class MainWindow : Window
{
    private string? _root;
    private List<Song> _songs = [];
    private List<Song> _dupes = [];

    /// <param name="folder">Scanned on open: `ShibaTag.exe "D:\Music\Album"` or a folder dropped on the exe.</param>
    public MainWindow(string? folder = null)
    {
        InitializeComponent();
        if (folder != null) Loaded += (_, _) => _ = ScanAsync(folder);
    }

    private bool DupesMode => DupesTab.IsChecked == true;

    // ---- chrome ----

    private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- folder ----

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        var pick = new OpenFolderDialog
        {
            Title = "Which music folder should I tidy?",
            InitialDirectory = _root ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };
        if (pick.ShowDialog(this) == true) _ = ScanAsync(pick.FolderName);
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (_root != null) _ = ScanAsync(_root);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } p && Directory.Exists(p[0])
            ? DragDropEffects.Link
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (ChooseButton.IsEnabled && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } p && Directory.Exists(p[0]))
            _ = ScanAsync(p[0]);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_root != null) Process.Start(new ProcessStartInfo(_root) { UseShellExecute = true });
    }

    private async Task ScanAsync(string root)
    {
        _root = root;
        FolderText.Text = root;
        FolderText.Foreground = (Brush)FindResource("Text");
        SetBusy(true);
        SetMascot(ShibaMood.Neutral, "Sniffing through your music...");
        SetStatus("Scanning...", null);
        try
        {
            var (songs, unreadable) = await Task.Run(() =>
            {
                var list = Music.Scan(root, out var bad);
                foreach (var s in list) Music.Propose(s);
                return (list, bad);
            });
            _songs = songs;
            _dupes = Music.FindDuplicates(songs).SelectMany(g => g).ToList();

            TagList.ItemsSource = _songs;
            var view = new ListCollectionView(_dupes);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Song.Group)));
            DupList.ItemsSource = view;
            NoDupesText.Visibility = _dupes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AllCheck.IsChecked = _songs.Any(s => s.Apply);

            var fixable = _songs.Count(s => s.HasChanges);
            var groups = _dupes.Count(s => s.IsKeeper);
            var low = _songs.Count(s => s.IsLowBitrate);
            SummaryText.Text = $"{_songs.Count} files  ·  {fixable} need tags  ·  {groups} duplicated  ·  {low} low bitrate"
                             + (unreadable > 0 ? $"  ·  {unreadable} unreadable" : "");
            SetStatus(_songs.Count == 0 ? "No MP3 or M4A files in that folder." : $"Scanned {_songs.Count} files.",
                      _songs.Count > 0);
            SetMascot(_songs.Count == 0 ? ShibaMood.Sad : ShibaMood.Happy,
                      _songs.Count == 0 ? "Hmm, no music in there." :
                      fixable > 0 ? $"{fixable} files are missing tags. Check the orange bits, then write!" :
                      groups > 0 ? "Tags look good! I did sniff out some duplicates though." :
                      "Everything's tidy. Good human!");
            Step1.IsEnabled = false;
            Step2.IsEnabled = _songs.Count == 0;
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't scan: " + ex.Message, false);
            SetMascot(ShibaMood.Sad, "Ruff. I couldn't read that folder.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---- views + actions ----

    private void View_Changed(object sender, RoutedEventArgs e)
    {
        if (TagsView is null) return; // fires during InitializeComponent
        TagsView.Visibility = DupesMode ? Visibility.Collapsed : Visibility.Visible;
        DupesView.Visibility = DupesMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButton();
    }

    private void AllCheck_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _songs.Where(s => s.HasChanges)) s.Apply = AllCheck.IsChecked == true;
        TagList.Items.Refresh();
    }

    private void UpdateActionButton()
    {
        ActionButton.Content = DupesMode ? "Move ticked copies to the Recycle Bin" : "Write tags to ticked files";
        ActionButton.IsEnabled = ChooseButton.IsEnabled && (DupesMode ? _dupes.Count > 0 : _songs.Any(s => s.HasChanges));
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        var dupes = DupesMode;
        var picked = dupes ? _dupes.Where(s => s.Recycle).ToList() : _songs.Where(s => s.Apply && s.HasChanges).ToList();
        if (picked.Count == 0)
        {
            SetStatus("Nothing ticked.", null);
            return;
        }
        if (dupes && _dupes.GroupBy(s => s.Group).Any(g => g.All(s => s.Recycle)) &&
            MessageBox.Show(this, "For at least one song every copy is ticked, so none would be left. Continue anyway?",
                            "ShibaTag", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var question = dupes
            ? $"Move {picked.Count} file(s) to the Recycle Bin?"
            : $"Write the new tags to {picked.Count} file(s)? Only the orange values are written.";
        if (MessageBox.Show(this, question, "ShibaTag", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        SetBusy(true);
        SetMascot(ShibaMood.Neutral, dupes ? "Carrying them to the bin..." : "Writing tags, paw by paw...");
        var errors = await Task.Run(() =>
        {
            var failed = new List<string>();
            foreach (var s in picked)
            {
                try
                {
                    if (dupes) Music.Recycle(s.Path); else Music.Write(s);
                }
                catch (Exception ex) { failed.Add($"{s.FileName}: {ex.Message}"); }
            }
            return failed;
        });
        SetBusy(false);
        Step3.IsEnabled = false;
        await ScanAsync(_root!);

        var done = picked.Count - errors.Count;
        var verb = dupes ? "Moved to Recycle Bin" : "Tagged";
        SetStatus(errors.Count == 0 ? $"{verb}: {done} file(s)." : $"{verb}: {done}, failed: {errors.Count} - {errors[0]}",
                  errors.Count == 0);
        if (errors.Count > 0) SetMascot(ShibaMood.Sad, "Some files wouldn't budge. Are they open in a player?");
    }

    // ---- misc ----

    private void SetBusy(bool busy)
    {
        ChooseButton.IsEnabled = !busy;
        RescanButton.IsEnabled = !busy && _root != null;
        OpenFolderButton.IsEnabled = !busy && _root != null;
        UpdateActionButton();
        Cursor = busy ? Cursors.Wait : null;
    }

    private void SetMascot(ShibaMood mood, string text)
    {
        Mascot.Mood = mood;
        BubbleText.Text = text;
    }

    /// <summary>ok: true = green, false = red, null = neutral.</summary>
    private void SetStatus(string text, bool? ok)
    {
        StatusText.Text = text;
        StatusDot.Fill = ok switch
        {
            true => new SolidColorBrush(Color.FromRgb(0x3F, 0xA3, 0x5B)),
            false => new SolidColorBrush(Color.FromRgb(0xD2, 0x28, 0x37)),
            null => (Brush)FindResource("Muted")
        };
    }
}
