using System.Globalization;
using System.Text;
using System.Text.Json;
using FrameSceneExtractor.Models;

namespace FrameSceneExtractor.Services;

public sealed class ExportService
{
    private readonly FfmpegService _ffmpeg;

    public ExportService(FfmpegService ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    public async Task ExportAsync(
        string videoPath,
        IReadOnlyList<SceneSegment> scenes,
        string destinationFolder,
        CancellationToken cancellationToken,
        IProgress<string>? status = null)
    {
        if (scenes.Count == 0)
        {
            throw new InvalidOperationException("There are no scenes to export.");
        }

        var root = Path.Combine(destinationFolder, $"{SanitizeFileName(Path.GetFileNameWithoutExtension(videoPath))} - Extracted");
        var framesFolder = Path.Combine(root, "frames");
        Directory.CreateDirectory(framesFolder);

        for (var i = 0; i < scenes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scene = scenes[i];
            var fileName = $"{i + 1:000} — {SceneSegment.FormatFilenameTimecode(scene.StartSeconds)} — {SceneSegment.FormatFilenameTimecode(scene.EndSeconds)}.png";
            var outputPath = Path.Combine(framesFolder, fileName);
            status?.Report($"Exporting frame {i + 1} of {scenes.Count}…");
            await _ffmpeg.ExtractFrameAsync(videoPath, scene.MidpointSeconds, outputPath, cancellationToken);
        }

        await WriteCsvAsync(Path.Combine(root, "scenes.csv"), scenes, cancellationToken);
        await WriteJsonAsync(Path.Combine(root, "scenes.json"), videoPath, scenes, cancellationToken);
        await WriteInfoAsync(Path.Combine(root, "source-info.txt"), videoPath, scenes, cancellationToken);
        status?.Report($"Export complete: {root}");
    }

    private static async Task WriteCsvAsync(string path, IReadOnlyList<SceneSegment> scenes, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scene,start,end,duration_seconds,image");
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var imageName = $"{i + 1:000} — {SceneSegment.FormatFilenameTimecode(scene.StartSeconds)} — {SceneSegment.FormatFilenameTimecode(scene.EndSeconds)}.png";
            builder.Append(i + 1).Append(',')
                .Append(EscapeCsv(scene.StartTimecode)).Append(',')
                .Append(EscapeCsv(scene.EndTimecode)).Append(',')
                .Append(scene.DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(Path.Combine("frames", imageName)))
                .AppendLine();
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(true), cancellationToken);
    }

    private static async Task WriteJsonAsync(string path, string videoPath, IReadOnlyList<SceneSegment> scenes, CancellationToken cancellationToken)
    {
        var payload = new
        {
            source = Path.GetFileName(videoPath),
            generatedUtc = DateTimeOffset.UtcNow,
            sceneCount = scenes.Count,
            scenes = scenes.Select((scene, index) => new
            {
                scene = index + 1,
                startSeconds = Math.Round(scene.StartSeconds, 3),
                endSeconds = Math.Round(scene.EndSeconds, 3),
                durationSeconds = Math.Round(scene.DurationSeconds, 3),
                start = scene.StartTimecode,
                end = scene.EndTimecode,
                image = Path.Combine("frames", $"{index + 1:000} — {SceneSegment.FormatFilenameTimecode(scene.StartSeconds)} — {SceneSegment.FormatFilenameTimecode(scene.EndSeconds)}.png")
            })
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }

    private static async Task WriteInfoAsync(string path, string videoPath, IReadOnlyList<SceneSegment> scenes, CancellationToken cancellationToken)
    {
        var text = $"FrameSceneExtractor\nSource: {videoPath}\nScenes: {scenes.Count}\nGenerated: {DateTimeOffset.Now:O}\n";
        await File.WriteAllTextAsync(path, text, cancellationToken);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
    }
}
