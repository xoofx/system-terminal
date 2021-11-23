using Windows.Win32.Foundation;
using Windows.Win32.System.Console;
using static Windows.Win32.Constants;
using static Windows.Win32.WindowsPInvoke;

namespace System.Drivers.Windows;

sealed class WindowsTerminalDriver : TerminalDriver
{
    public static WindowsTerminalDriver Instance { get; } = new();

    public override WindowsTerminalReader StdIn { get; }

    public override WindowsTerminalWriter StdOut { get; }

    public override WindowsTerminalWriter StdError { get; }

    readonly ManualResetEventSlim _event = new();

    WindowsTerminalDriver()
    {
        StdIn = new(this, GetStdHandle_SafeHandle(STD_HANDLE.STD_INPUT_HANDLE));
        StdOut = new(GetStdHandle_SafeHandle(STD_HANDLE.STD_OUTPUT_HANDLE), "output");
        StdError = new(GetStdHandle_SafeHandle(STD_HANDLE.STD_ERROR_HANDLE), "error");

        // Input needs to be UTF-16, but we make it appear as if it is UTF-8 to users of the library. See the comments
        // in WindowsTerminalReader for the gory details.
        _ = SetConsoleCP((uint)Encoding.Unicode.CodePage);
        _ = SetConsoleOutputCP((uint)Encoding.UTF8.CodePage);

        var inMode =
            CONSOLE_MODE.ENABLE_PROCESSED_INPUT |
            CONSOLE_MODE.ENABLE_LINE_INPUT |
            CONSOLE_MODE.ENABLE_ECHO_INPUT |
            CONSOLE_MODE.ENABLE_INSERT_MODE |
            CONSOLE_MODE.ENABLE_EXTENDED_FLAGS |
            CONSOLE_MODE.ENABLE_VIRTUAL_TERMINAL_INPUT;
        var outMode =
            CONSOLE_MODE.ENABLE_PROCESSED_OUTPUT |
            CONSOLE_MODE.ENABLE_WRAP_AT_EOL_OUTPUT |
            CONSOLE_MODE.ENABLE_VIRTUAL_TERMINAL_PROCESSING;

        // Set modes on all handles in case one of them has been redirected. These calls can fail if there is no console
        // attached, but that is OK.
        _ = StdIn.AddMode(inMode);
        _ = StdOut.AddMode(outMode) || StdError.AddMode(outMode);

        // We need to grab the window size at least once upfront so that adding an event handler to the Resize event
        // does not immediately cause the event to be fired.
        RefreshSize();

        // Windows currently has no SIGWINCH equivalent, so we have to poll for size changes.
        _ = TerminalUtility.StartThread("Terminal Resize Listener", () =>
        {
            while (true)
            {
                _event.Wait();

                RefreshSize();

                // TODO: Do we need to make this configurable?
                Thread.Sleep(100);
            }
        });
    }

    protected override TerminalSize? GetSize()
    {
        static CONSOLE_SCREEN_BUFFER_INFO? GetInfo(SafeHandle handle)
        {
            return GetConsoleScreenBufferInfo(handle, out var info) ? info : null;
        }

        // Try both handles in case only one of them has been redirected.
        return (GetInfo(StdOut.Handle) ?? GetInfo(StdError.Handle)) is CONSOLE_SCREEN_BUFFER_INFO i ?
            new(i.srWindow.Right - i.srWindow.Left + 1, i.srWindow.Bottom - i.srWindow.Top + 1) : null;
    }

    protected override void ToggleResizeEvent(bool enable)
    {
        if (enable)
            _event.Set();
        else
            _event.Reset();
    }

    public override void GenerateBreakSignal(TerminalBreakSignal signal)
    {
        _ = GenerateConsoleCtrlEvent(
            signal switch
            {
                TerminalBreakSignal.Interrupt => CTRL_C_EVENT,
                TerminalBreakSignal.Quit => CTRL_BREAK_EVENT,
                _ => throw new ArgumentOutOfRangeException(nameof(signal)),
            },
            0);
    }

    public override void GenerateSuspendSignal()
    {
        // TODO: Windows does not have an equivalent of SIGTSTP. Should we throw?
    }

    protected override void SetRawMode(bool raw)
    {
        if (!StdIn.IsValid || (!StdOut.IsValid && !StdError.IsValid))
            throw new TerminalException("There is no terminal attached.");

        var inMode =
            CONSOLE_MODE.ENABLE_PROCESSED_INPUT |
            CONSOLE_MODE.ENABLE_LINE_INPUT |
            CONSOLE_MODE.ENABLE_ECHO_INPUT;
        var outMode =
            CONSOLE_MODE.DISABLE_NEWLINE_AUTO_RETURN;

        if (!(raw ? StdIn.RemoveMode(inMode) && (StdOut.RemoveMode(outMode) || StdError.RemoveMode(outMode)) :
            StdIn.AddMode(inMode) && (StdOut.AddMode(outMode) || StdError.AddMode(outMode))))
            throw new TerminalException(
                $"Could not change raw mode setting: {(WIN32_ERROR)Marshal.GetLastSystemError()}");

        if (!FlushConsoleInputBuffer(StdIn.Handle))
            throw new TerminalException(
                $"Could not flush input buffer: {(WIN32_ERROR)Marshal.GetLastSystemError()}");
    }
}
