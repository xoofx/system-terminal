namespace System.Terminals;

abstract class NativeVirtualTerminal<THandle> : SystemVirtualTerminal
{
    public abstract bool IsHandleValid(THandle handle, bool write);

    public abstract bool IsHandleInteractive(THandle handle);
}
