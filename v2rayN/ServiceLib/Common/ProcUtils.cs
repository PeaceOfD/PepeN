namespace ServiceLib.Common;

public static class ProcUtils
{
    private static readonly string _tag = "ProcUtils";

    public static void ProcessStart(string? fileName, string arguments = "")
    {
        _ = ProcessStart(fileName, arguments, null);
    }

    public static int? ProcessStart(string? fileName, string arguments, string? dir)
    {
        if (fileName.IsNullOrEmpty())
        {
            return null;
        }
        try
        {
            if (fileName.Contains(' '))
            {
                fileName = fileName.AppendQuotes();
            }
            if (arguments.Contains(' '))
            {
                arguments = arguments.AppendQuotes();
            }

            Process proc = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = dir ?? string.Empty
                }
            };
            _ = proc.Start();
            return dir is null ? null : proc.Id;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return null;
    }

    public static bool RebootAsAdmin(bool blAdmin = true)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                UseShellExecute = true,
                Arguments = Global.RebootAs,
                WorkingDirectory = Utils.StartupPath(),
                FileName = Utils.GetExePath().AppendQuotes(),
                Verb = blAdmin ? "runas" : null,
            };
            return Process.Start(startInfo) != null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
    }

    /// <summary>
    /// PattN: the program a Windows process runs, as the process itself names it (QueryFullProcessImageName): from the
    /// moment the process exists, and with only the right to query limited information. Process.MainModule reads the list
    /// of modules the process has loaded, which Windows cannot give for a few milliseconds after the process starts
    /// (ERROR_PARTIAL_COPY), so a process looked at right after its start was missed. Null when it is gone or cannot be read.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? WindowsProgramOf(int processId)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return null;
        }
        var buffer = new char[32768];
        var length = buffer.Length;
        return QueryFullProcessImageName(handle, 0, buffer, ref length) ? new string(buffer, 0, length) : null;
    }

    private const int ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(Microsoft.Win32.SafeHandles.SafeProcessHandle process, int flags, [Out] char[] exeName, ref int size);
}
