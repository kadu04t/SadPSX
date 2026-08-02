using System.Runtime.InteropServices;
using System.Text;

namespace SadPSX.Frontend.Diagnostics;

internal static class DiagnosticTerminal
{
    private const uint AttachParentProcess = uint.MaxValue;
    private static readonly object Sync = new();
    private static bool _initialized;

    public static void EnsureVisible()
    {
        if (_initialized)
            return;

        lock (Sync)
        {
            if (_initialized)
                return;

            if (OperatingSystem.IsWindows() &&
                GetConsoleWindow() == nint.Zero &&
                !AttachConsole(AttachParentProcess) &&
                !AllocConsole())
            {
                return;
            }

            ReconnectOutput();
            _initialized = true;
            Console.WriteLine("SadPSX Diagnostic Console");
            Console.WriteLine("F1-F8 diagnostics are active.");
            Console.WriteLine();
        }
    }

    private static void ReconnectOutput()
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var output = new StreamWriter(
            Console.OpenStandardOutput(),
            encoding)
        {
            AutoFlush = true,
        };
        var error = new StreamWriter(
            Console.OpenStandardError(),
            encoding)
        {
            AutoFlush = true,
        };
        Console.SetOut(TextWriter.Synchronized(output));
        Console.SetError(TextWriter.Synchronized(error));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();
}
