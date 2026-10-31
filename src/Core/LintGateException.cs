namespace Core;

/// <summary>Thrown when <c>dotnet format</c>, or the restore before it, run by a <see cref="LintGate"/> exits with a non-zero code.</summary>
public sealed class LintGateException(int exitCode, string standardError, string command = "dotnet format")
    : Exception($"{command} failed with exit code {exitCode}: {standardError}")
{
}
