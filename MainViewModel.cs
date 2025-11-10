using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;

namespace justsayo_win;

public class MainViewModel : INotifyPropertyChanged
{
    private const string GameId = "just-sayori";
    private const string JsonUrl = "https://traduction-club.live/api/justsayori/justsayori_launcher.json";
    private const string DdlcZipFileName = "ddlc-win.zip";

    private readonly string _launcherDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_data");
    private readonly string _storedDdlcZipPath;
    private readonly string _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_data", "settings.json");
    private readonly string _installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JustSayori");
    private string _executablePath = "";

    private static readonly HttpClient _httpClient = new();
    private GameInfo? _remoteGameInfo;
    private ActionState _currentState = ActionState.Checking;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _processCheckTimer;
    private CancellationTokenSource? _installCts;
    private SubmodsViewModel? _submodsViewModel;
    private int _dotCount;
    private string _baseButtonText = "";

    private string _statusText = "Consulting information...";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private string _buttonText = "Checking...";
    public string ButtonText { get => _buttonText; set => SetField(ref _buttonText, value); }

    private bool _isButtonEnabled = false;
    public bool IsButtonEnabled { get => _isButtonEnabled; set => SetField(ref _isButtonEnabled, value); }

    private int _downloadProgress = 0;
    public int DownloadProgress { get => _downloadProgress; set => SetField(ref _downloadProgress, value); }

    private Visibility _progressBarVisibility = Visibility.Collapsed;
    public Visibility ProgressBarVisibility { get => _progressBarVisibility; set => SetField(ref _progressBarVisibility, value); }

    private object _currentView;
    public object CurrentView { get => _currentView; set => SetField(ref _currentView, value); }

    private bool _isLaunching;
    public bool IsLaunching { get => _isLaunching; set => SetField(ref _isLaunching, value); }

    private bool _isConfirmingUninstall;
    public bool IsConfirmingUninstall { get => _isConfirmingUninstall; set => SetField(ref _isConfirmingUninstall, value); }

    private bool _isUninstalling;
    public bool IsUninstalling { get => _isUninstalling; set => SetField(ref _isUninstalling, value); }

    private bool _isInstalling;
    public bool IsInstalling { get => _isInstalling; set => SetField(ref _isInstalling, value); }

    private SettingsModel _settings = new();
    public OnLaunchBehavior SelectedLaunchBehavior
    {
        get => _settings.LaunchBehavior;
        set
        {
            if (_settings.LaunchBehavior == value) return;
            _settings.LaunchBehavior = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    // --- Commands ---
    public ICommand MainActionCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand ConfirmUninstallCommand { get; }
    public ICommand CancelUninstallCommand { get; }
    public ICommand CancelInstallCommand { get; }
    public ICommand LocateFolderCommand { get; }

    private enum ActionState { Checking, SelectDdlc, Install, Update, Play }

    public bool IsGameInstalled => _currentState == ActionState.Play || _currentState == ActionState.Update;

    public event Action? RequestMinimizeToTray;

    public MainViewModel()
    {
        LoadSettings();
        _currentView = new PlayView { DataContext = this };

        _storedDdlcZipPath = Path.Combine(_launcherDataDir, DdlcZipFileName);
        Directory.CreateDirectory(_launcherDataDir);

        MainActionCommand = new AsyncRelayCommand(PerformMainAction);
        UninstallCommand = new RelayCommand(_ => IsConfirmingUninstall = true);
        ConfirmUninstallCommand = new AsyncRelayCommand(ConfirmUninstallAsync);
        CancelUninstallCommand = new RelayCommand(_ => IsConfirmingUninstall = false);
        CancelInstallCommand = new RelayCommand(CancelInstallation);
        LocateFolderCommand = new AsyncRelayCommand(LocateGameFolder);

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _animationTimer.Tick += AnimationTimer_Tick;

        _processCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; // Check every 2 seconds
        _processCheckTimer.Tick += ProcessCheckTimer_Tick;

        _ = CheckGameStatusAsync();
    }

    private void CancelInstallation(object? _ = null)
    {
        _installCts?.Cancel();
    }

    public void Navigate(string viewName)
    {
        switch (viewName)
        {
            case "Play":
                CurrentView = new PlayView { DataContext = this };
                break;
            case "Submods":
                _submodsViewModel ??= new SubmodsViewModel(_installDir, IsGameInstalled);
                CurrentView = new SubmodsView { DataContext = _submodsViewModel };
                break;
            case "Settings":
                CurrentView = new SettingsView { DataContext = this };
                break;
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                _settings = JsonSerializer.Deserialize<SettingsModel>(json) ?? new SettingsModel();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load settings: {ex.Message}");
            _settings = new SettingsModel();
        }
    }

    private void SaveSettings()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
    }
    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        _dotCount = (_dotCount + 1) % 4;
        ButtonText = $"{_baseButtonText}{new string('.', _dotCount)}";
    }

    private void ProcessCheckTimer_Tick(object? sender, EventArgs e)
    {
        if (_remoteGameInfo == null || string.IsNullOrEmpty(_remoteGameInfo.ExecutableName)) return;

        var processName = Path.GetFileNameWithoutExtension(_remoteGameInfo.ExecutableName);
        var processes = Process.GetProcessesByName(processName);

        if (processes.Length > 0 && (_currentState == ActionState.Play || _currentState == ActionState.Update))
        {
            ButtonText = "Playing...";
            IsButtonEnabled = false;
        }
        else if (processes.Length == 0 && ButtonText == "Playing...")
        {
            // Revert the button to its correct state based on whether an update is needed or not
            var buttonText = _currentState == ActionState.Update ? "Update" : "Play";
            ButtonText = buttonText;
            IsButtonEnabled = true;
        }
    }

    private async Task CheckGameStatusAsync()
    {
        IsButtonEnabled = false;
        _currentState = ActionState.Checking;
        ButtonText = "Checking...";
        StatusText = "Checking for game information...";

        try
        {
            var response = await _httpClient.GetStringAsync(JsonUrl);
            var projectsNode = JsonDocument.Parse(response).RootElement.GetProperty("proyectos");
            var allGames = JsonSerializer.Deserialize<GameInfo[]>(projectsNode.GetRawText());
            _remoteGameInfo = Array.Find(allGames ?? [], g => g.Id == GameId);

            if (_remoteGameInfo == null || string.IsNullOrEmpty(_remoteGameInfo.ExecutableName))
            {
                StatusText = "Could not find game information on the server.";
                ButtonText = "Error";
                return;
            }

            _executablePath = Path.Combine(_installDir, _remoteGameInfo.ExecutableName);
            var versionFilePath = Path.Combine(_installDir, "version.txt");

            if (File.Exists(_executablePath))
            {
                var localVersion = File.Exists(versionFilePath) ? await File.ReadAllTextAsync(versionFilePath) : "0.0.0";
                if (localVersion == _remoteGameInfo.Version)
                {
                    UpdateState(ActionState.Play, $"Version {localVersion} installed.", "Play");
                    _processCheckTimer.Start();
                }
                else
                {
                    UpdateState(ActionState.Update, $"Update available: v{_remoteGameInfo.Version}", "Update");
                    _processCheckTimer.Start();
                }
            }
            else if (File.Exists(_storedDdlcZipPath))
            {
                UpdateState(ActionState.Install, "Ready to install the best mod of this world.", "Install");
            }
            else
            {
                UpdateState(ActionState.SelectDdlc, "Please select your ddlc-win.zip file to continue.", "Select ddlc-win.zip");
            }
        }
        catch (Exception ex)
        {
            StatusText = "Error connecting to the server. Please check your connection.";
            ButtonText = "Retry";
            IsButtonEnabled = true;
            Debug.WriteLine($"Error checking status: {ex.Message}");
        }
    }

    private void UpdateState(ActionState newState, string status, string button)
    {
        if (newState == ActionState.Install || newState == ActionState.SelectDdlc)
        {
            _processCheckTimer.Stop();
        }

        _currentState = newState;
        StatusText = status;
        ButtonText = button;
        IsButtonEnabled = true;
        OnPropertyChanged(nameof(IsGameInstalled));
    }

    private async Task PerformMainAction()
    {
        switch (_currentState)
        {
            case ActionState.SelectDdlc:
                SelectAndStoreDdlcZip();
                break;
            case ActionState.Install:
            case ActionState.Update:
                await InstallOrUpdateGameAsync();
                break;
            case ActionState.Play:
                await LaunchGame();
                break;
            case ActionState.Checking:
            default:
                await CheckGameStatusAsync();
                break;
        }
    }

    private void SelectAndStoreDdlcZip()
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ddlc-win Zip File|*.zip",
            Title = "Select your original ddlc-win.zip file"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                File.Copy(openFileDialog.FileName, _storedDdlcZipPath, true);
                StatusText = "ddlc-win.zip stored successfully! Ready to install.";
                ButtonText = "Install";
                _currentState = ActionState.Install;
            }
            catch (Exception ex)
            {
                StatusText = "Error storing ddlc-win.zip. Please try again.";
                Debug.WriteLine($"Error copying ddlc-win.zip: {ex.Message}");
            }
        }
    }

    private async Task ConfirmUninstallAsync()
    {
        IsConfirmingUninstall = false;
        IsUninstalling = true;

        try
        {
            await Task.Delay(100);

            if (Directory.Exists(_installDir))
            {
                await Task.Run(() => Directory.Delete(_installDir, true));
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"An error occurred during uninstallation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsUninstalling = false;
            // Re-check the status to update the main "Play" button and overall state
            _submodsViewModel?.HandleMainGameUninstalled();
            await CheckGameStatusAsync();
        }
    }

    private Task LocateGameFolder()
    {
        if (!IsGameInstalled || !Directory.Exists(_installDir))
        {
            System.Windows.MessageBox.Show("Game installation folder not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.CompletedTask;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_installDir)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open folder: {ex.Message}");
            System.Windows.MessageBox.Show("Could not open the game folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return Task.CompletedTask;
    }

    private async Task InstallOrUpdateGameAsync()
    {
        _installCts = new CancellationTokenSource();
        var token = _installCts.Token;

        if (string.IsNullOrEmpty(_remoteGameInfo?.DownloadUrl)) return;

        IsInstalling = true;
        IsButtonEnabled = false;
        ProgressBarVisibility = Visibility.Visible;
        var tempDir = Path.Combine(Path.GetTempPath(), "JustSayoriInstall");
        
        _baseButtonText = "Downloading";
        _dotCount = 0;
        _animationTimer.Start();

        if (_currentState == ActionState.Update && !File.Exists(_storedDdlcZipPath))
        {
            _animationTimer.Stop();
            UpdateState(ActionState.SelectDdlc, "The original ddlc-win.zip is required to update. Please select it.", "Select ddlc-win.zip");
            return;
        }

        try
        {
            // Cleanup old directories
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);
            if (_currentState == ActionState.Update && Directory.Exists(_installDir))
            {
                Directory.Delete(_installDir, true);
            }
            Directory.CreateDirectory(_installDir);

            StatusText = "Downloading patch...";
            var patchZipPath = Path.Combine(tempDir, "patch.zip");
            await DownloadFileAsync(_remoteGameInfo.DownloadUrl, patchZipPath, token);

            var ddlcExtractPath = Path.Combine(tempDir, "ddlc");
            var patchExtractPath = Path.Combine(tempDir, "patch");
            _baseButtonText = "Extracting";
            StatusText = "Extracting base game...";
            await Task.Run(() => ZipFile.ExtractToDirectory(_storedDdlcZipPath, ddlcExtractPath));
            token.ThrowIfCancellationRequested();
            StatusText = "Extracting patch...";
            await Task.Run(() => ZipFile.ExtractToDirectory(patchZipPath, patchExtractPath));
            token.ThrowIfCancellationRequested();

            _baseButtonText = "Installing";
            StatusText = "Installing...";
            await Task.Run(() =>
            {
                var ddlcGameDir = GetFirstSubDirectory(ddlcExtractPath);
                if (ddlcGameDir == null) throw new DirectoryNotFoundException("Could not find the main game directory inside ddlc-win.zip.");

                // Copy DDLC files to the final install directory
                CopyDirectory(ddlcGameDir, _installDir);

                var patchSourceDir = GetFirstSubDirectory(patchExtractPath);
                if (patchSourceDir == null)
                {
                    // If no subfolder, the patch files are likely at the root of the zip.
                    patchSourceDir = patchExtractPath;
                }

                // Copy the patch files over the top of the DDLC installation
                CopyDirectory(patchSourceDir, _installDir);

                var scriptFileToDelete = Path.Combine(_installDir, "game", "scripts.rpa");
                if (File.Exists(scriptFileToDelete))
                {
                    File.Delete(scriptFileToDelete);
                }
            });

            var versionFilePath = Path.Combine(_installDir, "version.txt");
            await File.WriteAllTextAsync(versionFilePath, _remoteGameInfo.Version);

            UpdateState(ActionState.Play, "Installation complete!", "Play");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Installation cancelled.";
            ButtonText = "Install";
            IsButtonEnabled = true;
            Debug.WriteLine("Installation was cancelled by the user.");
        }
        catch (Exception ex)
        {
            StatusText = "An error occurred during installation.";
            ButtonText = "Retry Install";
            IsButtonEnabled = true;
            Debug.WriteLine($"Error installing: {ex.Message}");
        }
        finally
        {
            _animationTimer.Stop();
            IsInstalling = false;
            _installCts?.Dispose();
            _installCts = null;
            ProgressBarVisibility = Visibility.Collapsed;
            DownloadProgress = 0;
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var downloadedBytes = 0L;

        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;
            if (totalBytes > 0)
            {
                DownloadProgress = (int)((double)downloadedBytes / totalBytes * 100);
            }
        }
    }

    private async Task LaunchGame()
    {
        try
        {
            if (File.Exists(_executablePath))
            {
                IsLaunching = true;
                ButtonText = "Launching...";
                IsButtonEnabled = false;

                await Task.Delay(1000);

                IsLaunching = false;

                Process.Start(new ProcessStartInfo(_executablePath)
                {
                    WorkingDirectory = _installDir
                });

                ButtonText = "Playing...";
                IsButtonEnabled = false;
                _processCheckTimer.Start();

                // Handle launcher behavior based on settings
                switch (SelectedLaunchBehavior)
                {
                    case OnLaunchBehavior.Close:
                        System.Windows.Application.Current.Shutdown();
                        break;
                    case OnLaunchBehavior.MinimizeToTray:
                        RequestMinimizeToTray?.Invoke();
                        break;
                    case OnLaunchBehavior.DoNothing:
                    default:
                        break;
                }
            }
            else
            {
                UpdateState(ActionState.Install, "Game executable not found. Please reinstall.", "Install");
            }
        }
        catch (Exception ex)
        {
            StatusText = "Failed to launch the game.";
            Debug.WriteLine($"Error launching: {ex.Message}");
        }
    }

    #region Helper Methods
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
    #endregion

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

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
