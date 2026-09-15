using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameSceneExtractor.Models;
using FrameSceneExtractor.Services;
using Microsoft.Win32;

namespace FrameSceneExtractor;

public partial class MainWindow : Window
{
    private readonly FfmpegService _ffmpeg = new();
    private readonly ExportService _exportService;
    private CancellationTokenSource? _workCts;
    private string? _selectedVideoPath;
    private string? _cacheDirectory;

    public ObservableCollection<SceneSegment> Scenes { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _exportService = new ExportService(_ffmpeg);
    }

    private void SelectVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a video",
            Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.wmv;*.mpg;*.mpeg;*.ts|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            SelectVideo(dialog.FileName);
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && File.Exists(files[0]))
        {
            SelectVideo(files[0]);
        }
    }

    private void SelectVideo(string path)
    {
        _selectedVideoPath = path;
        VideoPathText.Text = path;
        Scenes.Clear();
        SceneCountText.Text = "Ready to analyse";
        ExportButton.IsEnabled = false;
        StatusText.Text = $"Selected {Path.GetFileName(path)}";
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedVideoPath is null || !File.Exists(_selectedVideoPath))
        {
            MessageBox.Show(this, "Choose a video first.", "FrameSceneExtractor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryGetMinimumDuration(out var minimumSceneDuration))
        {
            return;
        }

        _workCts?.Cancel();
        _workCts?.Dispose();
        _workCts = new CancellationTokenSource();
        var token = _workCts.Token;
        var threshold = GetSelectedThreshold();

        try
        {
            SetBusy(true, indeterminate: true);
            Scenes.Clear();
            SceneCountText.Text = "Analysing…";
            _ffmpeg.EnsureAvailable();

            var duration = await _ffmpeg.GetDurationAsync(_selectedVideoPath, token);
            var status = new Progress<string>(message => StatusText.Text = message);
            var boundaries = await _ffmpeg.DetectSceneBoundariesAsync(
                _selectedVideoPath,
                duration,
                threshold,
                minimumSceneDuration,
                token,
                status);

            for (var i = 0; i < boundaries.Count - 1; i++)
            {
                Scenes.Add(new SceneSegment
                {
                    Index = i + 1,
                    StartSeconds = boundaries[i],
                    EndSeconds = boundaries[i + 1]
                });
            }

            SceneCountText.Text = $"{Scenes.Count} scene{(Scenes.Count == 1 ? string.Empty : "s")} detected";
            PrepareCacheDirectory();

            WorkProgress.IsIndeterminate = false;
            WorkProgress.Minimum = 0;
            WorkProgress.Maximum = Math.Max(1, Scenes.Count);
            WorkProgress.Value = 0;

            for (var i = 0; i < Scenes.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                StatusText.Text = $"Creating preview {i + 1} of {Scenes.Count}…";
                await RefreshThumbnailAsync(Scenes[i], token);
                WorkProgress.Value = i + 1;
            }

            StatusText.Text = $"Analysis complete — {Scenes.Count} scene{(Scenes.Count == 1 ? string.Empty : "s")}.";
            ExportButton.IsEnabled = Scenes.Count > 0;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Operation cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Analysis failed.";
            MessageBox.Show(this, ex.Message, "FrameSceneExtractor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _workCts?.Cancel();
        StatusText.Text = "Cancelling…";
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedVideoPath is null || Scenes.Count == 0)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose where the extracted scene folder should be created",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        _workCts?.Cancel();
        _workCts?.Dispose();
        _workCts = new CancellationTokenSource();
        var token = _workCts.Token;

        try
        {
            SetBusy(true, indeterminate: true);
            var status = new Progress<string>(message => StatusText.Text = message);
            await _exportService.ExportAsync(_selectedVideoPath, Scenes.ToList(), dialog.FolderName, token, status);
            MessageBox.Show(this, "Frames and timestamp metadata were exported successfully.", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Export cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void MergePrevious_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SceneSegment current)
        {
            return;
        }

        var index = Scenes.IndexOf(current);
        if (index <= 0)
        {
            StatusText.Text = "The first scene has no previous scene to merge with.";
            return;
        }

        var previous = Scenes[index - 1];
        previous.EndSeconds = current.EndSeconds;
        Scenes.RemoveAt(index);
        ReindexScenes();
        await RefreshThumbnailSafeAsync(previous);
        StatusText.Text = "Scenes merged.";
    }

    private async void MergeNext_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SceneSegment current)
        {
            return;
        }

        var index = Scenes.IndexOf(current);
        if (index < 0 || index >= Scenes.Count - 1)
        {
            StatusText.Text = "The last scene has no following scene to merge with.";
            return;
        }

        var next = Scenes[index + 1];
        current.EndSeconds = next.EndSeconds;
        Scenes.RemoveAt(index + 1);
        ReindexScenes();
        await RefreshThumbnailSafeAsync(current);
        StatusText.Text = "Scenes merged.";
    }

    private async void Split_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SceneSegment current || current.DurationSeconds < 0.20)
        {
            return;
        }

        var index = Scenes.IndexOf(current);
        if (index < 0)
        {
            return;
        }

        var oldEnd = current.EndSeconds;
        var midpoint = current.MidpointSeconds;
        current.EndSeconds = midpoint;
        var secondHalf = new SceneSegment
        {
            StartSeconds = midpoint,
            EndSeconds = oldEnd
        };
        Scenes.Insert(index + 1, secondHalf);
        ReindexScenes();
        await RefreshThumbnailSafeAsync(current);
        await RefreshThumbnailSafeAsync(secondHalf);
        StatusText.Text = "Scene split at its midpoint.";
    }

    private void RemoveScene_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SceneSegment current)
        {
            return;
        }

        Scenes.Remove(current);
        ReindexScenes();
        StatusText.Text = "Scene removed from the export list.";
    }

    private async Task RefreshThumbnailSafeAsync(SceneSegment scene)
    {
        if (_selectedVideoPath is null)
        {
            return;
        }

        try
        {
            if (_workCts is null || _workCts.IsCancellationRequested)
            {
                _workCts?.Dispose();
                _workCts = new CancellationTokenSource();
            }

            await RefreshThumbnailAsync(scene, _workCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText.Text = $"Scene changed, but preview refresh failed: {ex.Message}";
        }
    }

    private async Task RefreshThumbnailAsync(SceneSegment scene, CancellationToken cancellationToken)
    {
        if (_selectedVideoPath is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_cacheDirectory))
        {
            PrepareCacheDirectory();
        }

        var output = Path.Combine(_cacheDirectory!, $"scene-{Guid.NewGuid():N}.jpg");
        await _ffmpeg.ExtractFrameAsync(_selectedVideoPath, scene.MidpointSeconds, output, cancellationToken);
        scene.ThumbnailPath = output;
    }

    private void PrepareCacheDirectory()
    {
        TryDeleteCache();
        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameSceneExtractor", "Cache");
        _cacheDirectory = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDirectory);
    }

    private void TryDeleteCache()
    {
        if (string.IsNullOrWhiteSpace(_cacheDirectory) || !Directory.Exists(_cacheDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_cacheDirectory, true);
        }
        catch
        {
            // Cached preview images are disposable. A locked cache can be cleaned on a later run.
        }
    }

    private bool TryGetMinimumDuration(out double value)
    {
        var text = MinDurationText.Text.Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            MessageBox.Show(this, "Enter a valid minimum scene duration, for example 0.50.", "Invalid duration", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (value < 0 || value > 60)
        {
            MessageBox.Show(this, "Minimum scene duration must be between 0 and 60 seconds.", "Invalid duration", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private double GetSelectedThreshold() => SensitivityCombo.SelectedIndex switch
    {
        0 => 0.60,
        1 => 0.48,
        2 => 0.36,
        3 => 0.26,
        4 => 0.18,
        _ => 0.36
    };

    private void ReindexScenes()
    {
        for (var i = 0; i < Scenes.Count; i++)
        {
            Scenes[i].Index = i + 1;
        }

        SceneCountText.Text = $"{Scenes.Count} scene{(Scenes.Count == 1 ? string.Empty : "s")}";
        ExportButton.IsEnabled = Scenes.Count > 0 && !CancelButton.IsEnabled;
    }

    private void SetBusy(bool busy, bool indeterminate = false)
    {
        AnalyzeButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        ExportButton.IsEnabled = !busy && Scenes.Count > 0;
        WorkProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        WorkProgress.IsIndeterminate = busy && indeterminate;
        if (!busy)
        {
            WorkProgress.Value = 0;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _workCts?.Cancel();
        TryDeleteCache();
        _workCts?.Dispose();
        base.OnClosed(e);
    }
}
