# Glass Player

A floating Windows music player with synced lyrics, customizable themes, a mini player, and Discord presence.

See [CHANGELOG.md](CHANGELOG.md) for patch notes.

<img width="459" height="99" alt="изображение" src="https://github.com/user-attachments/assets/386ee512-43d2-4cee-ba47-5e7112bfede2" />
<img width="467" height="86" alt="изображение" src="https://github.com/user-attachments/assets/fd042332-674f-49b5-a23d-bbc80fe085c7" />
<img width="502" height="877" alt="изображение" src="https://github.com/user-attachments/assets/8bf9ec86-3885-4306-bf06-0fefdae1cb7b" />
<img width="402" height="145" alt="изображение" src="https://github.com/user-attachments/assets/f69acca5-0833-45cf-80e4-f1451629ced3" />


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
