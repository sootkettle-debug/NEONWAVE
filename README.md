# NEONWAVE

A neon-soaked desktop music player for Windows, built with C# / WPF and NAudio.

## Features
- Local audio playback (MP3, WAV, AIFF, WMA, AAC, M4A and FLAC, subject to installed NAudio codecs)
- Play, pause, stop, previous / next and seek
- Drag-and-drop music and multi-file import
- Save and load JSON playlists (`.nwv.json`)
- Master volume and 10-band peaking equalizer
- Animated synthwave spectrum-style display
- Keyboard shortcuts: Space (play/pause), Ctrl+O (add music), Left/Right (seek 5 seconds)
- Dark sci-fi interface with cyan and magenta neon accents

## Download for Windows
Every push to `main` triggers the **Build NEONWAVE for Windows** workflow. Open the repository's **Actions** tab, select the latest successful run, and download the `NEONWAVE-Windows-x64` artifact. Extract the ZIP and run `NEONWAVE.exe`.

The build is self-contained for 64-bit Windows and does not require a separately installed .NET runtime.

## Build locally
Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet restore NEONWAVE/NEONWAVE.csproj
dotnet publish NEONWAVE/NEONWAVE.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Notes
This is an early release. The visualizer is an animated spectrum-style display rather than a calibrated FFT analyser. Album-art display and embedded metadata editing are not included yet. Audio format support depends on the codecs available through NAudio and Windows.
