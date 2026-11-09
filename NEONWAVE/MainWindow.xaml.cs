using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly Random random = new();
    private AudioFileReader? reader;
    private WaveOutEvent? output;
    private EqualizerSampleProvider? equalizer;
    private bool changingSeek;
    private bool draggingSeek;
    private int currentIndex = -1;
    private readonly List<Slider> eqSliders = new();

    public MainWindow()
    {
        InitializeComponent();
        Playlist.ItemsSource = tracks;
        BuildSpectrum();
        BuildEqualizer();
        timer.Tick += (_, _) => UpdateDisplay();
        timer.Start();
        Closed += (_, _) => DisposeAudio();
    }

    private void BuildSpectrum()
    {
        for (int i = 0; i < 48; i++)
        {
            var bar = new Border { Background = new SolidColorBrush(i % 3 == 0 ? Color.FromRgb(255,59,200) : Color.FromRgb(54,245,255)), CornerRadius = new CornerRadius(2), Height = 5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2,0,2,0), Opacity = .9 };
            Spectrum.Children.Add(bar);
        }
    }

    private void BuildEqualizer()
    {
        string[] labels = ["60 Hz","170 Hz","310 Hz","600 Hz","1 kHz","3 kHz","6 kHz","12 kHz","14 kHz","16 kHz"];
        foreach (var label in labels)
        {
            var panel = new StackPanel { Margin = new Thickness(3,5,3,5) };
            panel.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(133,134,168)), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
            var slider = new Slider { Minimum = -12, Maximum = 12, Value = 0, Orientation = Orientation.Vertical, Height = 78, Width = 24, Margin = new Thickness(0,4,0,0), HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Gain in dB" };
            eqSliders.Add(slider);
            panel.Children.Add(slider);
            EqualizerPanel.Children.Add(panel);
        }
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists).Where(p => new[] { ".mp3", ".wav", ".aiff", ".aif", ".wma", ".aac", ".m4a", ".flac" }.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant())))
            if (!tracks.Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase))) tracks.Add(new Track { Path = path });
        QueueCount.Text = $"{tracks.Count} TRACKS  •  NEONWAVE QUEUE";
        if (currentIndex < 0 && tracks.Count > 0) Playlist.SelectedIndex = 0;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Add audio to NEONWAVE", Filter = "Audio files|*.mp3;*.wav;*.aiff;*.aif;*.wma;*.aac;*.m4a;*.flac|All files|*.*", Multiselect = true };
        if (dialog.ShowDialog() == true) AddFiles(dialog.FileNames);
    }
    private void Window_DragOver(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    private void Window_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files); }
    private void Playlist_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (Playlist.SelectedIndex >= 0) currentIndex = Playlist.SelectedIndex; }
    private void Playlist_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (Playlist.SelectedIndex >= 0) PlayIndex(Playlist.SelectedIndex); }
    private void Play_Click(object sender, RoutedEventArgs e) { if (output?.PlaybackState == PlaybackState.Playing) { output.Pause(); PlayButton.Content = "▶"; } else if (output?.PlaybackState == PlaybackState.Paused) { output.Play(); PlayButton.Content = "Ⅱ"; } else if (tracks.Count > 0) PlayIndex(currentIndex < 0 ? 0 : currentIndex); }
    private void Previous_Click(object sender, RoutedEventArgs e) { if (reader != null && reader.CurrentTime.TotalSeconds > 3) reader.CurrentTime = TimeSpan.Zero; else if (tracks.Count > 0) PlayIndex((Math.Max(0,currentIndex - 1) + tracks.Count) % tracks.Count); }
    private void Next_Click(object sender, RoutedEventArgs e) => NextTrack();
    private void NextTrack() { if (tracks.Count > 0) PlayIndex((currentIndex + 1) % tracks.Count); }
    private void Stop_Click(object sender, RoutedEventArgs e) { output?.Stop(); if (reader != null) reader.Position = 0; PlayButton.Content = "▶"; UpdateDisplay(); }

    private void PlayIndex(int index)
    {
        if (index < 0 || index >= tracks.Count) return;
        try
        {
            DisposeAudio();
            currentIndex = index;
            Playlist.SelectedIndex = index;
            reader = new AudioFileReader(tracks[index].Path) { Volume = VolumeSlider.Value / 100.0f };
            equalizer = new EqualizerSampleProvider(reader);
            ApplyEq();
            output = new WaveOutEvent();
            output.Init(equalizer);
            output.PlaybackStopped += (_, args) => Dispatcher.Invoke(() =>
            {
                if (args.Exception == null && reader != null && reader.Position >= reader.Length - 1) NextTrack();
            });
            output.Play();
            PlayButton.Content = "Ⅱ";
            TrackTitle.Text = tracks[index].DisplayName;
            TrackInfo.Text = System.IO.Path.GetFileName(tracks[index].Path);
            changingSeek = true; SeekSlider.Value = 0; changingSeek = false;
        }
        catch (Exception ex) { MessageBox.Show($"Could not play this file.\n\n{ex.Message}", "NEONWAVE audio error", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void DisposeAudio()
    {
        if (output != null) { output.PlaybackStopped -= Output_PlaybackStopped; output.Stop(); output.Dispose(); output = null; }
        reader?.Dispose(); reader = null; equalizer = null;
    }
    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e) { }

    private void UpdateDisplay()
    {
        if (reader != null)
        {
            ElapsedText.Text = FormatTime(reader.CurrentTime);
            DurationText.Text = FormatTime(reader.TotalTime);
            if (!draggingSeek && reader.TotalTime.TotalSeconds > 0)
            {
                changingSeek = true;
                SeekSlider.Value = reader.CurrentTime.TotalSeconds / reader.TotalTime.TotalSeconds * 100;
                changingSeek = false;
            }
        }
        foreach (Border bar in Spectrum.Children)
        {
            double h = output?.PlaybackState == PlaybackState.Playing ? random.Next(8, 84) : 4;
            bar.Height = h;
            bar.Opacity = .55 + h / 180;
        }
    }
    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!changingSeek && reader != null && draggingSeek) reader.CurrentTime = TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds * SeekSlider.Value / 100);
    }
    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText != null) VolumeText.Text = $"{(int)VolumeSlider.Value}%";
        if (reader != null) reader.Volume = (float)(VolumeSlider.Value / 100);
    }
    private void ApplyEq()
    {
        if (equalizer == null) return;
        equalizer.SetGains(eqSliders.Select(s => (float)s.Value).ToArray());
    }
    private void ResetEq_Click(object sender, RoutedEventArgs e) { foreach (var s in eqSliders) s.Value = 0; ApplyEq(); }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        int i = Playlist.SelectedIndex; if (i < 0) return;
        bool playing = i == currentIndex && output != null;
        if (playing) { DisposeAudio(); PlayButton.Content = "▶"; TrackTitle.Text = "No track loaded"; }
        tracks.RemoveAt(i);
        if (tracks.Count == 0) currentIndex = -1;
        else { currentIndex = Math.Min(i, tracks.Count - 1); Playlist.SelectedIndex = currentIndex; }
        QueueCount.Text = $"{tracks.Count} TRACKS  •  NEONWAVE QUEUE";
    }
    private void Clear_Click(object sender, RoutedEventArgs e) { DisposeAudio(); tracks.Clear(); currentIndex = -1; Playlist.SelectedIndex = -1; TrackTitle.Text = "No track loaded"; TrackInfo.Text = "Drop MP3 files here or add music to begin"; QueueCount.Text = "0 TRACKS  •  DROP FILES TO ADD"; PlayButton.Content = "▶"; }
    private void SavePlaylist_Click(object sender, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { Filter = "NEONWAVE playlist|*.nwv.json", FileName = "My Playlist.nwv.json" };
        if (d.ShowDialog() == true) File.WriteAllText(d.FileName, JsonSerializer.Serialize(tracks.Select(t => t.Path), new JsonSerializerOptions { WriteIndented = true }));
    }
    private void LoadPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "NEONWAVE playlist|*.nwv.json;*.json" };
        if (d.ShowDialog() == true) { try { AddFiles(JsonSerializer.Deserialize<string[]>(File.ReadAllText(d.FileName)) ?? []); } catch (Exception ex) { MessageBox.Show(ex.Message, "Playlist error"); } }
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !(Keyboard.FocusedElement is Slider)) { Play_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) Add_Click(this, new RoutedEventArgs());
        else if (e.Key == Key.Left && reader != null) reader.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, reader.CurrentTime.TotalSeconds - 5));
        else if (e.Key == Key.Right && reader != null) reader.CurrentTime = TimeSpan.FromSeconds(Math.Min(reader.TotalTime.TotalSeconds, reader.CurrentTime.TotalSeconds + 5));
    }
}

// Lightweight 10-band peaking equalizer implemented as an NAudio sample provider.
public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly BiQuadFilter[] filters;
    private readonly float[] gains = new float[10];
    private readonly double[] frequencies = [60,170,310,600,1000,3000,6000,12000,14000,16000];
    public EqualizerSampleProvider(ISampleProvider source)
    {
        this.source = source;
        filters = frequencies.Select(f => BiQuadFilter.PeakingEQ(source.WaveFormat.SampleRate, (float)f, 1.0f, 0)).ToArray();
    }
    public WaveFormat WaveFormat => source.WaveFormat;
    public void SetGains(float[] db)
    {
        for (int i=0; i<filters.Length; i++)
        {
            gains[i] = i < db.Length ? db[i] : 0;
            filters[i] = BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, (float)frequencies[i], 1.0f, gains[i]);
        }
    }
    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        for (int i=0; i<read; i++) { float sample = buffer[offset+i]; foreach (var filter in filters) sample = filter.Transform(sample); buffer[offset+i] = Math.Clamp(sample, -1f, 1f); }
        return read;
    }
}