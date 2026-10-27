using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DemonsSoulsLauncher;

// The item preview number is not in the game file. It is copied into the
// graphics buffer, so this watches memcpy and rewrites the exact pair.
// The save-and-quit words are right-aligned to the row's extra width.
// Ultrawide leaves that extra near 1001.6, so the words start at the
// gold bar's right tip. That extra also places the pulse, so the watcher
// leaves it alone. The size function stays as the patch left it: writing
// the button from inside that function makes layout call itself until
// the stack overflows.
public static class LiveFix
{
    const ulong Hook = 0x900E0A2BB;
    const ulong Vtable = 0x9025D8430;
    const ulong MarkCard = 0x908006628;
    const ulong MarkWrap = 0x908006630;
    const ulong MarkRow = 0x908006638;
    const ulong MarkGold = 0x908006640;
    const ulong MarkHits = 0x908006648;
    const int IatRva = 0x12F1BA0;
    const ulong HostLo = 0x10000000000UL;
    const ulong HostHi = 0x20000000000UL;
    const ulong GpuLo = 0x200000000UL;
    const ulong GpuHi = 0x210000000UL;

    static readonly byte[] HookOn = Convert.FromHexString("e940b91f07909090");
    static readonly byte[] HookOff = Convert.FromHexString("c5f858c14883c408");
    static readonly byte[] VtableBytes = BitConverter.GetBytes(Vtable);
    static readonly byte[] Pair = Convert.FromHexString("3d6f9d3f");
    static readonly byte[] Aspect = Convert.FromHexString("35792240");
    static readonly byte[] Shifted = Convert.FromHexString("ad116e3f");

    public static bool StartWatcher(string knownProcessIds)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;
        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                ArgumentList = { "--watch", knownProcessIds },
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (started == null)
            {
                Log("watch start failed");
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log("watch start failed " + ex.Message);
            return false;
        }
    }

    public static void Watch(string knownProcessIds)
    {
        Log("watch " + knownProcessIds);
        using var gate = new Mutex(false, @"Local\DemonsSoulsLauncher.Watch");
        if (!gate.WaitOne(0))
        {
            Log("watch already running");
            return;
        }
        try
        {
            var known = knownProcessIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(text => int.TryParse(text, out var id) ? id : 0)
                .Where(id => id != 0)
                .ToHashSet();
            var pid = WaitForNewEmulator(known, TimeSpan.FromMinutes(5));
            if (pid == 0)
                Log("no new game");
            else
                Attach(pid);
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }

    public static byte[] MemcpyStub() => BuildMemcpyStub();

    static void Attach(int pid)
    {
        Log("attach " + pid);
        var process = Open(pid);
        if (process == IntPtr.Zero)
        {
            Log("open failed " + Marshal.GetLastWin32Error());
            return;
        }
        try
        {
            if (!WaitForPatch(process, TimeSpan.FromMinutes(3)))
            {
                Log("patch hook not seen");
                return;
            }
            ulong counter = 0;
            if (!Suspend(process))
            {
                Log("suspend failed");
                return;
            }
            try
            {
                counter = InstallMemcpy(process);
            }
            finally
            {
                Resume(process);
            }
            if (counter != 0)
                WatchPreview(process, pid, counter);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    static void WatchPreview(IntPtr process, int pid, ulong counter)
    {
        var last = uint.MaxValue;
        var loggedZero = false;
        var loggedMiss = false;
        var started = DateTime.UtcNow;
        var seen = new SaveWidgets();
        var attempts = 0;
        var marks = 0u;
        while (IsRunning(pid))
        {
            Thread.Sleep(2000);
            var now = ReadU32(process, counter);
            if (now != last)
            {
                Log("preview patches " + now);
                last = now;
                loggedZero = now != 0;
            }
            else if (!loggedZero && now == 0 && DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
            {
                Log("preview patches 0");
                loggedZero = true;
            }
            if (attempts >= 3)
                continue;
            var hits = ReadU32(process, MarkHits);
            if (hits != marks)
            {
                Log("save marks " + hits);
                marks = hits;
            }
            if (seen.Card != 0)
            {
                attempts++;
                if (!StillBaked(process, seen))
                    Log("save shape changed before the write");
                else if (RepairSave(process, seen))
                    Log("save button corrected");
                seen = new SaveWidgets();
                continue;
            }
            var found = new SaveWidgets
            {
                Card = ReadU64(process, MarkCard),
                Wrap = ReadU64(process, MarkWrap),
                Row = ReadU64(process, MarkRow),
                Gold = ReadU64(process, MarkGold),
            };
            if (found.Card == 0 || found.Wrap == 0 || found.Row == 0 || found.Gold == 0)
            {
                if (!loggedMiss && DateTime.UtcNow - started > TimeSpan.FromSeconds(20) && hits == 0)
                {
                    Log("save button not seen");
                    loggedMiss = true;
                }
                continue;
            }
            if (!StillBaked(process, found))
                continue;
            loggedMiss = false;
            Log($"save button seen {found.Card:X}");
            seen = found;
        }
    }

    readonly struct SaveWidgets
    {
        public ulong Card { get; init; }
        public ulong Wrap { get; init; }
        public ulong Row { get; init; }
        public ulong Gold { get; init; }
    }

    static bool RepairSave(IntPtr process, SaveWidgets widgets)
    {
        if (!Suspend(process))
        {
            Log("save suspend failed");
            return false;
        }
        var suspended = true;
        var hookOff = false;
        try
        {
            if (!HookOn.AsSpan().SequenceEqual(Read(process, Hook, HookOn.Length)))
            {
                Log("save hook is not the patch");
                return false;
            }
            if (!StillBaked(process, widgets))
            {
                Log("save shape changed before the write");
                return false;
            }
            if (!Write(process, Hook, HookOff))
            {
                Log("save hook off failed");
                return false;
            }
            hookOff = true;
            WriteFloat(process, widgets.Card + 0x2E4, 10f);
            WriteFloat(process, widgets.Card + 0x2FC, 10f);
            WriteFloat(process, widgets.Card + 0x304, 1021.6f);
            WriteFloat(process, widgets.Card + 0x2EC, 61.6f);
            WriteFloat(process, widgets.Wrap + 0x304, 1021.6f);
            WriteFloat(process, widgets.Wrap + 0x2EC, 921.6f);
            WriteFloat(process, widgets.Row + 0x304, 1021.6f);
            WriteFloat(process, widgets.Row + 0x2EC, 921.6f);
            WriteFloat(process, widgets.Gold + 0x2E4, 292f);
            WriteFloat(process, widgets.Gold + 0x2FC, 292f);
            if (Math.Abs(ReadFloat(process, widgets.Card + 0x304) - 1021.6f) > 1f)
            {
                Log("save write did not land");
                return false;
            }
            Resume(process);
            suspended = false;
            Thread.Sleep(100);
            if (!Suspend(process))
            {
                Log("save second suspend failed");
                return false;
            }
            suspended = true;
            var width = ReadFloat(process, widgets.Card + 0x304);
            var goldX = ReadFloat(process, widgets.Gold + 0x2E4);
            if (Math.Abs(width - 1021.6f) > 1f || Math.Abs(goldX - 292f) > 1f)
            {
                Log($"save did not stick width {width:0.0} gold {goldX:0.0}");
                return false;
            }
            WriteFloat(process, widgets.Card + 0x2EC, 141.6f);
            WriteFloat(process, widgets.Wrap + 0x2EC, 971.6f);
            return true;
        }
        finally
        {
            if (hookOff)
                Write(process, Hook, HookOn);
            if (suspended)
                Resume(process);
        }
    }

    static bool StillBaked(IntPtr process, SaveWidgets widgets)
    {
        if (ReadU64(process, widgets.Card) != Vtable || ReadU64(process, widgets.Gold) != Vtable)
            return false;
        if (ReadU64(process, widgets.Gold + 0x2C8) != widgets.Row)
            return false;
        var cardX = ReadFloat(process, widgets.Card + 0x2E4);
        var cardWidth = ReadFloat(process, widgets.Card + 0x304);
        var goldX = ReadFloat(process, widgets.Gold + 0x2E4);
        return cardX is < -15 and > -45
            && cardWidth is > 1090 and < 1115
            && goldX is > 310 and < 360;
    }

    static ulong InstallMemcpy(IntPtr process)
    {
        var modules = ModuleBases(process);
        if (modules.Count == 0)
        {
            Log("no modules");
            return 0;
        }
        var slot = modules[0] + (ulong)IatRva;
        var current = ReadU64(process, slot);
        if (current == 0 || !InModule(process, current, "VCRUNTIME140"))
        {
            Log($"memcpy slot {current:X} is not VCRUNTIME140");
            return 0;
        }
        var code = BuildMemcpyStub();
        var memory = VirtualAllocEx(process, 0, 0x1000, 0x3000, 0x40);
        if (memory == 0)
        {
            Log("preview alloc failed " + Marshal.GetLastWin32Error());
            return 0;
        }
        BitConverter.GetBytes(current).CopyTo(code, code.Length - 12);
        if (!Write(process, memory, code))
        {
            Log("preview stub write failed");
            return 0;
        }
        var old = 0u;
        if (!VirtualProtectEx(process, slot, 8, 0x04, ref old))
        {
            Log("preview protect failed " + Marshal.GetLastWin32Error());
            return 0;
        }
        var wrote = Write(process, slot, BitConverter.GetBytes(memory));
        VirtualProtectEx(process, slot, 8, old, ref old);
        if (!wrote)
        {
            Log("preview slot write failed");
            return 0;
        }
        Log($"preview hook installed at {memory:X}");
        return memory + (ulong)(code.Length - 4);
    }

    static byte[] BuildMemcpyStub()
    {
        var emit = new Emit();
        emit.Raw(0x49, 0x83, 0xF8, 0x18);
        emit.Jcc(0x82, "go");
        emit.Raw(0x49, 0x81, 0xF8, 0x00, 0x00, 0x01, 0x00);
        emit.Jcc(0x87, "go");
        emit.Raw(0x50);
        emit.Raw(0x48, 0xB8);
        emit.Raw(BitConverter.GetBytes(HostLo));
        emit.Raw(0x48, 0x39, 0xC2);
        emit.Jcc(0x82, "gpu_src");
        emit.Raw(0x48, 0xB8);
        emit.Raw(BitConverter.GetBytes(HostHi));
        emit.Raw(0x48, 0x39, 0xC2);
        emit.Jcc(0x82, "scan");
        emit.Label("gpu_src");
        emit.Raw(0x48, 0xB8);
        emit.Raw(BitConverter.GetBytes(GpuLo));
        emit.Raw(0x48, 0x39, 0xC2);
        emit.Jcc(0x82, "gpu_dst");
        emit.Raw(0x48, 0xB8);
        emit.Raw(BitConverter.GetBytes(GpuHi));
        emit.Raw(0x48, 0x39, 0xC2);
        emit.Jcc(0x82, "scan");
        emit.Label("gpu_dst");
        emit.Raw(0x48, 0xB8);
        emit.Raw(BitConverter.GetBytes(GpuLo));
        emit.Raw(0x48, 0x39, 0xC1);
        emit.Jcc(0x82, "pop");
        emit.Raw(0x48, 0xB8);
        emit.Raw(BitConverter.GetBytes(GpuHi));
        emit.Raw(0x48, 0x39, 0xC1);
        emit.Jcc(0x83, "pop");
        emit.Label("scan");
        emit.Raw(0x41, 0x52);
        emit.Raw(0x4C, 0x8B, 0xD2);
        emit.Raw(0x4A, 0x8D, 0x44, 0x02, 0xF4);
        emit.Label("loop");
        emit.Raw(0x4C, 0x3B, 0xD0);
        emit.Jcc(0x87, "done");
        emit.Raw(0x41, 0x81, 0x3A);
        emit.Raw(Pair);
        emit.Jcc(0x85, "step");
        emit.Raw(0x41, 0x81, 0x7A, 0x08);
        emit.Raw(Aspect);
        emit.Jcc(0x85, "step");
        emit.Raw(0x41, 0xC7, 0x02);
        emit.Raw(Shifted);
        emit.Raw(0xFF, 0x05);
        emit.Disp("hits");
        emit.Label("step");
        emit.Raw(0x49, 0x83, 0xC2, 0x04);
        emit.Jmp("loop");
        emit.Label("done");
        emit.Raw(0x41, 0x5A);
        emit.Label("pop");
        emit.Raw(0x58);
        emit.Label("go");
        emit.Raw(0xFF, 0x25, 0x00, 0x00, 0x00, 0x00);
        emit.Raw(new byte[8]);
        emit.Label("hits");
        emit.Raw(new byte[4]);
        emit.Bind();
        return emit.Code.ToArray();
    }

    static int WaitForNewEmulator(HashSet<int> known, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            foreach (var process in Process.GetProcessesByName("kyty_emulator"))
            {
                if (!known.Contains(process.Id))
                    return process.Id;
            }
            Thread.Sleep(250);
        }
        return 0;
    }

    static bool WaitForPatch(IntPtr process, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (HookOn.SequenceEqual(Read(process, Hook, HookOn.Length)))
                return true;
            Thread.Sleep(250);
        }
        return false;
    }

    static bool IsRunning(int pid)
    {
        try
        {
            return !Process.GetProcessById(pid).HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static List<ulong> ModuleBases(IntPtr process)
    {
        var handles = new IntPtr[256];
        var size = (uint)(IntPtr.Size * handles.Length);
        if (!EnumProcessModules(process, handles, size, out var needed) || needed < (uint)IntPtr.Size)
            return new List<ulong>();
        var count = (int)(needed / (uint)IntPtr.Size);
        return handles.Take(count).Select(handle => (ulong)handle.ToInt64()).ToList();
    }

    static bool InModule(IntPtr process, ulong address, string fileName)
    {
        foreach (var module in ModuleBases(process))
        {
            var name = new StringBuilder(260);
            if (GetModuleFileNameEx(process, (IntPtr)module, name, name.Capacity) == 0)
                continue;
            if (!name.ToString().EndsWith(fileName + ".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!GetModuleInformation(process, (IntPtr)module, out var info, (uint)Marshal.SizeOf<ModuleInfo>()))
                continue;
            var start = (ulong)info.Base.ToInt64();
            return address >= start && address < start + info.Size;
        }
        return false;
    }

    static void Log(string line)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;
            var directory = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(directory))
                return;
            var path = Path.Combine(directory, "launcher-fix.log");
            File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss ") + line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static bool Suspend(IntPtr process) => NtSuspendProcess(process) == 0;

    static void Resume(IntPtr process) => NtResumeProcess(process);

    static IntPtr Open(int pid) => OpenProcess(0x1F0FFF, false, pid);

    static byte[] Read(IntPtr process, ulong address, int size)
    {
        var buffer = new byte[size];
        return ReadProcessMemory(process, address, buffer, size, out var read) && read == size ? buffer : Array.Empty<byte>();
    }

    static ulong ReadU64(IntPtr process, ulong address)
    {
        var data = Read(process, address, 8);
        return data.Length == 8 ? BitConverter.ToUInt64(data, 0) : 0;
    }

    static uint ReadU32(IntPtr process, ulong address)
    {
        var data = Read(process, address, 4);
        return data.Length == 4 ? BitConverter.ToUInt32(data, 0) : 0;
    }

    static float ReadFloat(IntPtr process, ulong address)
    {
        var data = Read(process, address, 4);
        return data.Length == 4 ? BitConverter.ToSingle(data, 0) : float.NaN;
    }

    static void WriteFloat(IntPtr process, ulong address, float value) =>
        Write(process, address, BitConverter.GetBytes(value));

    static bool Write(IntPtr process, ulong address, byte[] data) =>
        WriteProcessMemory(process, address, data, data.Length, out var written) && written == data.Length;

    sealed class Emit
    {
        public List<byte> Code { get; } = new();
        readonly List<(int At, string Name)> _fix = new();
        readonly Dictionary<string, int> _labels = new();

        public void Raw(params byte[] bytes) => Code.AddRange(bytes);

        public void Label(string name) => _labels[name] = Code.Count;

        public void Jcc(byte op, string name)
        {
            Code.Add(0x0F);
            Code.Add(op);
            Disp(name);
        }

        public void Jmp(string name)
        {
            Code.Add(0xE9);
            Disp(name);
        }

        public void Disp(string name)
        {
            _fix.Add((Code.Count, name));
            Code.AddRange(new byte[4]);
        }

        public void Bind()
        {
            foreach (var (at, name) in _fix)
            {
                var rel = _labels[name] - (at + 4);
                var bytes = BitConverter.GetBytes(rel);
                for (var i = 0; i < 4; i++)
                    Code[at + i] = bytes[i];
            }
        }
    }

    [DllImport("kernel32", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool ReadProcessMemory(IntPtr process, ulong address, byte[] buffer, int size, out int read);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool WriteProcessMemory(IntPtr process, ulong address, byte[] buffer, int size, out int written);

    [DllImport("kernel32", SetLastError = true)]
    static extern ulong VirtualAllocEx(IntPtr process, ulong address, nuint size, uint type, uint protect);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool VirtualProtectEx(IntPtr process, ulong address, nuint size, uint protect, ref uint old);

    [DllImport("psapi", SetLastError = true)]
    static extern bool EnumProcessModules(IntPtr process, IntPtr[] modules, uint size, out uint needed);

    [DllImport("psapi", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetModuleFileNameEx(IntPtr process, IntPtr module, StringBuilder name, int size);

    [DllImport("psapi", SetLastError = true)]
    static extern bool GetModuleInformation(IntPtr process, IntPtr module, out ModuleInfo info, uint size);

    [StructLayout(LayoutKind.Sequential)]
    struct ModuleInfo
    {
        public IntPtr Base;
        public uint Size;
        public IntPtr Entry;
    }

    [DllImport("ntdll")]
    static extern int NtSuspendProcess(IntPtr process);

    [DllImport("ntdll")]
    static extern int NtResumeProcess(IntPtr process);
}
