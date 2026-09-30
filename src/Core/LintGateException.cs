namespace Core;

/// <summary>Thrown when <c>dotnet format</c> in a <see cref="LintGate"/> exits with a code other than 0.</summary>
public sealed class LintGateException(int exitCode, string standardError)
    : Exception($"dotnet format failed with exit code {exitCode}: {standardError}")
{
}
