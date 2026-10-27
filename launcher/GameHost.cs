using System.Diagnostics;
using System.IO;

namespace DemonsSoulsLauncher;

public static class GameHost
{
    public const string TitleId = "PPSA01341";

    public static bool EmulatorIsRunning() =>
        Process.GetProcessesByName("kyty_emulator").Length > 0;

    public static string EmulatorPath(string kytyFolder) =>
        Path.Combine(kytyFolder, "kyty_emulator.exe");

    public static string ScriptPath(string kytyFolder) =>
        Path.Combine(kytyFolder, "run-windows.ps1");

    public static string PatchPath() =>
        Path.Combine(AppContext.BaseDirectory, "ultrawide-3440x1440.json");

    public static bool PathsReady(LauncherSettings settings, out string reason)
    {
        if (string.IsNullOrWhiteSpace(settings.KytyFolder)
            || !File.Exists(EmulatorPath(settings.KytyFolder))
            || !File.Exists(ScriptPath(settings.KytyFolder)))
        {
            reason = "Choose the Kyty folder. It needs kyty_emulator.exe and run-windows.ps1.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(settings.GameFolder)
            || !File.Exists(Path.Combine(settings.GameFolder, "eboot.bin"))
            || !Directory.Exists(Path.Combine(settings.GameFolder, "sce_sys")))
        {
            reason = "Choose the game folder. It needs eboot.bin and sce_sys.";
            return false;
        }
        reason = "";
        return true;
    }

    public static bool CanPlay(LauncherSettings settings, out string reason)
    {
        if (!PathsReady(settings, out reason))
            return false;
        if (settings.Ultrawide && !File.Exists(PatchPath()))
        {
            reason = "The ultrawide patch is missing next to this launcher.";
            return false;
        }
        reason = "";
        return true;
    }

    public static (int Width, int Height) RenderSize(int percent, bool ultrawide)
    {
        percent = Math.Clamp(percent, 50, 100);
        var fullWidth = ultrawide ? 3440 : 1920;
        var fullHeight = ultrawide ? 1440 : 1080;
        var minHeight = ultrawide ? 720 : 540;
        var height = (int)Math.Round(fullHeight * percent / 100.0);
        height = Math.Clamp(height, minHeight, fullHeight);
        var width = (int)Math.Round(fullWidth * percent / 100.0);
        return (width, height);
    }

    public static ProcessStartInfo BuildStart(LauncherSettings settings)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            WorkingDirectory = settings.KytyFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(ScriptPath(settings.KytyFolder));
        start.ArgumentList.Add("-Game");
        start.ArgumentList.Add(settings.GameFolder);
        start.ArgumentList.Add("-Language");
        start.ArgumentList.Add(Math.Clamp(settings.Language, 0, 29).ToString());
        start.ArgumentList.Add("-Fullscreen");
        if (settings.Ultrawide)
        {
            start.ArgumentList.Add("-Width");
            start.ArgumentList.Add("3440");
            start.ArgumentList.Add("-Height");
            start.ArgumentList.Add("1440");
            start.ArgumentList.Add("-Patch");
            start.ArgumentList.Add(PatchPath());
        }
        else
        {
            start.ArgumentList.Add("-Width");
            start.ArgumentList.Add("1920");
            start.ArgumentList.Add("-Height");
            start.ArgumentList.Add("1080");
            start.ArgumentList.Add("-AspectFit");
        }

        var fullHeight = settings.Ultrawide ? 1440 : 1080;
        var (_, height) = RenderSize(settings.RenderPercent, settings.Ultrawide);
        if (height < fullHeight)
        {
            start.ArgumentList.Add("-RenderHeight");
            start.ArgumentList.Add(height.ToString());
        }

        return start;
    }

    public static void Play(LauncherSettings settings) => Process.Start(BuildStart(settings));
}
