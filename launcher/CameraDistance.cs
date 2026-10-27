using System.Runtime.InteropServices;
using System.Windows.Input;

namespace DemonsSoulsLauncher;

// The follow camera reads this one float every frame. Step 0 is the game's
// original distance. Each step adds the same amount, and step 4 is as far
// as it goes.
public static class CameraDistance
{
    const ulong Slot = 0x9027AAF50;
    const float Original = 4.5f;
    const float StepSize = 1.125f;

    public static float Distance(int step) => Original + StepSize * Math.Clamp(step, 0, 4);

    public static int Move(int step, int direction) => Math.Clamp(step + direction, 0, 4);

    public static int StepOf(int bits)
    {
        for (var step = 0; step <= 4; step++)
        {
            if (bits == BitConverter.SingleToInt32Bits(Distance(step)))
                return step;
        }
        return -1;
    }

    public static bool TryReadStep(int pid, out int step)
    {
        step = 0;
        if (pid == 0)
            return false;
        var process = OpenProcess(0x1F0FFF, false, pid);
        if (process == 0)
            return false;
        try
        {
            if (!TryRead(process, out var bits))
                return false;
            step = StepOf(bits);
            return step >= 0;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static string KeyName(int virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return key switch
        {
            Key.Prior => "Page Up",
            Key.Next => "Page Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Return => "Enter",
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.None => "Unknown",
            _ => key.ToString(),
        };
    }

    public static bool IsDown(int virtualKey) =>
        virtualKey != 0 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static bool IsForeground(int pid)
    {
        var window = GetForegroundWindow();
        if (window == 0 || pid == 0)
            return false;
        GetWindowThreadProcessId(window, out var foreground);
        return foreground == pid;
    }

    public static bool TryApply(int pid, int step)
    {
        if (pid == 0)
            return false;
        var process = OpenProcess(0x1F0FFF, false, pid);
        if (process == 0)
            return false;
        try
        {
            var target = Distance(step);
            var targetBits = BitConverter.SingleToInt32Bits(target);
            if (!TryRead(process, out var current))
                return false;
            if (current == targetBits)
                return true;
            if (!IsStepValue(current))
                return false;

            var page = Slot & ~0xFFFUL;
            uint old = 0;
            if (!VirtualProtectEx(process, page, 0x1000, 0x04, ref old))
                return false;
            var wrote = Write(process, Slot, BitConverter.GetBytes(target));
            uint restored = 0;
            VirtualProtectEx(process, page, 0x1000, old, ref restored);
            return wrote && TryRead(process, out var after) && after == targetBits;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    static bool IsStepValue(int bits) => StepOf(bits) >= 0;

    static bool TryRead(IntPtr process, out int bits)
    {
        var buffer = new byte[4];
        if (!ReadProcessMemory(process, Slot, buffer, buffer.Length, out var read) || read != 4)
        {
            bits = 0;
            return false;
        }
        bits = BitConverter.ToInt32(buffer, 0);
        return true;
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
    static extern bool VirtualProtectEx(IntPtr process, ulong address, nuint size, uint protect, ref uint old);

    [DllImport("user32")]
    static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32")]
    static extern nint GetForegroundWindow();

    [DllImport("user32")]
    static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
