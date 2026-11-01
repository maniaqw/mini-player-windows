# Glass Player

A floating Windows music player with synced lyrics, customizable themes, a mini player, and Discord presence.

## Requirements

- Windows 10 version 2004 or later, or Windows 11
- .NET 8 SDK with Windows desktop support

## Run from source

```powershell
dotnet run --project MiniPlayer.csproj
```

## Checks

The check project includes desktop UI checks that require an interactive Windows session.

```powershell
dotnet run --project tests/GlassPlayer.Checks.csproj
```

## Source layout

- `Assets/`: application icons and artwork
- `Localization/`: translations
- `Shaders/`: shader source and the shader bytecode embedded by WPF
- `tests/`: application checks

This repository contains the application source only; it does not include installer scripts or prebuilt releases. `Shaders/Glass.ps` is required by the application and is kept alongside its HLSL source.
