# Cordex

A lightweight, feature-rich wrapper for the Discord web client built with WPF and CefSharp.

## Features

- **System Tray Integration** - Minimize to tray and control Discord from the system tray
- **Global Hotkeys** - Control Discord from anywhere with customizable keyboard shortcuts
- **Voice Activity Indicator** - Real-time visual feedback when you're talking in voice channels
- **Mute Status Icons** - Tray icon changes based on your mute/unmute/talking status
- **Audio Monitoring** - Automatic detection of voice activity with visual indicators
- **Custom Window Chrome** - Modern, borderless window design with custom title bar
- **Single Instance** - Prevents multiple instances from running simultaneously
- **Persistent Sessions** - Your Discord session is saved and restored between launches

## System Requirements

- **Operating System**: Windows 10/11 (64-bit)
- **Architecture**: x64
- **Dependencies**: None - fully self-contained (no .NET runtime installation required)

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the ZIP file to your desired location
3. Run `Cordex.exe`

**Note**: Keep all files in the same folder. The application requires the CefSharp libraries and other dependencies to be in the same directory as the executable.

## Usage

### First Launch
- On first launch, Cordex will open Discord's web interface
- Log in to your Discord account
- Your session will be saved automatically

### Window Controls
- **Minimize** - Minimizes to system tray
- **Maximize/Restore** - Toggle fullscreen mode
- **Close (X)** - Minimizes to tray (doesn't exit the app)
- **Settings (⚙️)** - Opens settings window

### System Tray
Right-click the tray icon to access:
- **Open Cordex** - Restore the window
- **Toggle Mute** - Quickly mute/unmute your microphone
- **Exit** - Close the application completely

### Tray Icon States
- **Default** (app icon) - Not in a voice channel
- **Muted** (muted icon) - In voice channel, microphone muted
- **Unmuted** (unmuted icon) - In voice channel, microphone active but not talking
- **Talking** (talking icon) - In voice channel, actively speaking

### Default Hotkeys
- **Ctrl+Shift+M** - Toggle Mute
- **Ctrl+Shift+D** - Toggle Deafen
- **Ctrl+Shift+N** - Focus Cordex Window

### Customizing Hotkeys
1. Click the settings icon (⚙️) in the title bar
2. Navigate to the Keybinds section
3. Click on any keybind field
4. Press your desired key combination
5. Click "Save" to apply changes

## Building from Source

### Prerequisites
- .NET 10 SDK
- Windows 10/11
- Visual Studio 2022 or JetBrains Rider (optional)

### Build Commands

**Debug Build:**
```powershell
dotnet build Cordex.csproj -c Debug
```

**Release Build:**
```powershell
dotnet build Cordex.csproj -c Release
```

**Publish (Self-Contained):**
```powershell
dotnet publish Cordex.csproj -c Release -r win-x64 --self-contained true
```

The output will be in `bin\Release\net10.0-windows\win-x64\publish\`

## Project Structure

```
Cordex/
├── Assets/              # Application icons
│   ├── app.ico         # Main application icon
│   ├── muted.ico       # Muted state icon
│   ├── unmuted.ico     # Unmuted state icon
│   └── talking.ico     # Talking state icon
├── Core/               # Core functionality
│   ├── AudioMonitor.cs      # Voice activity detection
│   ├── KeybindManager.cs    # Global hotkey management
│   ├── SessionManager.cs    # Session and cache management
│   ├── SettingsManager.cs   # Settings persistence
│   └── TrayManager.cs       # System tray integration
├── App.xaml            # Application resources
├── App.xaml.cs         # Application entry point
├── MainWindow.xaml     # Main window UI
├── MainWindow.xaml.cs  # Main window logic
├── SettingsWindow.xaml # Settings UI
├── SettingsWindow.xaml.cs # Settings logic
└── Cordex.csproj       # Project configuration
```

## Configuration

### Settings Location
Settings are stored in: `%AppData%\Cordex\settings.json`

### Cache Location
Discord cache is stored in: `%AppData%\Cordex\Cache`

### Manual Configuration
You can manually edit `settings.json` to customize hotkeys:

```json
{
  "Mute": {
    "Modifiers": 6,
    "VirtualKey": 77,
    "Display": "Ctrl+Shift+M"
  },
  "Deafen": {
    "Modifiers": 6,
    "VirtualKey": 68,
    "Display": "Ctrl+Shift+D"
  },
  "Focus": {
    "Modifiers": 6,
    "VirtualKey": 78,
    "Display": "Ctrl+Shift+N"
  }
}
```

**Modifier Values:**
- `1` = Alt
- `2` = Ctrl
- `4` = Shift
- Combine by adding (e.g., `6` = Ctrl+Shift)

## Technologies Used

- **.NET 10** - Application framework
- **WPF** - User interface
- **CefSharp** - Chromium Embedded Framework for rendering Discord web client
- **WPF-UI** - Modern UI controls and theming
- **NAudio** (via AudioMonitor) - Audio device monitoring

## Known Issues

- **First Launch**: May take a few seconds to initialize CefSharp
- **Voice Detection**: Requires microphone permissions
- **Single File**: Cannot be built as a true single-file executable due to CefSharp native dependencies

## Troubleshooting

### Application won't start
- Ensure all files from the publish folder are in the same directory
- Check that you're running the 64-bit version on a 64-bit Windows system
- Try running as administrator

### Hotkeys not working
- Check if another application is using the same hotkey combination
- Try changing the hotkey in settings
- Restart Cordex after changing hotkeys

### Voice activity not detected
- Ensure microphone permissions are granted
- Check Windows privacy settings for microphone access
- Verify your microphone is set as the default recording device

### Discord not loading
- Check your internet connection
- Clear cache by deleting `%AppData%\Cordex\Cache`
- Restart the application

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## License

This project is provided as-is for personal use. Discord is a trademark of Discord Inc.

## Disclaimer

Cordex is an unofficial third-party application and is not affiliated with, endorsed by, or connected to Discord Inc. Use at your own risk. This application simply wraps the official Discord web client and does not modify or intercept Discord's functionality.

## Credits

- **CefSharp** - Chromium Embedded Framework for .NET
- **WPF-UI** - Modern WPF UI library
- **Discord** - Communication platform

---

**Version**: 1.0.0  
**Author**: [Your Name]  
**Repository**: [GitHub URL] 
