using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VoiceBridge;

// Installer lifecycle API: requests normal shutdown of one verified VoiceBridge
// process. Never uses RmForceShutdown, process termination, or window automation.
public static class GracefulAppShutdown
{
    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess
    {
        public uint ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME Started;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint fileCount, IntPtr files,
        uint processCount, [In] UniqueProcess[] processes, uint serviceCount, IntPtr services);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmShutdown(uint session, uint flags, IntPtr callback);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);

    public static int Request(int processId, string expectedExecutable)
    {
        expectedExecutable = Path.GetFullPath(expectedExecutable);
        if (!string.Equals(Path.GetFileName(expectedExecutable), "VoiceBridge.Next.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only VoiceBridge lifecycle requests are supported.");
        using var process = Process.GetProcessById(processId);
        using var caller = Process.GetCurrentProcess();
        if (process.Id == caller.Id || process.SessionId != caller.SessionId ||
            !string.Equals(process.MainModule?.FileName, expectedExecutable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The target is not the expected same-session VoiceBridge process.");
        var started = process.StartTime.ToFileTimeUtc();
        var target = new UniqueProcess { ProcessId = (uint)processId,
            Started = new() { dwLowDateTime = (int)started, dwHighDateTime = (int)(started >> 32) } };
        var result = RmStartSession(out var session, 0, new StringBuilder(33));
        if (result != 0) return result;
        try
        {
            result = RmRegisterResources(session, 0, IntPtr.Zero, 1, [target], 0, IntPtr.Zero);
            if (result != 0) return result;
            return RmShutdown(session, 0, IntPtr.Zero);
        }
        finally { RmEndSession(session); }
    }
}
