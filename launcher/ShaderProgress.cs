using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DemonsSoulsLauncher;

public static class ShaderProgress
{
    public static readonly string[] Languages =
    [
        "Japanese",
        "English (United States)",
        "French (France)",
        "Spanish (Spain)",
        "German",
        "Italian",
        "Dutch",
        "Portuguese (Portugal)",
        "Russian",
        "Korean",
        "Chinese (Traditional)",
        "Chinese (Simplified)",
        "Finnish",
        "Swedish",
        "Danish",
        "Norwegian",
        "Polish",
        "Portuguese (Brazil)",
        "English (United Kingdom)",
        "Turkish",
        "Spanish (Latin America)",
        "Arabic",
        "French (Canada)",
        "Czech",
        "Hungarian",
        "Greek",
        "Romanian",
        "Thai",
        "Vietnamese",
        "Indonesian",
    ];

    private static readonly Regex Shards = new(
        @"shards:\s+(\d+)\s+done,\s+(\d+)\s+running,\s+(\d+)\s+waiting",
        RegexOptions.CultureInvariant);

    public readonly record struct State(int Percent, bool Complete, string Label);

    public static bool TryRead(string line, out State state)
    {
        state = default;
        var text = line.Trim();
        if (text.StartsWith("done in ", StringComparison.Ordinal))
        {
            state = new State(100, true, "Complete");
            return true;
        }
        var match = Shards.Match(text);
        if (!match.Success)
            return false;
        var done = int.Parse(match.Groups[1].Value);
        var running = int.Parse(match.Groups[2].Value);
        var waiting = int.Parse(match.Groups[3].Value);
        var total = done + running + waiting;
        var percent = total == 0 ? 0 : Math.Clamp(done * 100 / total, 0, 99);
        state = new State(percent, false, $"{percent}%");
        return true;
    }

    public static bool HasCache(string kytyFolder, string gameFolder)
    {
        if (!TryGameId(gameFolder, out var id))
            return false;
        var directory = Path.Combine(kytyFolder, "_PipelineCache", "static");
        foreach (var extension in new[] { ".bin", ".binaries", ".blobs" })
        {
            try
            {
                if (new FileInfo(Path.Combine(directory, id + extension)).Length > 0)
                    return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return false;
    }

    private static bool TryGameId(string gameFolder, out string id)
    {
        id = "";
        var paramPath = Path.Combine(gameFolder, "sce_sys", "param.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(paramPath));
            var root = document.RootElement;
            var title = root.TryGetProperty("titleId", out var titleId) ? titleId.GetString() : "";
            var version = root.TryGetProperty("contentVersion", out var content) ? content.GetString() : "";
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(version))
                return false;
            id = title + "_" + version;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
