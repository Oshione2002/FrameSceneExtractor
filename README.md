# FrameSceneExtractor

FrameSceneExtractor is a Windows desktop application that detects major visual scene changes in a video and exports one representative still image for each detected scene together with the scene's start and end timestamps.

The application is intended for videos where you want **one image per distinct visual state/scene**, rather than one image every fixed number of seconds.

## What it does

- Opens or drag-drops common video formats including MP4, MKV, MOV, AVI, WebM, M4V, WMV, MPG/MPEG and TS.
- Uses FFmpeg's scene-change analysis locally to find major visual boundaries.
- Provides five sensitivity presets from **Very strict** to **Very sensitive**.
- Lets you set a minimum scene duration so flashes and very short transition fragments can be suppressed.
- Creates a representative midpoint preview for every detected scene.
- Shows each scene as a start/end range such as `00:00:05.438 → 00:00:09.762`.
- Lets you merge a scene with the previous or next scene, split a scene at its midpoint, or remove a scene from the export list.
- Exports a PNG for every retained scene.
- Includes the start and end timestamps in each exported image filename.
- Also exports `scenes.csv`, `scenes.json`, and `source-info.txt`.
- Supports cancelling analysis/export operations.
- Processes the source video entirely on the local PC; it does not upload the video.

## No end-user dependencies

The Windows installer is self-contained. The installed application includes:

- FrameSceneExtractor
- the .NET 8 Windows runtime required by the app
- FFmpeg
- FFprobe

The end user does **not** need to separately install Python, .NET, FFmpeg, OpenCV, Node.js, Visual Studio, Chocolatey, Winget, or another runtime/package manager.

After installation, core video analysis and extraction work offline.

## Output example

```text
My Video - Extracted/
├── frames/
│   ├── 001 — 00-00-00-000 — 00-00-05-438.png
│   ├── 002 — 00-00-05-438 — 00-00-09-762.png
│   └── 003 — 00-00-09-762 — 00-00-15-116.png
├── scenes.csv
├── scenes.json
└── source-info.txt
```

## Recommended default settings

- Scene sensitivity: **Balanced**
- Minimum scene duration: **0.50 seconds**

Use **Strict** or **Very strict** if normal movement inside a shot is producing too many scenes. Use a more sensitive preset if genuine cuts are being missed.

## Downloading the installer

Every push to `main` runs the **Build Windows Installer** GitHub Actions workflow.

1. Open the repository's **Actions** tab.
2. Open the latest successful **Build Windows Installer** run.
3. Download the `FrameSceneExtractor-Windows-Installer` artifact.
4. Extract the artifact ZIP.
5. Run `FrameSceneExtractor-Setup.exe`.

## Build architecture

```text
WPF / .NET 8 (Windows x64)
        │
        ├── UI and scene editor
        │
        ├── FFprobe → video duration
        │
        ├── FFmpeg scene filter → candidate boundaries
        │
        ├── minimum-duration filtering
        │
        └── FFmpeg → representative still extraction

GitHub Actions (Windows runner)
        │
        ├── dotnet publish --self-contained
        ├── bundle FFmpeg + FFprobe
        └── Inno Setup → FrameSceneExtractor-Setup.exe
```

## Building locally

Requirements are only needed for developers building the installer, not for people installing the finished app.

```powershell
dotnet restore FrameSceneExtractor.sln
dotnet publish src/FrameSceneExtractor/FrameSceneExtractor.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts/publish
```

The repository's GitHub Actions workflow performs the complete production build, including injecting the FFmpeg binaries and compiling the final Inno Setup installer.

## Current target

- Windows 10/11
- x64 processors
- Dedicated GPU not required
- Internet not required after installation

## Third-party software

FFmpeg and FFprobe are separate open-source projects. See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for the relevant notice and links to FFmpeg's licensing information.
