namespace Core;

/// <summary>Thrown when <c>dotnet format</c> run by a <see cref="LintGate"/> exits with a non-zero code.</summary>
public sealed class LintGateException(int exitCode, string standardError)
    : Exception($"dotnet format failed with exit code {exitCode}: {standardError}")
{
}
