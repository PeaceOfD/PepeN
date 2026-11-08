namespace ServiceLib.Tests.Common;

/// <summary>
/// PattN: the program a Windows process runs, read from the process right after its start (ProcUtils.WindowsProgramOf),
/// which Process.MainModule cannot always do in the process's first milliseconds.
/// </summary>
public class ProcUtilsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pattn-proc-").FullName;

    public void Dispose()
    {
        // Windows may hold a program's file a moment after its process ends.
        for (var tries = 0; Directory.Exists(_root); tries++)
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && tries < 50)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Test]
    public async Task AProgramIsReadFromItsProcessRightAfterItStarts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var program = Path.Combine(_root, "p-xray-p.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), program);

        // Over and over, as the first milliseconds of a process are when MainModule fails.
        for (var run = 0; run < 10; run++)
        {
            var info = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-n", "60", "127.0.0.1" })
            {
                info.ArgumentList.Add(argument);
            }
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the test process.");
            try
            {
                var read = ProcUtils.WindowsProgramOf(process.Id);

                await string.Equals(read, program, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            }
            finally
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Test]
    public async Task NoProcessHasNoProgram()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await ProcUtils.WindowsProgramOf(int.MaxValue).Should().BeNull();
    }
}
