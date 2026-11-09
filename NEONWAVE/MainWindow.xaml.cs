using Microsoft.Win32;
using NAudio.Wave;
using System.Collections.ObjectModel;
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

public sealed class Track
{
    public string Path { get; set; } = "";
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);
    public override string ToString() => DisplayName;
}

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Track> tracks = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private AudioFileReader? reader;
    private WaveOutEvent? output;
    private bool changingSeek;
    private bool draggingSeek;
    private int currentIndex = -1;
    private bool advanceQueued;

    private static readonly string[] SupportedExtensions =
        [".mp3", ".wav", ".aiff", ".aif", ".wma", ".aac", ".m4a", ".flac"];

    public MainWindow()
    {
        InitializeComponent();
        Playlist.ItemsSource = tracks;
        timer.Tick += (_, _) => UpdateDisplay();
        timer.Start();
        Closed += (_, _) => DisposeAudio();
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists)
            .Where(p => SupportedExtensions.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant())))
        {
            if (!tracks.Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)))
                tracks.Add(new Track { Path = path });
        }

        UpdateQueueCount();
        if (currentIndex < 0 && tracks.Count > 0) Playlist.SelectedIndex = 0;
    }

    private void UpdateQueueCount() =>
        QueueCount.Text = $"{tracks.Count} TRACKS  •  NEONWAVE QUEUE";

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add audio to NEONWAVE",
            Filter = "Audio files|*.mp3;*.wav;*.aiff;*.aif;*.wma;*.aac;*.m4a;*.flac|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true) AddFiles(dialog.FileNames);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a music folder",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var files = Directory.EnumerateFiles(dialog.FolderName, "*.*", SearchOption.AllDirectories);
            AddFiles(files);
            TrackInfo.Text = $"Imported audio from {dialog.FolderName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not scan that folder. Some folders may be inaccessible.\n\n{ex.Message}",
                "NEONWAVE folder import", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e) =>
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;

        var files = new List<string>();
        foreach (var item in dropped)
        {
            if (File.Exists(item)) files.Add(item);
            else if (Directory.Exists(item))
            {
                try { files.AddRange(Directory.EnumerateFiles(item, "*.*", SearchOption.AllDirectories)); }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not fully scan {item}.\n\n{ex.Message}", "NEONWAVE folder import",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        AddFiles(files);
    }

    private void Playlist_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Playlist.SelectedIndex >= 0) currentIndex = Playlist.SelectedIndex;
    }

    private void Playlist_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Playlist.SelectedIndex >= 0) PlayIndex(Playlist.SelectedIndex);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (output?.PlaybackState == PlaybackState.Playing)
        {
            output.Pause();
            SetLogoPulse(false);
            PlayButton.Content = "▶";
        }
        else if (output?.PlaybackState == PlaybackState.Paused)
        {
            output.Play();
            SetLogoPulse(true);
            PlayButton.Content = "Ⅱ";
        }
        else if (tracks.Count > 0)
        {
            PlayIndex(currentIndex < 0 ? 0 : currentIndex);
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (reader != null && reader.CurrentTime.TotalSeconds > 3) reader.CurrentTime = TimeSpan.Zero;
        else if (tracks.Count > 0) PlayIndex((Math.Max(0, currentIndex - 1) + tracks.Count) % tracks.Count);
    }

    private void Next_Click(object sender, RoutedEventArgs e) => NextTrack();

    private void NextTrack()
    {
        if (tracks.Count > 0) PlayIndex((currentIndex + 1) % tracks.Count);
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        SetLogoPulse(false);
        output?.Stop();
        if (reader != null) reader.Position = 0;
        PlayButton.Content = "▶";
        UpdateDisplay();
    }

    private void PlayIndex(int index)
    {
        if (index < 0 || index >= tracks.Count) return;

        try
        {
            advanceQueued = false;
            DisposeAudio();
            currentIndex = index;
            Playlist.SelectedIndex = index;
            reader = new AudioFileReader(tracks[index].Path)
            {
                Volume = (float)(VolumeSlider.Value / 100.0)
            };

            var playbackReader = reader;
            var playbackOutput = new WaveOutEvent();
            output = playbackOutput;
            playbackOutput.Init(playbackReader);
            playbackOutput.PlaybackStopped += (_, args) =>
            {
                // Never inspect the reader on the audio callback thread: it may already
                // have been disposed because the user skipped, stopped, or closed the app.
                if (args.Exception != null) return;

                Dispatcher.BeginInvoke(() =>
                {
                    // Check identity before touching the reader. A delayed callback from
                    // a disposed output must be ignored.
                    if (!ReferenceEquals(output, playbackOutput) ||
                        !ReferenceEquals(reader, playbackReader))
                        return;

                    if (!advanceQueued && playbackReader.Position >= playbackReader.Length - 1)
                    {
                        advanceQueued = true;
                        NextTrack();
                    }
                });
            };
            playbackOutput.Play();
            SetLogoPulse(true);
            PlayButton.Content = "Ⅱ";
            TrackTitle.Text = tracks[index].DisplayName;
            TrackInfo.Text = tracks[index].Path;
            changingSeek = true;
            SeekSlider.Value = 0;
            changingSeek = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not play this file.\n\n{ex.Message}", "NEONWAVE audio error",
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
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            var glowPulse = new DoubleAnimation(16, 34, TimeSpan.FromSeconds(0.85))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            var colorPulse = new ColorAnimation(Color.FromRgb(255, 59, 200), Color.FromRgb(54, 245, 255),
                TimeSpan.FromSeconds(1.1))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

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

    private void UpdateDisplay()
    {
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

    private static string FormatTime(TimeSpan time) =>
        $"{(int)time.TotalMinutes}:{time.Seconds:00}";

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
        int index = Playlist.SelectedIndex;
        if (index < 0) return;

        bool removingCurrent = index == currentIndex && output != null;
        if (removingCurrent)
        {
            DisposeAudio();
            PlayButton.Content = "▶";
            TrackTitle.Text = "No track loaded";
        }

        tracks.RemoveAt(index);
        if (tracks.Count == 0) currentIndex = -1;
        else
        {
            currentIndex = Math.Min(index, tracks.Count - 1);
            Playlist.SelectedIndex = currentIndex;
        }
        UpdateQueueCount();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        DisposeAudio();
        tracks.Clear();
        currentIndex = -1;
        Playlist.SelectedIndex = -1;
        TrackTitle.Text = "No track loaded";
        TrackInfo.Text = "Add an audio folder or drop music files here";
        QueueCount.Text = "0 TRACKS  •  DROP FILES OR FOLDERS TO ADD";
        PlayButton.Content = "▶";
        changingSeek = true;
        SeekSlider.Value = 0;
        changingSeek = false;
        ElapsedText.Text = "0:00";
        DurationText.Text = "0:00";
    }

    private void SavePlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "NEONWAVE playlist|*.nwv.json",
            FileName = "My Playlist.nwv.json"
        };
        if (dialog.ShowDialog() == true)
            File.WriteAllText(dialog.FileName,
                JsonSerializer.Serialize(tracks.Select(t => t.Path), new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LoadPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "NEONWAVE playlist|*.nwv.json;*.json" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            AddFiles(JsonSerializer.Deserialize<string[]>(File.ReadAllText(dialog.FileName)) ?? []);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Playlist error");
        }
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