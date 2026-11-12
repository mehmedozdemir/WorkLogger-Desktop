namespace WorkLogger.Infrastructure.Win32;

/// <summary>Ön plandaki pencere bilgisi. Pencere yoksa (kilit ekranı, masaüstü geçişi) App null olur.</summary>
public readonly record struct ForegroundInfo(string? App, string Title);

/// <summary>Win32 üzerinden ön plan penceresini ve kullanıcı boşta süresini okur.</summary>
public sealed class WindowProbe
{
    private readonly Dictionary<uint, (string Name, long Expires)> _processNames = [];

    public unsafe ForegroundInfo Foreground()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
            return new ForegroundInfo(null, "");

        var title = "";
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length > 0)
        {
            var size = Math.Min(length + 1, 2048);
            var buffer = size <= 512 ? stackalloc char[size] : new char[size];
            fixed (char* p = buffer)
            {
                var copied = NativeMethods.GetWindowText(hwnd, p, size);
                title = new string(buffer[..Math.Max(0, copied)]).Trim();
            }
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var app = pid == 0 ? null : ProcessName(pid);
        return new ForegroundInfo(app, title);
    }

    /// <summary>Son klavye/fare girdisinden bu yana geçen süre.</summary>
    public static TimeSpan IdleTime()
    {
        var info = new NativeMethods.LastInputInfo { CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LastInputInfo>() };
        if (!NativeMethods.GetLastInputInfo(ref info))
            return TimeSpan.Zero;
        // GetTickCount 32 bit sarar; işaretsiz fark sarmayı doğru ele alır.
        var elapsed = unchecked((uint)Environment.TickCount - info.DwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    private unsafe string? ProcessName(uint pid)
    {
        var now = Environment.TickCount64;
        if (_processNames.TryGetValue(pid, out var cached) && cached.Expires > now)
            return cached.Name;

        string? name = null;
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle != 0)
        {
            try
            {
                const int capacity = 1024;
                var buffer = stackalloc char[capacity];
                var size = (uint)capacity;
                if (NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size))
                    name = Path.GetFileName(new string(buffer, 0, (int)size));
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        name ??= "(bilinmiyor)";
        if (_processNames.Count > 512)
            _processNames.Clear();
        _processNames[pid] = (name, now + 60_000);
        return name;
    }
}
