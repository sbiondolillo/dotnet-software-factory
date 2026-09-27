namespace Core;

/// <summary>Thrown when the Gherkin parser refuses the text of a <c>.feature</c> file <see cref="FeatureFiles"/> reads.</summary>
public sealed class FeatureFileException(string path, Exception parseError)
    : Exception($"{path}: {parseError.Message}")
{
}
