using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TruckHub;

/// <summary>
/// Inicia um processo sem o cursor "seta + ampulheta" (feedback de inicialização do Windows).
/// Process.Start não permite STARTF_FORCEOFFFEEDBACK; sem ele, cada início do conector mostrava
/// o cursor de carregando no app inteiro.
/// </summary>
internal static class NativeLauncher
{
    private const uint StartfUseShowWindow = 0x1, StartfForceOffFeedback = 0x80, CreateNoWindow = 0x08000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
        public uint dwFlags; public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
        uint flags, IntPtr env, string? dir, ref StartupInfo si, out ProcessInformation pi);

    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    /// <summary>Devolve o PID, ou 0 se não conseguiu (o chamador cai no Process.Start normal).</summary>
    public static int StartHidden(string exe, string workingDirectory)
    {
        try
        {
            var si = new StartupInfo { dwFlags = StartfUseShowWindow | StartfForceOffFeedback, wShowWindow = 0 };
            si.cb = Marshal.SizeOf<StartupInfo>();
            var cmd = new StringBuilder("\"" + exe + "\"");
            if (!CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, CreateNoWindow, IntPtr.Zero, workingDirectory, ref si, out var pi))
                return 0;
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }
        catch { return 0; }
    }
}
