using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace justsayo_win;

public class SubmodsViewModel : INotifyPropertyChanged
{
    private const string SubmodsJsonUrl = "https://traduction-club.live/api/justsayori/justsayori_submods.json";
    private readonly string _installDir;
    private readonly string _submodStatusFilePath;

    private static readonly HttpClient _httpClient = new();
    private List<SubmodItem> _allSubmods = new();
    private Dictionary<string, string> _installedSubmodVersions = new();
    private object? _currentSubmodView;
    private SubmodType _selectedFilter = SubmodType.Submod;

    public bool IsMainGameInstalled { get; }
    public ObservableCollection<SubmodItem> FilteredSubmods { get; } = new();
    public object? CurrentSubmodView
    {
        get => _currentSubmodView;
        set => SetField(ref _currentSubmodView, value);
    }
    public SubmodType SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetField(ref _selectedFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public ICommand SelectSubmodCommand { get; }
    public ICommand GoBackCommand { get; }
    public ICommand InstallSubmodCommand { get; }
    public ICommand UninstallSubmodCommand { get; }
    public ICommand ResetStatusCommand { get; }

    public SubmodsViewModel(string gameInstallDir, bool isMainGameInstalled)
    {
        _installDir = gameInstallDir;
        IsMainGameInstalled = isMainGameInstalled;
        _submodStatusFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_data", "submods_status.json");

        SelectSubmodCommand = new RelayCommand<SubmodItem>(SelectSubmod);
        GoBackCommand = new RelayCommand(GoBack);
        InstallSubmodCommand = new AsyncRelayCommand<SubmodItem>(InstallSubmod);
        UninstallSubmodCommand = new AsyncRelayCommand<SubmodItem>(UninstallSubmod);
        ResetStatusCommand = new RelayCommand<SubmodItem>(ResetStatus);

        // Set the initial view to the list of cards
        CurrentSubmodView = this;
        _ = LoadSubmodsAsync();
    }

    private async Task LoadSubmodsAsync()
    {
        LoadInstalledSubmodStatus();
        try
        {
            var json = await _httpClient.GetStringAsync(SubmodsJsonUrl);
            var catalog = JsonSerializer.Deserialize<SubmodCatalog>(json);

            if (catalog == null) return;

            _allSubmods.Clear();
            _allSubmods.AddRange(catalog.Submods);
            _allSubmods.AddRange(catalog.Backgrounds);
            _allSubmods.AddRange(catalog.Outfits);

            foreach (var submod in _allSubmods)
            {
                if (_installedSubmodVersions.TryGetValue(submod.Id, out var installedVersion))
                {
                    submod.Status = (submod.Type == SubmodType.Submod && submod.Version != installedVersion)
                        ? SubmodStatus.UpdateAvailable
                        : SubmodStatus.Installed;
                }
                else
                {
                    submod.Status = SubmodStatus.NotInstalled;
                }
            }

            ApplyFilter();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load submods: {ex.Message}");
        }
    }

    private void ApplyFilter()
    {
        FilteredSubmods.Clear();
        var filtered = _allSubmods.Where(s => s.Type == SelectedFilter);
        foreach (var submod in filtered)
        {
            FilteredSubmods.Add(submod);
        }
    }

    private void SelectSubmod(SubmodItem? submod)
    {
        if (submod == null) return;
        CurrentSubmodView = submod;
    }

    private void GoBack(object? _ = null)
    {
        CurrentSubmodView = this; // return to the main list view
    }

    private async Task InstallSubmod(SubmodItem? submod)
    {
        if (submod == null) return;

        submod.IsInstalling = true;
        var tempDir = Path.Combine(Path.GetTempPath(), $"submod_install_{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, "download.zip");

            Debug.WriteLine($"Downloading {submod.Name}...");
            using (var response = await _httpClient.GetAsync(submod.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloadedBytes += bytesRead;
                    if (totalBytes > 0)
                    {
                        submod.InstallProgress = (double)downloadedBytes / totalBytes * 100;
                    }
                }
            }

            Debug.WriteLine("Extracting...");
            var extractPath = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            var sourceDir = GetFirstSubDirectory(extractPath) ?? extractPath;

            switch (submod.Type)
            {
                case SubmodType.Submod:
                    var submodsFolder = Path.Combine(_installDir, "game", "submods");
                    Directory.CreateDirectory(submodsFolder);
                    var targetSubmodDir = Path.Combine(submodsFolder, new DirectoryInfo(sourceDir).Name);
                    CopyDirectory(sourceDir, targetSubmodDir);
                    break;

                case SubmodType.Background:
                case SubmodType.Outfit:
                    CopyDirectory(sourceDir, _installDir);
                    break;
            }

            _installedSubmodVersions[submod.Id] = submod.Version;
            submod.Status = SubmodStatus.Installed;
            SaveInstalledSubmodStatus();
            System.Windows.MessageBox.Show($"{submod.Name} installed successfully!", "Success");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to install submod: {ex.Message}");
            System.Windows.MessageBox.Show($"An error occurred while installing {submod.Name}: {ex.Message}", "Installation Error");
        }
        finally
        {
            submod.IsInstalling = false;
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private async Task UninstallSubmod(SubmodItem? submod)
    {
        if (submod == null || submod.Type != SubmodType.Submod) return;

        var result = System.Windows.MessageBox.Show(
            $"This will attempt to delete the folder for '{submod.Name}' from your 'game/submods' directory. This action cannot be undone. Are you sure?",
            "Confirm Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var submodsFolder = Path.Combine(_installDir, "game", "submods");
            if (!Directory.Exists(submodsFolder))
            {
                throw new DirectoryNotFoundException("The 'game/submods' folder does not exist.");
            }

            var submodDirToDelete = Directory.GetDirectories(submodsFolder)
                .FirstOrDefault(d => d.EndsWith(submod.Id.Split('.').Last(), StringComparison.OrdinalIgnoreCase));

            if (submodDirToDelete != null && Directory.Exists(submodDirToDelete))
            {
                await Task.Run(() => Directory.Delete(submodDirToDelete, true));
                _installedSubmodVersions.Remove(submod.Id);
                submod.Status = SubmodStatus.NotInstalled;
                SaveInstalledSubmodStatus();
                System.Windows.MessageBox.Show($"{submod.Name} has been uninstalled.", "Success");
            }
            else
            {
                System.Windows.MessageBox.Show($"Could not find the directory for '{submod.Name}' to uninstall. You may need to remove it manually.", "Directory Not Found");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to uninstall submod: {ex.Message}");
            System.Windows.MessageBox.Show($"An error occurred: {ex.Message}", "Uninstall Error");
        }
    }

    private void ResetStatus(SubmodItem? submod)
    {
        if (submod == null) return;
        var result = System.Windows.MessageBox.Show(
            "This will mark the submod as 'Not Installed' in the launcher, but will NOT delete any files. Use this if you manually removed files and want to reinstall. Continue?",
            "Reset Installation Status",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            if (_installedSubmodVersions.Remove(submod.Id))
            {
                submod.Status = SubmodStatus.NotInstalled;
                SaveInstalledSubmodStatus();
                System.Windows.MessageBox.Show("Status reset successfully.", "Success");
            }
        }
    }

    private void LoadInstalledSubmodStatus()
    {
        try
        {
            if (File.Exists(_submodStatusFilePath))
            {
                var json = File.ReadAllText(_submodStatusFilePath);
                _installedSubmodVersions = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load submod status: {ex.Message}");
            _installedSubmodVersions = new();
        }
    }

    private void SaveInstalledSubmodStatus()
    {
        try
        {
            var json = JsonSerializer.Serialize(_installedSubmodVersions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_submodStatusFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save submod status: {ex.Message}");
        }
    }

    public void HandleMainGameUninstalled()
    {
        _installedSubmodVersions.Clear();
        foreach (var submod in _allSubmods)
        {
            submod.Status = SubmodStatus.NotInstalled;
        }
        SaveInstalledSubmodStatus();
        ApplyFilter(); // refresh the view to show the changes
    }

    private static string? GetFirstSubDirectory(string path)
    {
        var directories = Directory.GetDirectories(path);
        return directories.Length > 0 ? directories[0] : null;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
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

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public RelayCommand(Action<T?> execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute((T?)parameter);
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public RelayCommand(Action<object?> execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute(parameter);
}

public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public AsyncRelayCommand(Func<T?, Task> execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute((T?)parameter);
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}