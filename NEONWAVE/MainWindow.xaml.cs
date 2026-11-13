using Microsoft.Win32;
using NAudio.Wave;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace NEONWAVE;

public sealed class Track : INotifyPropertyChanged
{
    private bool isPlaying;

    public string Path { get; set; } = "";
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);
    public bool IsPlaying
    {
        get => isPlaying;
        set
        {
            if (isPlaying == value) return;
            isPlaying = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayingMarker)));
        }
    }
    public string PlayingMarker => IsPlaying ? "●" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    public override string ToString() => DisplayName;
}

public sealed class MusicFolder
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public ObservableCollection<Track> Tracks { get; } = new();
    public override string ToString() => Name;
}

public sealed class MusicPlaylist
{
    public string Name { get; set; } = "";
    public ObservableCollection<Track> Tracks { get; } = new();
    public override string ToString() => Name;
}

public partial class MainWindow : Window
{
    private readonly ObservableCollection<MusicFolder> folders = new();
    private readonly ObservableCollection<MusicPlaylist> playlists = new();
    private readonly List<Track> allTracks = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private List<Track> activeTracks = new();
    private AudioFileReader? reader;
    private WaveOutEvent? output;
    private bool changingSeek;
    private bool draggingSeek;
    private int currentIndex = -1;
    private bool advanceQueued;
    private double expandedWindowHeight;

    private static readonly string[] SupportedExtensions =
        [".mp3", ".wav", ".aiff", ".aif", ".wma", ".aac", ".m4a", ".flac"];

    public MainWindow()
    {
        InitializeComponent();
        expandedWindowHeight = Height;
        FolderList.ItemsSource = folders;
        PlaylistList.ItemsSource = playlists;
        timer.Tick += (_, _) => UpdateDisplay();
        timer.Start();
        Closed += (_, _) => DisposeAudio();
    }

    private static bool IsAudioFile(string path) =>
        SupportedExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    private void ToggleMusicLibrary_Click(object sender, RoutedEventArgs e)
    {
        var expand = LibraryContent.Visibility != Visibility.Visible;

        // The curtain should shrink the actual application window, not leave
        // an empty lower half behind the compact library header.
        if (expand)
        {
            MinHeight = 560;
            Height = Math.Max(expandedWindowHeight, MinHeight);
        }
        else
        {
            expandedWindowHeight = Height;
            MinHeight = 400;
            Height = 450;
        }

        LibraryActions.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        LibraryContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        LibraryContent.Height = expand ? double.NaN : 0;
        QueueCount.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        LibraryRow.Height = expand
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
        MusicLibraryPanel.Height = expand ? double.NaN : 48;
        MusicLibraryPanel.Padding = expand
            ? new Thickness(14)
            : new Thickness(14, 8, 14, 8);
        LibraryHeader.Margin = expand
            ? new Thickness(0, 0, 0, 10)
            : new Thickness(0);
        LibraryToggleText.Text = expand ? "CLOSE MUSIC LIBRARY" : "OPEN MUSIC LIBRARY";
        LibraryToggleButton.Content = expand ? "−" : "+";
    }

    private MusicFolder GetOrCreateFolder(string folderPath, string? displayName = null)
    {
        var normalized = System.IO.Path.GetFullPath(folderPath);
        var existing = folders.FirstOrDefault(f =>
            string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var folder = new MusicFolder
        {
            Path = normalized,
            Name = displayName ?? System.IO.Path.GetFileName(normalized.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
        };
        if (string.IsNullOrWhiteSpace(folder.Name)) folder.Name = normalized;
        folders.Add(folder);
        return folder;
    }

    private void AddFilesToFolder(IEnumerable<string> paths, string folderPath, string? displayName = null)
    {
        var folder = GetOrCreateFolder(folderPath, displayName);
        foreach (var path in paths.Where(File.Exists).Where(IsAudioFile))
        {
            if (allTracks.Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var track = new Track { Path = System.IO.Path.GetFullPath(path) };
            folder.Tracks.Add(track);
            allTracks.Add(track);
        }

        if (folder.Tracks.Count == 0)
        {
            folders.Remove(folder);
            return;
        }

        FolderList.SelectedItem = folder;
        ShowFolder(folder);
        UpdateQueueCount();
    }

    private void AddIndividualFiles(IEnumerable<string> paths)
    {
        var selectedPaths = paths.Where(File.Exists).Where(IsAudioFile)
            .Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var group in selectedPaths
                     .GroupBy(p => System.IO.Path.GetDirectoryName(p) ?? ""))
            AddFilesToFolder(group, group.Key);

        // Files added together form one playback queue, even when they came from
        // different directories. Selecting a folder later switches back to that folder.
        activeTracks = selectedPaths
            .Select(path => allTracks.FirstOrDefault(t =>
                string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)))
            .Where(track => track != null)
            .Cast<Track>()
            .ToList();
        currentIndex = -1;
    }

    private void UpdateQueueCount() =>
        QueueCount.Text = $"{allTracks.Count} TRACKS  •  {folders.Count} FOLDERS";

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add audio to DARKWAVE",
            Filter = "Audio files|*.mp3;*.wav;*.aiff;*.aif;*.wma;*.aac;*.m4a;*.flac|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true) AddIndividualFiles(dialog.FileNames);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a music folder", Multiselect = false };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var files = Directory.EnumerateFiles(dialog.FolderName, "*.*", SearchOption.AllDirectories).ToList();
            AddFilesToFolder(files, dialog.FolderName, System.IO.Path.GetFileName(dialog.FolderName.TrimEnd(System.IO.Path.DirectorySeparatorChar)));
            TrackInfo.Text = $"Imported audio from {dialog.FolderName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not scan that folder. Some folders may be inaccessible.\n\n{ex.Message}",
                "DARKWAVE folder import", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e) =>
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;
        AddIndividualFiles(dropped.Where(File.Exists));

        foreach (var item in dropped.Where(Directory.Exists))
        {
            try
            {
                var files = Directory.EnumerateFiles(item, "*.*", SearchOption.AllDirectories).ToList();
                AddFilesToFolder(files, item, System.IO.Path.GetFileName(item.TrimEnd(System.IO.Path.DirectorySeparatorChar)));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not fully scan {item}.\n\n{ex.Message}", "DARKWAVE folder import",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderList.SelectedItem is MusicFolder folder)
            ShowFolder(folder);
    }

    private void ShowFolder(MusicFolder folder)
    {
        PlaylistList.SelectedItem = null;
        FolderTracks.ItemsSource = folder.Tracks;
        FolderContentsTitle.Text = $"TRACKS  •  {folder.Name.ToUpperInvariant()}";
        activeTracks = folder.Tracks.ToList();
        currentIndex = -1;
        FolderTracks.SelectedIndex = -1;
    }

    private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlaylistList.SelectedItem is MusicPlaylist playlist)
            ShowPlaylist(playlist);
    }

    private void ShowPlaylist(MusicPlaylist playlist)
    {
        FolderList.SelectedItem = null;
        FolderTracks.ItemsSource = playlist.Tracks;
        FolderContentsTitle.Text = $"TRACKS  •  {playlist.Name.ToUpperInvariant()}";
        activeTracks = playlist.Tracks.ToList();
        currentIndex = -1;
        FolderTracks.SelectedIndex = -1;
    }

    private void FolderTracks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FolderTracks.SelectedItem is not Track track) return;
        activeTracks = (FolderTracks.ItemsSource as ObservableCollection<Track>)?.ToList() ?? activeTracks;
        var index = activeTracks.IndexOf(track);
        if (index >= 0) PlayIndex(index);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (output?.PlaybackState == PlaybackState.Playing)
        {
            output.Pause();
            if (currentIndex >= 0 && currentIndex < activeTracks.Count)
                activeTracks[currentIndex].IsPlaying = false;
            SetLogoPulse(false);
            PlayButton.Content = "▶";
        }
        else if (output?.PlaybackState == PlaybackState.Paused)
        {
            output.Play();
            if (currentIndex >= 0 && currentIndex < activeTracks.Count)
                activeTracks[currentIndex].IsPlaying = true;
            SetLogoPulse(true);
            PlayButton.Content = "Ⅱ";
        }
        else if (activeTracks.Count > 0)
        {
            PlayIndex(currentIndex < 0 ? 0 : currentIndex);
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (reader != null && reader.CurrentTime.TotalSeconds > 3) reader.CurrentTime = TimeSpan.Zero;
        else if (activeTracks.Count > 0) PlayIndex((Math.Max(0, currentIndex - 1) + activeTracks.Count) % activeTracks.Count);
    }

    private void Next_Click(object sender, RoutedEventArgs e) => NextTrack();

    private void NextTrack()
    {
        if (activeTracks.Count > 0) PlayIndex((currentIndex + 1) % activeTracks.Count);
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        SetLogoPulse(false);
        if (currentIndex >= 0 && currentIndex < activeTracks.Count)
            activeTracks[currentIndex].IsPlaying = false;
        output?.Stop();
        if (reader != null) reader.Position = 0;
        PlayButton.Content = "▶";
        UpdateDisplay();
    }

    private void PlayIndex(int index)
    {
        if (index < 0 || index >= activeTracks.Count) return;
        try
        {
            advanceQueued = false;
            DisposeAudio();
            currentIndex = index;
            var track = activeTracks[index];
            FolderTracks.SelectedItem = track;
            reader = new AudioFileReader(track.Path) { Volume = (float)(VolumeSlider.Value / 100.0) };

            var playbackReader = reader;
            var playbackOutput = new WaveOutEvent();
            output = playbackOutput;
            playbackOutput.Init(playbackReader);
            playbackOutput.PlaybackStopped += (_, args) =>
            {
                if (args.Exception != null) return;
                Dispatcher.BeginInvoke(() => TryAdvanceEndedTrack(playbackOutput, playbackReader));
            };
            playbackOutput.Play();
            track.IsPlaying = true;
            SetLogoPulse(true);
            PlayButton.Content = "Ⅱ";
            TrackTitle.Text = track.DisplayName;
            TrackInfo.Text = track.Path;
            changingSeek = true;
            SeekSlider.Value = 0;
            changingSeek = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not play this file.\n\n{ex.Message}", "DARKWAVE audio error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetLogoPulse(bool isPlaying)
    {
        if (LogoGlow == null) return;
        if (LogoGlow.Effect is not DropShadowEffect glow)
        {
            glow = new DropShadowEffect { ShadowDepth = 0, Opacity = 0.9 };
            LogoGlow.Effect = glow;
        }

        if (isPlaying)
        {
            var pulse = new DoubleAnimation(0.72, 1.0, TimeSpan.FromSeconds(0.85))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            var glowPulse = new DoubleAnimation(16, 34, TimeSpan.FromSeconds(0.85))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            var colorPulse = new ColorAnimation(Color.FromRgb(255, 59, 200), Color.FromRgb(54, 245, 255),
                TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            LogoGlow.BeginAnimation(UIElement.OpacityProperty, pulse);
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowPulse);
            glow.BeginAnimation(DropShadowEffect.ColorProperty, colorPulse);
        }
        else
        {
            LogoGlow.BeginAnimation(UIElement.OpacityProperty, null);
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            glow.BeginAnimation(DropShadowEffect.ColorProperty, null);
            LogoGlow.Opacity = 1;
            glow.BlurRadius = 24;
            glow.Color = Color.FromRgb(255, 50, 210);
        }
    }

    private void DisposeAudio()
    {
        SetLogoPulse(false);
        foreach (var track in allTracks) track.IsPlaying = false;
        foreach (var track in activeTracks) track.IsPlaying = false;
        if (output != null)
        {
            var oldOutput = output;
            output = null;
            oldOutput.Stop();
            oldOutput.Dispose();
        }
        reader?.Dispose();
        reader = null;
    }

    private void TryAdvanceEndedTrack(WaveOutEvent expectedOutput, AudioFileReader expectedReader)
    {
        // PlaybackStopped can arrive on an audio callback thread. Only inspect the
        // current playback objects on the UI thread, and ignore callbacks from old tracks.
        if (!ReferenceEquals(output, expectedOutput) ||
            !ReferenceEquals(reader, expectedReader) ||
            advanceQueued ||
            expectedOutput.PlaybackState != PlaybackState.Stopped)
            return;

        var reachedEnd =
            expectedReader.Position >= expectedReader.Length - 1 ||
            expectedReader.CurrentTime >= expectedReader.TotalTime - TimeSpan.FromMilliseconds(500);

        if (!reachedEnd) return;

        advanceQueued = true;
        NextTrack();
    }

    private void UpdateDisplay()
    {
        if (output != null && reader != null)
            TryAdvanceEndedTrack(output, reader);

        if (reader == null) return;
        ElapsedText.Text = FormatTime(reader.CurrentTime);
        DurationText.Text = FormatTime(reader.TotalTime);
        if (!draggingSeek && reader.TotalTime.TotalSeconds > 0)
        {
            changingSeek = true;
            SeekSlider.Value = reader.CurrentTime.TotalSeconds / reader.TotalTime.TotalSeconds * 100;
            changingSeek = false;
        }
    }

    private static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!changingSeek && reader != null && draggingSeek)
            reader.CurrentTime = TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds * SeekSlider.Value / 100);
    }

    private void SeekSlider_MouseDown(object sender, MouseButtonEventArgs e) => draggingSeek = true;

    private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (reader != null)
            reader.CurrentTime = TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds * SeekSlider.Value / 100);
        draggingSeek = false;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText != null) VolumeText.Text = $"{(int)VolumeSlider.Value}%";
        if (reader != null) reader.Volume = (float)(VolumeSlider.Value / 100.0);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (FolderTracks.SelectedItem is not Track selected) return;
        if (reader != null && currentIndex >= 0 && currentIndex < activeTracks.Count &&
            ReferenceEquals(activeTracks[currentIndex], selected))
        {
            DisposeAudio();
            PlayButton.Content = "▶";
            TrackTitle.Text = "No track loaded";
        }

        var selectedPlaylist = PlaylistList.SelectedItem as MusicPlaylist;
        selectedPlaylist?.Tracks.Remove(selected);
        var folder = folders.FirstOrDefault(f => f.Tracks.Contains(selected));
        folder?.Tracks.Remove(selected);
        allTracks.Remove(selected);
        if (folder != null && folder.Tracks.Count == 0) folders.Remove(folder);
        if (selectedPlaylist != null) ShowPlaylist(selectedPlaylist);
        else if (folder != null && folders.Contains(folder)) ShowFolder(folder);
        else if (folders.Count > 0) FolderList.SelectedIndex = 0;
        else { FolderTracks.ItemsSource = null; activeTracks.Clear(); currentIndex = -1; }
        UpdateQueueCount();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        DisposeAudio();
        folders.Clear();
        playlists.Clear();
        allTracks.Clear();
        activeTracks.Clear();
        currentIndex = -1;
        FolderTracks.ItemsSource = null;
        FolderContentsTitle.Text = "TRACKS IN SELECTED FOLDER";
        FolderList.SelectedIndex = -1;
        TrackTitle.Text = "No track loaded";
        TrackInfo.Text = "Add an audio folder or drop music files here";
        QueueCount.Text = "0 TRACKS  •  ADD A FOLDER TO BEGIN";
        PlayButton.Content = "▶";
        changingSeek = true;
        SeekSlider.Value = 0;
        changingSeek = false;
        ElapsedText.Text = "0:00";
        DurationText.Text = "0:00";
    }

    private void SavePlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "DARKWAVE playlist|*.dwv.json", FileName = "My Playlist.dwv.json" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var paths = allTracks.Select(t => t.Path).ToArray();
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(paths,
                new JsonSerializerOptions { WriteIndented = true }));
            var playlist = CreatePlaylist(System.IO.Path.GetFileNameWithoutExtension(
                System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)), paths);
            AddOrReplacePlaylist(playlist);
            PlaylistList.SelectedItem = playlist;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Playlist save error"); }
    }

    private void LoadPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "DARKWAVE playlist|*.dwv.json;*.json", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? [];
                AddIndividualFiles(paths.Where(File.Exists));
                var playlist = CreatePlaylist(System.IO.Path.GetFileNameWithoutExtension(
                    System.IO.Path.GetFileNameWithoutExtension(file)), paths);
                AddOrReplacePlaylist(playlist);
                PlaylistList.SelectedItem = playlist;
            }
            catch (Exception ex) { MessageBox.Show($"Could not load {file}.\\n\\n{ex.Message}", "Playlist error"); }
        }
    }

    private MusicPlaylist CreatePlaylist(string name, IEnumerable<string> paths)
    {
        var playlist = new MusicPlaylist { Name = name };
        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            var track = allTracks.FirstOrDefault(t =>
                string.Equals(t.Path, fullPath, StringComparison.OrdinalIgnoreCase))
                ?? new Track { Path = fullPath };
            playlist.Tracks.Add(track);
        }
        return playlist;
    }

    private void AddOrReplacePlaylist(MusicPlaylist playlist)
    {
        var existing = playlists.FirstOrDefault(p =>
            string.Equals(p.Name, playlist.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) playlists.Remove(existing);
        playlists.Add(playlist);
        UpdateQueueCount();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !(Keyboard.FocusedElement is Slider))
        {
            Play_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            Add_Click(this, new RoutedEventArgs());
        else if (e.Key == Key.Left && reader != null)
            reader.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, reader.CurrentTime.TotalSeconds - 5));
        else if (e.Key == Key.Right && reader != null)
            reader.CurrentTime = TimeSpan.FromSeconds(Math.Min(reader.TotalTime.TotalSeconds, reader.CurrentTime.TotalSeconds + 5));
    }
}