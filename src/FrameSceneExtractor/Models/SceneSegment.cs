using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrameSceneExtractor.Models;

public sealed class SceneSegment : INotifyPropertyChanged
{
    private int _index;
    private double _startSeconds;
    private double _endSeconds;
    private string? _thumbnailPath;

    public int Index
    {
        get => _index;
        set => SetField(ref _index, value);
    }

    public double StartSeconds
    {
        get => _startSeconds;
        set
        {
            if (SetField(ref _startSeconds, value))
            {
                NotifyDerived();
            }
        }
    }

    public double EndSeconds
    {
        get => _endSeconds;
        set
        {
            if (SetField(ref _endSeconds, value))
            {
                NotifyDerived();
            }
        }
    }

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set => SetField(ref _thumbnailPath, value);
    }

    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
    public double MidpointSeconds => StartSeconds + (DurationSeconds / 2d);
    public string StartTimecode => FormatTimecode(StartSeconds);
    public string EndTimecode => FormatTimecode(EndSeconds);
    public string DurationText => $"{DurationSeconds:0.000} s";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(MidpointSeconds));
        OnPropertyChanged(nameof(StartTimecode));
        OnPropertyChanged(nameof(EndTimecode));
        OnPropertyChanged(nameof(DurationText));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public static string FormatTimecode(double seconds)
    {
        seconds = Math.Max(0, seconds);
        var totalMilliseconds = (long)Math.Round(seconds * 1000d);
        var milliseconds = totalMilliseconds % 1000;
        var totalSeconds = totalMilliseconds / 1000;
        var secs = totalSeconds % 60;
        var totalMinutes = totalSeconds / 60;
        var mins = totalMinutes % 60;
        var hours = totalMinutes / 60;
        return $"{hours:00}:{mins:00}:{secs:00}.{milliseconds:000}";
    }

    public static string FormatFilenameTimecode(double seconds) => FormatTimecode(seconds)
        .Replace(':', '-')
        .Replace('.', '-');
}
