using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FrameSceneExtractor.Services;

public sealed class FfmpegService
{
    private static readonly Regex PtsRegex = new(@"pts_time:(?<time>[0-9eE+\-.]+)", RegexOptions.Compiled);

    public string ToolsDirectory { get; }
    public string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");
    public string FfprobePath => Path.Combine(ToolsDirectory, "ffprobe.exe");

    public FfmpegService()
    {
        ToolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
    }

    public void EnsureAvailable()
    {
        if (!File.Exists(FfmpegPath) || !File.Exists(FfprobePath))
        {
            throw new FileNotFoundException(
                "The bundled video engine is missing. Reinstall FrameSceneExtractor using the official Setup.exe. " +
                $"Expected FFmpeg files in: {ToolsDirectory}");
        }
    }

    public async Task<double> GetDurationAsync(string videoPath, CancellationToken cancellationToken)
    {
        EnsureAvailable();

        var startInfo = CreateStartInfo(FfprobePath);
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        startInfo.ArgumentList.Add(videoPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFprobe.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFprobe could not read the video. {stderr}".Trim());
        }

        if (!double.TryParse(stdout, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || duration <= 0)
        {
            throw new InvalidOperationException("The video duration could not be determined.");
        }

        return duration;
    }

    public async Task<IReadOnlyList<double>> DetectSceneBoundariesAsync(
        string videoPath,
        double durationSeconds,
        double sceneThreshold,
        double minimumSceneDuration,
        CancellationToken cancellationToken,
        IProgress<string>? status = null)
    {
        EnsureAvailable();

        sceneThreshold = Math.Clamp(sceneThreshold, 0.01, 0.99);
        minimumSceneDuration = Math.Max(0, minimumSceneDuration);

        var startInfo = CreateStartInfo(FfmpegPath);
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-nostats");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add($"select=gt(scene\\,{sceneThreshold.ToString("0.####", CultureInfo.InvariantCulture)}),showinfo");
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("null");
        startInfo.ArgumentList.Add("-");

        status?.Report("Scanning the video for major visual changes…");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFmpeg.");
        var candidates = new List<double>();
        var errorTail = new Queue<string>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (errorTail.Count >= 20)
            {
                errorTail.Dequeue();
            }
            errorTail.Enqueue(line);

            var match = PtsRegex.Match(line);
            if (match.Success && double.TryParse(match.Groups["time"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var timestamp))
            {
                if (timestamp > 0.001 && timestamp < durationSeconds - 0.001)
                {
                    candidates.Add(timestamp);
                    status?.Report($"Scene change candidate at {FormatShort(timestamp)}");
                }
            }
        }

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("FFmpeg scene analysis failed.\n" + string.Join(Environment.NewLine, errorTail));
        }

        var sortedCandidates = candidates
            .Where(x => x > 0 && x < durationSeconds)
            .OrderBy(x => x)
            .ToList();

        var accepted = new List<double> { 0d };
        foreach (var candidate in sortedCandidates)
        {
            if (candidate - accepted[^1] >= minimumSceneDuration)
            {
                accepted.Add(candidate);
            }
        }

        if (accepted.Count > 1 && durationSeconds - accepted[^1] < minimumSceneDuration)
        {
            accepted.RemoveAt(accepted.Count - 1);
        }

        accepted.Add(durationSeconds);
        return accepted;
    }

    public async Task ExtractFrameAsync(
        string videoPath,
        double timestampSeconds,
        string outputPath,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var startInfo = CreateStartInfo(FfmpegPath);
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(Math.Max(0, timestampSeconds).ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:v:0");
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFmpeg.");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException($"Could not extract a still frame at {timestampSeconds:0.000}s. {stderr}".Trim());
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName) => new()
    {
        FileName = fileName,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = System.Text.Encoding.UTF8,
        StandardErrorEncoding = System.Text.Encoding.UTF8
    };

    private static string FormatShort(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff");
}
