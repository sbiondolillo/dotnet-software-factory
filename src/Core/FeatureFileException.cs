namespace Core;

/// <summary>Thrown by <see cref="FeatureFiles"/> when Gherkin's own parser rejects a feature file's content.
/// The message names the file's path and holds the parser's own message.</summary>
public sealed class FeatureFileException(string path, string parserMessage)
    : Exception($"{path}: {parserMessage}")
{
    /// <summary>The path of the feature file the parser rejected.</summary>
    public string Path { get; } = path;
}
