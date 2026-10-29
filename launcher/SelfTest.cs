using System.IO;

namespace DemonsSoulsLauncher;

public static class SelfTest
{
    public static int Run()
    {
        CheckRender();
        CheckLaunch();
        CheckPicture();
        CheckFixes();
        CheckShaders();
        CheckCamera();
        CheckSaves();
        Console.WriteLine("self-test ok");
        return 0;
    }

    private static void CheckRender()
    {
        var full = GameHost.RenderSize(100, 3440, 1440);
        var almost = GameHost.RenderSize(98, 3440, 1440);
        var floor = GameHost.RenderSize(50, 3440, 1440);
        Expect(full == (3440, 1440), "100%");
        Expect(almost == (3371, 1411), "98%");
        Expect(floor == (1720, 720), "50%");
        Expect(GameHost.RenderSize(100, 1920, 1080) == (1920, 1080), "16:9 100%");
        Expect(GameHost.RenderSize(98, 1920, 1080) == (1882, 1058), "16:9 98%");
    }

    private static void CheckLaunch()
    {
        var settings = new LauncherSettings
        {
            KytyFolder = @"D:\Kyty",
            GameFolder = @"D:\Game",
            Ultrawide = true,
            RenderPercent = 98,
        };
        var wide = GameHost.BuildStart(settings);
        Expect(wide.FileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase), "powershell");
        Expect(wide.ArgumentList.Contains(GameHost.ScriptPath(settings.KytyFolder)), "script");
        Expect(wide.ArgumentList.Contains("3440") && wide.ArgumentList.Contains("1440"), "ultrawide size");
        Expect(wide.ArgumentList.Contains("-Patch"), "patch flag");
        Expect(wide.ArgumentList.Contains(GameHost.PatchPath()), "patch path");
        Expect(wide.ArgumentList.Contains("-RenderHeight") && wide.ArgumentList.Contains("1411"), "render height");
        Expect(!wide.ArgumentList.Contains("-AspectFit"), "no fit on ultrawide");
        Expect(!wide.ArgumentList.Contains("-Follow") && !wide.ArgumentList.Contains("-Prompt"), "no log follow");
        Expect(wide.WorkingDirectory == @"D:\Kyty", "working directory");
        var wideArgs = wide.ArgumentList.ToList();
        var wideLanguage = wideArgs.IndexOf("-Language");
        Expect(wideLanguage >= 0 && wideArgs[wideLanguage + 1] == "1", "language");
        Expect(wide.Environment["SDL_VULKAN_LIBRARY"] == GameHost.VulkanPath(), "vulkan library");
        Expect(wide.Environment["CODEX_UI_SHADER_HASH"] == GameHost.ShaderHashes, "shader hash");
        Expect(wide.Environment["CODEX_UI_SCALE"] == "1", "ui scale");
        Expect(wide.Environment["CODEX_UI_OUTPUT_WIDTH"] == "5160", "output width");

        settings.Ultrawide = false;
        settings.RenderPercent = 100;
        var fit = GameHost.BuildStart(settings);
        Expect(fit.ArgumentList.Contains("1920") && fit.ArgumentList.Contains("1080"), "16:9 size");
        Expect(fit.ArgumentList.Contains("-AspectFit"), "fit");
        Expect(!fit.ArgumentList.Contains("-Patch"), "no patch on 16:9");
        Expect(!fit.Environment.ContainsKey("SDL_VULKAN_LIBRARY"), "no vulkan library on 16:9");
        Expect(!fit.ArgumentList.Contains("-RenderHeight"), "full height omitted");

        settings.RenderPercent = 98;
        var fitScaled = GameHost.BuildStart(settings);
        Expect(fitScaled.ArgumentList.Contains("-RenderHeight") && fitScaled.ArgumentList.Contains("1058"), "16:9 render height");
        Expect(fitScaled.ArgumentList.Contains("1920") && fitScaled.ArgumentList.Contains("1080"), "16:9 window stays full");

        settings.Width = 2560;
        settings.Height = 1080;
        var wider = GameHost.BuildStart(settings);
        Expect(wider.ArgumentList.Contains("2560") && wider.ArgumentList.Contains("1080"), "2560x1080 window");
        Expect(wider.ArgumentList.Contains("-Patch"), "2560x1080 patch");
        Expect(!wider.ArgumentList.Contains("-AspectFit"), "2560x1080 is wide");
        Expect(wider.Environment["CODEX_UI_OUTPUT_WIDTH"] == "5120", "2560x1080 output");

        settings.Width = 2560;
        settings.Height = 1440;
        settings.RenderPercent = 100;
        var hd = GameHost.BuildStart(settings);
        Expect(hd.ArgumentList.Contains("-AspectFit"), "2560x1440 is 16:9");
        Expect(!hd.ArgumentList.Contains("-Patch"), "no patch on 2560x1440");
        Expect(!hd.Environment.ContainsKey("SDL_VULKAN_LIBRARY"), "no vulkan library on 2560x1440");
    }

    private static void CheckPicture()
    {
        Expect(Picture.Fits(1920, 1080), "1080p fits");
        Expect(Picture.IsSixteenByNine(1920, 1080), "1080p shape");
        Expect(Picture.IsSixteenByNine(2560, 1440), "1440p shape");
        Expect(!Picture.Fits(1920, 1200), "16:10 rejected");
        Expect(!Picture.Fits(1280, 1024), "5:4 rejected");
        Expect(!Picture.Fits(1024, 768), "small rejected");
        Expect(Picture.IsWide(3440, 1440), "3440 is wide");
        Expect(Picture.IsWide(2560, 1080), "2560x1080 is wide");
        Expect(Picture.OutputWidth(3440, 1440) == 5160, "3440 output");
        Expect(Picture.OutputWidth(2560, 1080) == 5120, "2560 output");

        var known = new Dictionary<string, string>
        {
            ["90ae7f"] = "c743048ee31840",
            ["90ac3f"] = "48ba2814000070080000",
            ["90a6ab"] = "48b92814000070080000",
            ["90b217"] = "48b92814000070080000",
            ["250eb48"] = "4006000080070000700d0000000f0000800a0000",
            ["237f264"] = "700d0000a005000028140000700800002814000070080000",
            ["24f9550"] = "700d0000a00500000000000000000000",
            ["90af74"] = "bf28140000",
            ["90b061"] = "bf28140000",
            ["916355"] = "be2814000090",
        };
        var entries = UltrawidePatch.Entries(3440, 1440);
        Expect(entries.Count == known.Count, "ten entries");
        foreach (var entry in entries)
        {
            Expect(entry.Off.Length == entry.On.Length, entry.Offset + " length");
            Expect(known[entry.Offset] == entry.On, entry.Offset);
        }
        var narrow = UltrawidePatch.Entries(2560, 1080);
        Expect(narrow.Single(entry => entry.Offset == "90af74").On == "bf00140000", "2560 scanout");
        Expect(narrow.Single(entry => entry.Offset == "237f264").On.Contains("38040000"), "2560 mode height");
        Expect(ThrowsNarrow(), "generator rejects 16:10");

        var modes = DisplayModes.List();
        Expect(modes.Any(size => size.Width == 1920 && size.Height == 1080), "list keeps 1080p");
        Expect(modes.Any(size => size.Width == 3440 && size.Height == 1440), "list keeps 3440");
        Expect(modes.All(size => Picture.Fits(size.Width, size.Height)), "list is 16:9 or wider");

        var migrated = new LauncherSettings { Ultrawide = true };
        migrated.Normalize();
        Expect(migrated.Width == 3440 && migrated.Height == 1440, "old ultrawide setting");
        migrated = new LauncherSettings { Ultrawide = false };
        migrated.Normalize();
        Expect(migrated.Width == 1920 && migrated.Height == 1080, "old 16:9 setting");
    }

    private static bool ThrowsNarrow()
    {
        try
        {
            UltrawidePatch.Entries(1920, 1200);
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    private static void CheckSaves()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsl-selftest-" + Guid.NewGuid().ToString("N"));
        var kyty = Path.Combine(root, "kyty");
        var profile = Path.Combine(kyty, "_SaveData", GameHost.TitleId, "SAVEDATA0PlayerProfile0");
        var capacity = Path.Combine(kyty, "_SaveData", ".kyty-capacity", GameHost.TitleId);
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(capacity);
        File.WriteAllText(Path.Combine(profile, "USR-DATA"), "player");
        File.WriteAllText(Path.Combine(capacity, "SAVEDATA0PlayerProfile0.blocks"), "12");

        var dest = Path.Combine(root, "out");
        var exportError = SaveArchive.Export(kyty, dest);
        Expect(exportError == null, exportError ?? "export");
        var zip = Directory.GetFiles(dest, "DemonsSouls-Save-*.zip").Single();

        File.WriteAllText(Path.Combine(profile, "USR-DATA"), "changed");
        var restoreError = SaveArchive.Restore(kyty, zip);
        Expect(restoreError == null, restoreError ?? "restore");
        Expect(File.ReadAllText(Path.Combine(profile, "USR-DATA")) == "player", "restored bytes");
        Expect(File.ReadAllText(Path.Combine(capacity, "SAVEDATA0PlayerProfile0.blocks")) == "12", "capacity");
        Expect(Directory.GetDirectories(Path.Combine(kyty, "_SaveData", "_backups")).Length == 1, "backup");
        Expect(File.ReadAllText(Path.Combine(
            Directory.GetDirectories(Path.Combine(kyty, "_SaveData", "_backups")).Single(),
            GameHost.TitleId, "SAVEDATA0PlayerProfile0", "USR-DATA")) == "changed", "backup bytes");

        var empty = Path.Combine(root, "empty.zip");
        File.WriteAllBytes(empty, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var rejected = SaveArchive.Restore(kyty, empty);
        Expect(rejected != null, "rejected empty zip");
        Expect(File.ReadAllText(Path.Combine(profile, "USR-DATA")) == "player", "untouched after reject");

        Directory.Delete(root, recursive: true);
    }

    private static void CheckShaders()
    {
        Expect(ShaderProgress.Languages.Length == 30, "language count");
        Expect(ShaderProgress.Languages[1] == "English (United States)", "english");
        var line = "  00:01:00 shards: 40 done, 3 running, 469 waiting (0 split)";
        Expect(ShaderProgress.TryRead(line, out var running) && running.Percent == 7 && !running.Complete, "shard percent");
        Expect(ShaderProgress.TryRead("done in 01:00:00: cache.bin (12 MB; 0 shards split)", out var done)
            && done.Complete && done.Percent == 100 && done.Label == "Complete", "shaders complete");
        Expect(!ShaderProgress.TryRead("precompile: seeds, 512 shards", out _), "ignore banner");

        var root = Path.Combine(Path.GetTempPath(), "dsl-shaders-" + Guid.NewGuid().ToString("N"));
        var kyty = Path.Combine(root, "kyty");
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "sce_sys"));
        File.WriteAllText(Path.Combine(game, "sce_sys", "param.json"),
            """{"titleId":"PPSA01341","contentVersion":"01.007.000"}""");
        var cache = Path.Combine(kyty, "_PipelineCache", "static");
        Directory.CreateDirectory(cache);
        var shadersOnly = Path.Combine(cache, "PPSA01341_01.007.000.shaders");
        File.WriteAllText(shadersOnly, "inputs");
        Expect(!ShaderProgress.HasCache(kyty, game), "shader inputs are not a finished cache");
        var binaries = Path.Combine(cache, "PPSA01341_01.007.000.binaries");
        File.WriteAllBytes(binaries, new byte[] { 1 });
        Expect(ShaderProgress.HasCache(kyty, game), "existing shader cache");
        Directory.Delete(root, recursive: true);
    }

    private static void CheckCamera()
    {
        Expect(CameraDistance.Distance(0) == 4.5f, "distance 0");
        Expect(CameraDistance.Distance(1) == 5.625f, "distance 1");
        Expect(CameraDistance.Distance(2) == 6.75f, "distance 2");
        Expect(CameraDistance.Distance(3) == 7.875f, "distance 3");
        Expect(CameraDistance.Distance(4) == 9f, "distance 4");
        Expect(CameraDistance.Distance(-1) == 4.5f, "distance floor");
        Expect(CameraDistance.Distance(9) == 9f, "distance ceiling");
        Expect(CameraDistance.Move(0, -1) == 0, "step floor");
        Expect(CameraDistance.Move(4, 1) == 4, "step ceiling");
        Expect(CameraDistance.Move(2, 1) == 3, "step up");
        Expect(CameraDistance.Move(2, -1) == 1, "step down");
        Expect(CameraDistance.StepOf(BitConverter.SingleToInt32Bits(4.5f)) == 0, "live step 0");
        Expect(CameraDistance.StepOf(BitConverter.SingleToInt32Bits(9f)) == 4, "live step 4");
        Expect(CameraDistance.StepOf(BitConverter.SingleToInt32Bits(1f)) == -1, "live step other");
        var settings = new LauncherSettings();
        Expect(settings.CameraStep == 0, "step default");
        Expect(settings.CameraFartherKey == 0x21, "page up");
        Expect(settings.CameraCloserKey == 0x22, "page down");
        Expect(CameraDistance.KeyName(0x21) == "Page Up", "page up name");
        Expect(CameraDistance.KeyName(0x22) == "Page Down", "page down name");
    }

    private static void CheckFixes()
    {
        var stub = LiveFix.MemcpyStub();
        Expect(stub.Length == 186, "narrow length");
        Expect(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stub))
            == "3BA9F996A236C2C577CE85306BB10B38C9C60B5E3C7B1E132161CFE656F1F52E", "tested payload");
        Expect(stub[0] == 0x49 && stub[1] == 0x81 && stub[2] == 0xF8 && stub[3] == 0x70, "length gate");
        Expect(stub.AsSpan().IndexOf(Convert.FromHexString("3d6f9d3f")) >= 0, "preview pair");
        Expect(stub.AsSpan().IndexOf(Convert.FromHexString("35792240")) >= 0, "preview aspect");
        Expect(stub.AsSpan().IndexOf(Convert.FromHexString("ad116e3f")) >= 0, "preview shift");
        Expect(stub.AsSpan().IndexOf(new byte[] { 0x4A, 0x8D, 0x44, 0x02, 0xF4 }) < 0, "no buffer scan");
        Expect(stub.AsSpan(174, 8).SequenceEqual(new byte[8]), "original pointer slot");
        Expect(stub.AsSpan(182, 4).SequenceEqual(new byte[4]), "counter slot");
    }

    private static void Expect(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
    }
}
