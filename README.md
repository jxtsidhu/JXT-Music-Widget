# JXT MUSIC WIDGET

A classy, fluid "now playing" widget for Windows 10 and 11. It shows the current track, album art, progress and playback controls in a floating card, and works with anything Windows lists as playing media: Spotify, YouTube in your browser, Apple Music, VLC and more.

Made by **JXT SIDHU**.

<!-- Add a screenshot or GIF here, for example: ![JXT MUSIC WIDGET](docs/preview.png) -->

## Features

- **Live media info** with album art, title, artist, progress bar and elapsed/total time
- **Playback controls**: previous, play/pause (with a smooth morphing icon) and next, plus click-to-seek on the progress bar
- **Album-art colours**: the card picks up an accent colour from the current cover
- **Smooth animation**: spring-based motion, cross-fading covers and scrolling long titles
- **Four layouts**: Classic, Compact, Vertical and Minimal
- **Settings window** with a live preview of the real widget
- **Themes**: Dark, Light, Black, or follow Windows
- **Customisable**: position (3x3 grid or drag anywhere), size, edge margin, monitor, corner radius, accent colour and tint, opacity, drop shadow, cover glow
- **Display options**: always show, show while playing, or peek on track change; progress bar, time labels and title scrolling can each be switched off
- **Start with Windows**: launches quietly into the system tray at sign-in
- **Tray app**: closing the settings window can minimise to the tray or quit (your choice)
- **DPI aware** and multi-monitor friendly
- **No installer, no dependencies**: builds with the compiler that ships with Windows

## Requirements

- Windows 10 (version 1809 or newer) or Windows 11
- .NET Framework 4.x (already included with Windows; enable it under *Windows Features* if the build says it is missing)

## Build

1. Download or clone this repository.
2. Make sure these files are in the same folder:
   - `JXTMusicWidget.cs`
   - `Build.bat`
   - `icon.ico`
3. Double-click `Build.bat`.

It compiles `JXT MUSIC WIDGET.exe` and starts it. If the build fails, the console keeps the error lines on screen so you can copy them into an issue.

## Usage

- **Double-click the exe** to open the settings window. The widget stays on screen while it is open so you can see changes live.
- **Left-click the tray icon** to reopen settings. **Right-click** the tray icon or the widget for quick options (layout, theme, position, size, visibility, start with Windows, exit).
- **Drag the widget** to place it anywhere. This switches Position to *Custom*.
- **Click the progress bar** to seek.
- Launching the exe again while it is running just opens the existing window.

## Where things are stored

- Settings: `%AppData%\JXT MUSIC WIDGET\settings.ini`
- Error log (only if something goes wrong): `%AppData%\JXT MUSIC WIDGET\error.log`
- Start with Windows uses the per-user `Run` registry key, so no admin rights are needed. Turning the option off removes it.

## Uninstall

1. Turn off **Start with Windows** in the settings.
2. Quit the widget from the sidebar or the tray menu.
3. Delete the exe and the `%AppData%\JXT MUSIC WIDGET` folder.

## Tech notes

- C# and WinForms, compiled with `csc.exe` from the .NET Framework (C# 5)
- Media data comes from the Windows `GlobalSystemMediaTransportControlsSessionManager` API
- The widget is a layered window drawn with GDI+ and updated with `UpdateLayeredWindow` for per-pixel transparency

## License

MIT License
Made By JXT SIDHU
