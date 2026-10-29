using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DemonsSoulsLauncher;

// The item preview is drawn into the world image. Centering the interface
// leaves that model beside its panel, so this watches one memcpy shape and
// rewrites the two camera values after the copy.
public static class LiveFix
{
    const int IatRva = 0x12F1BA0;

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
            var until = DateTime.UtcNow.AddMinutes(3);
            ulong counter = 0;
            while (DateTime.UtcNow < until && IsRunning(pid))
            {
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
                    break;
                Thread.Sleep(250);
            }
            if (counter == 0)
            {
                Log("preview hook not installed");
                return;
            }
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
        var started = DateTime.UtcNow;
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
        }
    }

    static ulong InstallMemcpy(IntPtr process)
    {
        var modules = ModuleBases(process);
        if (modules.Count == 0)
            return 0;
        var slot = modules[0] + (ulong)IatRva;
        var current = ReadU64(process, slot);
        if (current == 0 || !InModule(process, current, "VCRUNTIME140"))
            return 0;
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

    static byte[] BuildMemcpyStub() => Convert.FromHexString(
        "4981f8700c00000f859b00000049ba00000000020000004c39d10f828800000049ba00000010020000004c39d10f837500000081bacc0000003d6f9d3f0f856500000081bad4000000357922400f855500000081ba4c0100003d6f9d3f0f854500000081ba54010000357922400f85350000004883ec3848894c2420ff152c0000004c8b54242041c782cc000000ad116e3f41c7824c010000ad116e3fff05130000004883c438c3ff2500000000000000000000000000000000");

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

    static bool Write(IntPtr process, ulong address, byte[] data) =>
        WriteProcessMemory(process, address, data, data.Length, out var written) && written == data.Length;

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
