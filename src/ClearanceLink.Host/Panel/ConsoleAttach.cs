using System.Runtime.InteropServices;

namespace ClearanceLink.Host.Panel;

/// <summary>
/// ClearanceLink.exe is a Windows GUI program (so double-clicking opens only the window). When it is run with a command
/// from a command prompt, this attaches it to that prompt's console so the command's output shows there.
/// </summary>
internal static partial class ConsoleAttach
{
    private const int ParentProcess = -1;
    private const int StdOutput = -11;

    public static void ToParent()
    {
        // Output already redirected (to a file or a pipe): leave it there.
        if (OperatingSystem.IsWindows() && GetStdHandle(StdOutput) is 0 or -1)
        {
            AttachConsole(ParentProcess);
        }
    }

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}
