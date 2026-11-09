using System.Text.RegularExpressions;

namespace Core;

/// <summary>Thrown by <see cref="FeatureFiles"/> when a feature file is a symbolic link, or when Gherkin's parser rejects its content.
/// The message names the file's path and holds the reason. For a file on disk it holds the positions, and no text of the file.</summary>
public sealed partial class FeatureFileException(string path, string reason)
    : Exception($"{path}: {reason}")
{
    /// <summary>The path of the feature file that was refused.</summary>
    public string Path { get; } = path;

    /// <summary>Gives an exception for the same path whose reason holds only the parser's positions of this message, and no
    /// text of the file.</summary>
    public FeatureFileException WithoutText()
    {
        IEnumerable<string> positions = Position().Matches(Message).Select(match => match.Value);
        return new FeatureFileException(Path, $"the parser rejects the text at {string.Join(", ", positions)}");
    }

    [GeneratedRegex(@"^\(\d+:\d+\)", RegexOptions.Multiline)]
    private static partial Regex Position();
}
