# Third-party notices

## FFmpeg / FFprobe

FrameSceneExtractor bundles FFmpeg and FFprobe in the Windows installer so that video processing works offline without a separate FFmpeg installation.

The Windows build workflow downloads an FFmpeg release build from Gyan.dev and copies the accompanying license file into the installed `tools` directory when that file is present in the downloaded archive.

FFmpeg is a separate open-source project. Its licensing depends on the configuration used to build the binaries. See the FFmpeg legal and licensing documentation at https://ffmpeg.org/legal.html and https://ffmpeg.org/about.html.

FrameSceneExtractor does not claim ownership of FFmpeg or FFprobe.
