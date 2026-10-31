namespace Core;

/// <summary>Thrown by <see cref="FeatureFiles"/> when a feature file is a symbolic link, or when Gherkin's parser rejects its content.
/// The message names the file's path and holds the reason. For a file on disk it holds the positions, and no text of the file.</summary>
public sealed class FeatureFileException(string path, string reason)
    : Exception($"{path}: {reason}")
{
    /// <summary>The path of the feature file that was refused.</summary>
    public string Path { get; } = path;
}
