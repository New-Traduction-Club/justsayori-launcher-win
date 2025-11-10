// SubmodModels.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace justsayo_win;

public enum SubmodType { Submod, Background, Outfit }

public enum SubmodStatus { NotInstalled, Installed, UpdateAvailable }

public class SubmodCatalog
{
    [JsonPropertyName("submods")]
    public List<SubmodItem> Submods { get; set; } = new();

    [JsonPropertyName("backgrounds")]
    public List<SubmodItem> Backgrounds { get; set; } = new();

    [JsonPropertyName("outfits")]
    public List<SubmodItem> Outfits { get; set; } = new();
}

public class SubmodItem : INotifyPropertyChanged
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SubmodType Type { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("preview_image_url")]
    public string PreviewImageUrl { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("package_contents")]
    public string PackageContents { get; set; } = "";

    private SubmodStatus _status;
    [JsonIgnore]
    public SubmodStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    private bool _isInstalling;
    [JsonIgnore]
    public bool IsInstalling
    {
        get => _isInstalling;
        set => SetField(ref _isInstalling, value);
    }

    private double _installProgress;
    [JsonIgnore]
    public double InstallProgress
    {
        get => _installProgress;
        set => SetField(ref _installProgress, value);
    }


    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    #endregion
}

public class InstalledSubmodInfo
{
    public string Version { get; set; } = "";
    public List<string> InstalledFiles { get; set; } = new();
}
