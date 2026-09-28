namespace Core;

/// <summary>Thrown by <see cref="TestReport"/> when the XML parser refuses a TRX file's content.
/// The message names the file's path and holds the parser's own message.</summary>
public sealed class TestReportException(string path, string parserMessage)
    : Exception($"{path}: {parserMessage}")
{
    /// <summary>The path of the TRX file the parser refused.</summary>
    public string Path { get; } = path;
}
