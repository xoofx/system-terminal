Terminal.OutLine("Listening for resize events.");
Terminal.OutLine();

Terminal.Resized += size => Terminal.OutLine("Width = {0}, Height = {1}", size.Width, size.Height);

await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
