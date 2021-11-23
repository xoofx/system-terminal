namespace System.IO;

public abstract class TerminalHandle
{
    public abstract Stream Stream { get; }

    public abstract bool IsRedirected { get; }
}
