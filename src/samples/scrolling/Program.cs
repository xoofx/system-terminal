await OutAsync(
    new ControlBuilder()
        .SetScreenBuffer(ScreenBuffer.Alternate)
        .MoveCursorTo(0, 0)
        .SetScrollMargin(2, Size.Height));

try
{
    await OutAsync(
        new ControlBuilder()
            .SetDecorations(bold: true)
            .PrintLine("The last string entered will be displayed here.")
            .ResetAttributes()
            .PrintLine(new string('-', Size.Width)));

    var rng = new Random();

    [SuppressMessage("Security", "CA5394")]
    byte PickRandom()
    {
        return (byte)rng.Next(byte.MinValue, byte.MaxValue + 1);
    }

    while (true)
    {
        await OutAsync("Input: ");

        if (await ReadLineAsync() is not string str)
            break;

        await OutAsync(
            new ControlBuilder()
                .SaveCursorState()
                .MoveCursorTo(0, 0)
                .ClearLine()
                .SetForegroundColor(PickRandom(), PickRandom(), PickRandom())
                .Print(str.ReplaceLineEndings(string.Empty))
                .ResetAttributes()
                .RestoreCursorState());
    }
}
finally
{
    await OutAsync(
        new ControlBuilder()
            .SetScrollMargin(0, Size.Height)
            .SetScreenBuffer(ScreenBuffer.Main));
}
