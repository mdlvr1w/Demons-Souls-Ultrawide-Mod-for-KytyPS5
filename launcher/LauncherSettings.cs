using System.IO;
using System.Text.Json;

namespace DemonsSoulsLauncher;

public sealed class LauncherSettings
{
    public string KytyFolder { get; set; } = "";
    public string GameFolder { get; set; } = "";
    public bool Ultrawide { get; set; } = true;
    public int RenderPercent { get; set; } = 100;
    public int Language { get; set; } = 1;
    public bool ShadersComplete { get; set; }
    public int CameraStep { get; set; }
    public int CameraFartherKey { get; set; } = 0x21;
    public int CameraCloserKey { get; set; } = 0x22;

    public static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "launcher-settings.json");

    public static LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new LauncherSettings();
            var loaded = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath));
            return loaded ?? new LauncherSettings();
        }
        catch (IOException)
        {
            return new LauncherSettings();
        }
        catch (JsonException)
        {
            return new LauncherSettings();
        }
    }

    public void Save()
    {
        RenderPercent = Math.Clamp(RenderPercent, 50, 100);
        Language = Math.Clamp(Language, 0, 29);
        CameraStep = Math.Clamp(CameraStep, 0, 4);
        if (CameraFartherKey == 0)
            CameraFartherKey = 0x21;
        if (CameraCloserKey == 0)
            CameraCloserKey = 0x22;
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
