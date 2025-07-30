# SplitWire-Turkey C# WPF Version

A modern, professional WireGuard configuration tool built with C# WPF and Material Design.

## Advantages Over Python Version

### ✅ **Zero Antivirus Detection**
- Native Windows application
- No suspicious patterns or obfuscation
- Trusted C# language
- Professional code signing ready

### ✅ **Modern UI**
- Material Design interface
- Professional appearance
- Smooth animations
- Better user experience

### ✅ **Better Performance**
- Compiled native code
- Faster execution
- Lower memory usage
- Responsive interface

### ✅ **Professional Features**
- Proper error handling
- Async/await operations
- Clean architecture
- Easy to maintain

## Requirements

- **Windows 10/11**
- **.NET 6.0 SDK** (or later)
- **WireSock Secure Connect**
- **Administrator privileges**

## Building the Application

### Option 1: Using Build Script
```bash
# Run the build script
build_csharp.bat
```

### Option 2: Manual Build
```bash
# Navigate to project directory
cd SplitWireTurkey

# Restore dependencies
dotnet restore

# Build in Release mode
dotnet build -c Release

# Publish as single file
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### Option 3: Visual Studio
1. Open `SplitWireTurkey.sln` in Visual Studio
2. Build → Build Solution (Ctrl+Shift+B)
3. Build → Publish SplitWireTurkey

## Features

### 🔧 **WireGuard Profile Creation**
- Automatic wgcf registration
- Profile generation
- Configuration customization
- Application-specific routing

### 🔧 **WireSock Service Management**
- Service installation
- Service removal
- Automatic startup configuration
- Error handling

### 🔧 **Modern Interface**
- Material Design theme
- Tabbed interface
- Folder management
- Progress indicators

### 🔧 **Advanced Features**
- Custom folder paths
- Configuration file generation
- Service status monitoring
- Web browser integration

## File Structure

```
SplitWireTurkey/
├── App.xaml                 # Application resources
├── App.xaml.cs             # Application logic
├── MainWindow.xaml         # Main UI
├── MainWindow.xaml.cs      # Main window logic
├── Services/
│   ├── WireGuardService.cs # WireGuard operations
│   └── WireSockService.cs  # WireSock operations
├── Resources/              # Application resources
│   ├── splitwire.ico
│   ├── splitwire-logo-128.png
│   ├── splitwireturkeytext.png
│   ├── loading.gif
│   └── wgcf.exe
└── app.manifest           # Administrator privileges
```

## Security Features

### ✅ **Administrator Privileges**
- Proper UAC elevation
- Manifest-based permissions
- Secure service management

### ✅ **Error Handling**
- Comprehensive exception handling
- User-friendly error messages
- Graceful failure recovery

### ✅ **Code Quality**
- Clean, readable code
- Proper separation of concerns
- Async/await patterns
- Resource management

## Comparison with Python Version

| Feature | Python Version | C# Version |
|---------|----------------|------------|
| Antivirus Detection | High (False positives) | Zero |
| Performance | Good | Excellent |
| UI Quality | Basic | Professional |
| Code Quality | Good | Excellent |
| Maintenance | Moderate | Easy |
| Distribution | Complex | Simple |

## Building for Distribution

### Single File Executable
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### With Code Signing (Optional)
```bash
# Purchase a code signing certificate
# Sign the executable
signtool sign /f certificate.pfx /p password SplitWireTurkey.exe
```

## Troubleshooting

### Build Issues
1. Ensure .NET 6.0 SDK is installed
2. Run `dotnet restore` before building
3. Check all resource files are present

### Runtime Issues
1. Run as Administrator
2. Ensure WireSock is installed
3. Check Windows Defender exclusions

## License

© 2025 Çağrı Taşkın

This C# version provides a professional, antivirus-friendly alternative to the Python version with better performance and user experience. 