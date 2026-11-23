# Changelog

All notable changes to AlenPlayer are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0] — 2026-11-24

First public release.

### Playback
- Video, audio and photos in one window, powered by libmpv / FFmpeg
- Hardware decoding (D3D11), low CPU and memory usage
- Resume long videos where you left off
- Audio and subtitle track selection, external subtitles, playback speed, repeat, frame stepping
- Music visualizer for audio files

### Interface
- Dark theme with a custom title bar and Windows 11 rounded corners
- Windows Media Player–style control bar: visible while paused, fades out during playback
- Navigation arrows and click zones at the left/right edges to flip through a folder
- Playlist panel, fullscreen mode, always on top, drag and drop
- Window title shows the name of the opened file
- 11 UI languages: English, Русский, Українська, Қазақша, O'zbekcha, Español, Deutsch,
  Français, Português, Türkçe, 中文 — chosen automatically from the Windows language

### Photos
- Zoom to the cursor with the mouse wheel (never smaller than the window), drag within the frame
- Rotate, slideshow, transparent images shown on the app background

### Windows integration
- Explorer thumbnails for videos, album art and rare image formats
- "Set as default player" and "Remove from system" in the menu, no admin rights required
- Portable: `AlenPlayer.exe` + `libmpv-2.dll`, no installer

[1.0.0]: https://github.com/a1enq/AlenPlayer/releases/tag/v1.0.0
