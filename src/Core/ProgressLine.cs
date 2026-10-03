namespace Core;

/// <summary>Writes one progress line to a writer, or nothing when there is none.</summary>
internal static class ProgressLine
{
    public static async Task WriteAsync(TextWriter? progress, string line)
    {
        if (progress is not null)
        {
            await progress.WriteLineAsync(line).ConfigureAwait(false);
            await progress.FlushAsync().ConfigureAwait(false);
        }
    }
}
