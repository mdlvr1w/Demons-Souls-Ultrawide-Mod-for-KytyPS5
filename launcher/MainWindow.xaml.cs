using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DemonsSoulsLauncher;

public partial class MainWindow : Window
{
    private readonly LauncherSettings _settings = LauncherSettings.Load();
    private bool _ready;
    private bool _promptRun;
    private bool _draftUltrawide = true;
    private int _draftPercent = 100;
    private int _awaitingProcess;
    private int _cameraPid;
    private bool _fartherDown;
    private bool _closerDown;
    private int _draftFarther = 0x21;
    private int _draftCloser = 0x22;
    private int _keyCapture;
    private Process? _shaders;
    private readonly string _shaderLog = Path.Combine(AppContext.BaseDirectory, "precompile-progress.txt");

    public MainWindow()
    {
        InitializeComponent();
        _draftUltrawide = _settings.Ultrawide;
        _draftPercent = Math.Clamp(_settings.RenderPercent, 50, 100);
        RenderSlider.Value = _draftPercent;
        ShowRender(_draftPercent);
        ApplyMode(_draftUltrawide);
        foreach (var name in ShaderProgress.Languages)
            LanguageBox.Items.Add(name);
        LanguageBox.SelectedIndex = Math.Clamp(_settings.Language, 0, ShaderProgress.Languages.Length - 1);
        _draftFarther = _settings.CameraFartherKey;
        _draftCloser = _settings.CameraCloserKey;
        RefreshArt();
        _ready = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => WatchGame();
        timer.Start();
        var camera = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        camera.Tick += (_, _) => PollCamera();
        camera.Start();
        WatchGame();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_keyCapture != 0)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape || key == Key.None)
            {
                _keyCapture = 0;
                ShowKeys();
                return;
            }
            if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                return;
            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey <= 0)
                return;
            var other = _keyCapture == 1 ? _draftCloser : _draftFarther;
            if (virtualKey == other)
            {
                SettingsStatus.Text = "That key is already used.";
                return;
            }
            if (_keyCapture == 1)
                _draftFarther = virtualKey;
            else
                _draftCloser = virtualKey;
            _keyCapture = 0;
            SettingsStatus.Text = "";
            ShowKeys();
            return;
        }
        if (e.Key == Key.Escape)
            Back_Click(sender, e);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        OpenSettings(promptRun: false);

    private void OpenSaves_Click(object sender, RoutedEventArgs e)
    {
        SettingsPage.Visibility = Visibility.Collapsed;
        FrontPage.Visibility = Visibility.Collapsed;
        SavesStatus.Text = "";
        SavesPage.Visibility = Visibility.Visible;
        RefreshSaves();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (SavesPage.Visibility == Visibility.Visible)
        {
            SavesPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Visible;
            return;
        }
        CloseToFront();
    }

    private void BrowseKyty_Click(object sender, RoutedEventArgs e)
    {
        var picked = PickFolder("Kyty folder");
        if (picked == null)
            return;
        KytyBox.Text = picked;
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        var picked = PickFolder("Game folder");
        if (picked == null)
            return;
        GameBox.Text = picked;
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        _draftUltrawide = sender is Button { Tag: string tag } && tag == "wide";
        ApplyMode(_draftUltrawide);
        ShowRender(_draftPercent);
    }

    private void RenderSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready)
            return;
        _draftPercent = (int)Math.Round(RenderSlider.Value);
        ShowRender(_draftPercent);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (GameHost.EmulatorIsRunning())
        {
            ShowRunning(true);
            return;
        }
        if (!GameHost.PathsReady(_settings, out _))
        {
            OpenSettings(promptRun: true);
            return;
        }
        if (!GameHost.CanPlay(_settings, out var reason))
        {
            StatusText.Text = reason;
            return;
        }
        StartGame();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var draft = ReadDraft();
        if (_promptRun)
        {
            if (!GameHost.PathsReady(draft, out var pathReason))
            {
                SettingsStatus.Text = pathReason;
                return;
            }
            if (!GameHost.CanPlay(draft, out var playReason))
            {
                Commit(draft);
                CloseToFront();
                StatusText.Text = playReason;
                return;
            }
            Commit(draft);
            CloseToFront();
            StartGame();
            return;
        }

        Commit(draft);
        CloseToFront();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveFolderReady())
            return;
        var folder = PickFolder("Folder for the save copy");
        if (folder == null)
            return;
        var error = SaveArchive.Export(_settings.KytyFolder, folder);
        SavesStatus.Text = error ?? "Save exported.";
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveFolderReady())
            return;
        var dialog = new OpenFileDialog
        {
            Title = "Import save",
            Filter = "Save copy (*.zip)|*.zip",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        var confirm = MessageBox.Show(
            this,
            "Replace the current save with this copy? The current save is kept in _SaveData\\_backups.",
            "Demon's Souls",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;
        var error = SaveArchive.Restore(_settings.KytyFolder, dialog.FileName);
        SavesStatus.Text = error ?? "Save imported. The previous one is in _SaveData\\_backups.";
    }

    private void OpenSettings(bool promptRun)
    {
        _promptRun = promptRun;
        KytyBox.Text = _settings.KytyFolder;
        GameBox.Text = _settings.GameFolder;
        _draftUltrawide = _settings.Ultrawide;
        _draftPercent = Math.Clamp(_settings.RenderPercent, 50, 100);
        RenderSlider.Value = _draftPercent;
        ApplyMode(_draftUltrawide);
        ShowRender(_draftPercent);
        var extras = promptRun ? Visibility.Collapsed : Visibility.Visible;
        RenderRow.Visibility = extras;
        RenderHint.Visibility = extras;
        ShaderSection.Visibility = extras;
        CameraSection.Visibility = extras;
        ManageSavesButton.Visibility = extras;
        _draftFarther = _settings.CameraFartherKey;
        _draftCloser = _settings.CameraCloserKey;
        _keyCapture = 0;
        ShowKeys();
        LanguageBox.SelectedIndex = Math.Clamp(_settings.Language, 0, LanguageBox.Items.Count - 1);
        SaveSettingsButton.Content = promptRun ? "Save and run" : "Save";
        SettingsStatus.Text = "";
        FrontPage.Visibility = Visibility.Collapsed;
        SavesPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
    }

    private void CloseToFront()
    {
        _promptRun = false;
        SettingsPage.Visibility = Visibility.Collapsed;
        SavesPage.Visibility = Visibility.Collapsed;
        FrontPage.Visibility = Visibility.Visible;
    }

    private LauncherSettings ReadDraft() => new()
    {
        KytyFolder = KytyBox.Text.Trim(),
        GameFolder = GameBox.Text.Trim(),
        Ultrawide = _draftUltrawide,
        RenderPercent = _draftPercent,
        Language = LanguageBox.SelectedIndex < 0 ? _settings.Language : LanguageBox.SelectedIndex,
        ShadersComplete = _settings.ShadersComplete,
        CameraStep = _settings.CameraStep,
        CameraFartherKey = _draftFarther,
        CameraCloserKey = _draftCloser,
    };

    private void Commit(LauncherSettings draft)
    {
        _settings.KytyFolder = draft.KytyFolder;
        _settings.GameFolder = draft.GameFolder;
        _settings.Ultrawide = draft.Ultrawide;
        _settings.RenderPercent = draft.RenderPercent;
        _settings.Language = draft.Language;
        _settings.ShadersComplete = draft.ShadersComplete;
        _settings.CameraStep = draft.CameraStep;
        _settings.CameraFartherKey = draft.CameraFartherKey;
        _settings.CameraCloserKey = draft.CameraCloserKey;
        _settings.Save();
        RefreshArt();
    }

    private void StartGame()
    {
        if (GameHost.EmulatorIsRunning())
        {
            ShowRunning(true);
            return;
        }
        try
        {
            var running = string.Join(",", System.Diagnostics.Process.GetProcessesByName("kyty_emulator").Select(p => p.Id));
            GameHost.Play(_settings);
            if (!_settings.Ultrawide)
                StatusText.Text = "16:9 started.";
            else if (LiveFix.StartWatcher(running))
                StatusText.Text = "Ultrawide started.";
            else
                StatusText.Text = "Ultrawide started. The preview fix did not attach.";
            _awaitingProcess = 8;
            ShowRunning(true);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "The game did not start.";
            ShowRunning(false);
        }
    }

    private void WatchGame()
    {
        var running = GameHost.EmulatorIsRunning();
        if (running)
            _awaitingProcess = 0;
        else if (_awaitingProcess > 0)
        {
            _awaitingProcess--;
            running = true;
        }
        ShowRunning(running);
        RefreshSaves();
        PollShaders();
    }

    private void FartherKey_Click(object sender, RoutedEventArgs e) => BeginKeyCapture(1);

    private void CloserKey_Click(object sender, RoutedEventArgs e) => BeginKeyCapture(2);

    private void BeginKeyCapture(int which)
    {
        _keyCapture = which;
        SettingsStatus.Text = "Press a key. Escape cancels.";
        ShowKeys();
    }

    private void ShowKeys()
    {
        FartherKeyButton.Content = _keyCapture == 1 ? "Press a key" : CameraDistance.KeyName(_draftFarther);
        CloserKeyButton.Content = _keyCapture == 2 ? "Press a key" : CameraDistance.KeyName(_draftCloser);
    }

    private void PollCamera()
    {
        var processes = Process.GetProcessesByName("kyty_emulator");
        int pid;
        try
        {
            pid = processes.Length == 0 ? 0 : processes[0].Id;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
        if (pid == 0)
        {
            _cameraPid = 0;
            _fartherDown = false;
            _closerDown = false;
            return;
        }
        if (pid != _cameraPid)
        {
            _cameraPid = pid;
            _fartherDown = false;
            _closerDown = false;
        }
        CameraDistance.TryApply(pid, _settings.CameraStep);
        if (!CameraDistance.IsForeground(pid))
        {
            _fartherDown = false;
            _closerDown = false;
            return;
        }

        var farther = CameraDistance.IsDown(_settings.CameraFartherKey);
        var closer = CameraDistance.IsDown(_settings.CameraCloserKey);
        var fartherEdge = farther && !_fartherDown;
        var closerEdge = closer && !_closerDown;
        _fartherDown = farther;
        _closerDown = closer;
        if (fartherEdge == closerEdge)
            return;
        var next = CameraDistance.Move(_settings.CameraStep, fartherEdge ? 1 : -1);
        if (next == _settings.CameraStep)
            return;
        _settings.CameraStep = next;
        try
        {
            _settings.Save();
        }
        catch (IOException)
        {
        }
        CameraDistance.TryApply(pid, next);
    }

    private void ShowRunning(bool running)
    {
        PlayButton.IsEnabled = !running;
        PlayButton.ApplyTemplate();
        if (PlayButton.Template.FindName("PlayLabel", PlayButton) is TextBlock label)
            label.Text = running ? "Running" : "Play";
    }

    private void RefreshSaves()
    {
        if (SavesPage.Visibility != Visibility.Visible)
            return;
        var running = GameHost.EmulatorIsRunning();
        ExportButton.IsEnabled = !running;
        ImportButton.IsEnabled = !running;
        if (running)
            SavesStatus.Text = "Close the game before copying a save.";
        else if (SavesStatus.Text.StartsWith("Close the game", StringComparison.Ordinal))
            SavesStatus.Text = "";
    }

    private bool SaveFolderReady()
    {
        if (GameHost.EmulatorIsRunning())
        {
            SavesStatus.Text = "Close the game before copying a save.";
            return false;
        }
        if (Directory.Exists(_settings.KytyFolder) && File.Exists(GameHost.EmulatorPath(_settings.KytyFolder)))
            return true;
        SavesStatus.Text = "Choose the Kyty folder in Settings.";
        return false;
    }

    private void Precompile_Click(object sender, RoutedEventArgs e)
    {
        if (ShadersRunning())
            return;
        var draft = ReadDraft();
        if (GameHost.EmulatorIsRunning())
        {
            SettingsStatus.Text = "Close the game before precompiling shaders.";
            return;
        }
        if (!GameHost.PathsReady(draft, out var reason))
        {
            SettingsStatus.Text = reason;
            return;
        }
        var script = Path.Combine(draft.KytyFolder, "precompile-windows.ps1");
        if (!File.Exists(script))
        {
            SettingsStatus.Text = "precompile-windows.ps1 is not in the Kyty folder.";
            return;
        }
        try
        {
            File.WriteAllText(_shaderLog, "");
        }
        catch (IOException)
        {
            SettingsStatus.Text = "The shader progress file is in use.";
            return;
        }
        _settings.ShadersComplete = false;
        _settings.Save();
        ShaderBar.IsIndeterminate = true;
        ShaderBar.Value = 0;
        ShaderLabel.Text = "Preparing";
        PrecompileButton.IsEnabled = false;
        SettingsStatus.Text = "";
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            WorkingDirectory = draft.KytyFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = "/c powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + script
                + "\" -Game \"" + draft.GameFolder + "\" > \"" + _shaderLog + "\" 2>&1",
        };
        try
        {
            _shaders = Process.Start(start);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            SettingsStatus.Text = "Shaders did not start.";
            ShaderBar.IsIndeterminate = false;
            PrecompileButton.IsEnabled = true;
        }
    }

    private void PollShaders()
    {
        var text = ReadShared(_shaderLog);
        ShaderProgress.State? latest = null;
        string? failure = null;
        if (!string.IsNullOrEmpty(text))
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (ShaderProgress.TryRead(line, out var state))
                    latest = state;
                else if (line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    failure = line;
            }
        }
        var running = ShadersRunning();
        if (!running && ShaderProgress.HasCache(_settings.KytyFolder, _settings.GameFolder))
        {
            if (!_settings.ShadersComplete)
            {
                _settings.ShadersComplete = true;
                _settings.Save();
            }
            ShowShaders(true, "Complete", 100);
            PrecompileButton.IsEnabled = !GameHost.EmulatorIsRunning();
            return;
        }
        if (latest is { Complete: true } finished)
        {
            if (!_settings.ShadersComplete)
            {
                _settings.ShadersComplete = true;
                _settings.Save();
            }
            ShowShaders(true, finished.Label, 100);
            PrecompileButton.IsEnabled = !running && !GameHost.EmulatorIsRunning();
            return;
        }
        if (latest is { } progress)
            ShowShaders(false, progress.Label, progress.Percent);
        else if (running)
            ShowShaders(false, "Preparing", 0);
        else if (_settings.ShadersComplete)
            ShowShaders(true, "Complete", 100);
        else if (failure != null && _shaders != null)
        {
            ShaderBar.IsIndeterminate = false;
            ShaderLabel.Text = failure;
        }
        PrecompileButton.IsEnabled = !running && !GameHost.EmulatorIsRunning();
    }

    private bool ShadersRunning() =>
        _shaders is { HasExited: false }
        || Process.GetProcessesByName("kyty_shader_precompile").Length > 0;

    private void ShowShaders(bool complete, string label, int percent)
    {
        ShaderBar.IsIndeterminate = !complete && label == "Preparing";
        ShaderBar.Value = complete ? 100 : percent;
        ShaderLabel.Text = complete ? "Complete" : label;
    }

    private static string ReadShared(string path)
    {
        if (!File.Exists(path))
            return "";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytes = new byte[stream.Length];
            var read = stream.Read(bytes, 0, bytes.Length);
            if (read >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, read - 2);
            if (read >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, read - 3);
            if (read > 4 && bytes[1] == 0 && bytes[3] == 0)
                return Encoding.Unicode.GetString(bytes, 0, read);
            return Encoding.UTF8.GetString(bytes, 0, read);
        }
        catch (IOException)
        {
            return "";
        }
    }

    private void RefreshArt()
    {
        var info = GameInfo.Load(_settings.GameFolder);
        TitleText.Text = info.Title;
        DetailText.Text = info.Detail;
        Backdrop.Source = info.Backdrop;
        Tile.Source = info.Tile;
        Title = info.Title;
    }

    private void ApplyMode(bool ultrawide)
    {
        StyleMode(WideButton, !ultrawide);
        StyleMode(UltraButton, ultrawide);
    }

    private static void StyleMode(Button button, bool selected)
    {
        button.Background = selected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xC6, 0xA1, 0x5B))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        button.BorderBrush = selected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC6, 0xA1, 0x5B))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0xC6, 0xA1, 0x5B));
    }

    private void ShowRender(int percent)
    {
        if (RenderLabel == null)
            return;
        var (width, height) = GameHost.RenderSize(percent, _draftUltrawide);
        RenderLabel.Text = $"{percent}%   ·   {width}×{height}";
    }

    private string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
}
