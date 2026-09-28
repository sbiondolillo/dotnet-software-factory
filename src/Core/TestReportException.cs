namespace Core;

/// <summary>Thrown by <see cref="TestReport"/> when a file is not a TRX file it can read.
/// The message names the file's path and holds the reason, the XML parser's own message when the parser refused the file.</summary>
public sealed class TestReportException(string path, string reason)
    : Exception($"{path}: {reason}")
{
    /// <summary>The path of the file that could not be read.</summary>
    public string Path { get; } = path;
}
