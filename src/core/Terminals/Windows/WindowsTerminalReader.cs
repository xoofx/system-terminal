using Windows.Win32.Foundation;
using static Windows.Win32.WindowsPInvoke;

namespace System.Terminals.Windows;

sealed class WindowsTerminalReader : NativeTerminalReader<WindowsVirtualTerminal, SafeHandle>
{
    readonly object _lock;

    readonly byte[] _buffer;

    ReadOnlyMemory<byte> _buffered;

    public WindowsTerminalReader(
        WindowsVirtualTerminal terminal,
        string name,
        SafeHandle handle,
        object @lock)
        : base(terminal, name, handle)
    {
        _lock = @lock;
        _buffer = new byte[System.Terminal.Encoding.GetMaxByteCount(2)];
    }

    protected override unsafe int ReadBufferCore(Span<byte> buffer, CancellationToken cancellationToken)
    {
        using var guard = Terminal.Control.Guard();

        // If the handle is invalid, just present the illusion to the user that it has been redirected to /dev/null or
        // something along those lines, i.e. return EOF.
        if (buffer.IsEmpty || !IsValid)
            return 0;

        // The Windows console host is eventually going to support UTF-8 input via the ReadFile function. Sadly, this
        // does not work today; non-ASCII characters just turn into NULs. This means that we have to use the
        // ReadConsoleW function for interactive input and ReadFile for redirected input. This complicates the
        // interactive case considerably since ReadConsoleW operates in terms of UTF-16 code units while the API we
        // offer operates in terms of raw bytes.
        //
        // To solve this problem, we read one or two UTF-16 code units to form a complete code point. We then encode
        // that into UTF-8 in a separate buffer. Finally, we copy as many bytes as possible/requested from the UTF-8
        // buffer to the caller-provided buffer.
        if (IsInteractive)
        {
            lock (_lock)
            {
                if (_buffered.IsEmpty)
                {
                    Span<char> units = stackalloc char[2];
                    var chars = 0;

                    fixed (char* p = &MemoryMarshal.GetReference(units))
                    {
                        bool ret;
                        var read = 0u;

                        while ((ret = ReadConsoleW(Handle, p, 1, out read, null)) &&
                            Marshal.GetLastPInvokeError() == (int)WIN32_ERROR.ERROR_OPERATION_ABORTED &&
                            read == 0)
                        {
                            // Retry in case we get interrupted by a signal.
                        }

                        if (!ret)
                            WindowsTerminalUtility.ThrowIfUnexpected($"Could not read from {Name}");

                        if (read == 0)
                            return 0;

                        // There is a bug where ReadConsoleW will not process Ctrl-Z properly even though ReadFile will.
                        // The good news is that we can fairly easily emulate what the console host should be doing by
                        // just pretending that there is no more data to be read.
                        if (!Terminal.IsRawMode && *p == '\x1a')
                            return 0;

                        chars++;

                        // If we got a high surrogate, we expect to instantly see a low surrogate following it. In
                        // really bizarre situations (e.g. broken WriteConsoleInput calls), this might not be the case
                        // though; in such a case, we will just let UTF8Encoding encode the lone high surrogate into a
                        // replacement character (U+FFFD).
                        //
                        // It is not really clear whether this is the right thing to do. A case could easily be made for
                        // passing the lone surrogate through unmodified or simply discarding it...
                        if (char.IsHighSurrogate(*p))
                        {
                            while ((ret = ReadConsoleW(Handle, p + 1, 1, out read, null)) &&
                                Marshal.GetLastPInvokeError() == (int)WIN32_ERROR.ERROR_OPERATION_ABORTED &&
                                read == 0)
                            {
                                // Retry in case we get interrupted by a signal.
                            }

                            // See comments in UnixTerminalWriter for why we are only throwing on a failed read that
                            // read nothing.
                            if (read != 0)
                                chars++;
                            else if (!ret)
                                WindowsTerminalUtility.ThrowIfUnexpected($"Could not read from {Name}");
                        }

                        // Encode the UTF-16 code unit(s) into UTF-8 and grab a slice of the buffer corresponding to
                        // just the portion used.
                        _buffered = _buffer.AsMemory(0, System.Terminal.Encoding.GetBytes(units[..chars], _buffer));
                    }
                }

                // Now that we have some UTF-8 text buffered up, we can copy it over to the buffer provided by the
                // caller and adjust our UTF-8 buffer accordingly. Be careful not to overrun either buffer.
                var copied = Math.Min(_buffered.Length, buffer.Length);

                _buffered.Span[..copied].CopyTo(buffer[..copied]);
                _buffered = _buffered[copied..];

                return copied;
            }
        }
        else
        {
            bool result;
            uint read;

            // Note that Windows does not support WaitForMultipleObjects on files, so there is no point in trying to
            // use WindowsCancellationEvent in this case.
            lock (_lock)
                fixed (byte* p = &MemoryMarshal.GetReference(buffer))
                    result = ReadFile(Handle, p, (uint)buffer.Length, &read, null);

            // See comments in UnixTerminalWriter for why we are only throwing on a failed read that read nothing.
            if (!result && read == 0)
                WindowsTerminalUtility.ThrowIfUnexpected($"Could not read from {Name}");

            return (int)read;
        }
    }
}
