namespace Core;

/// <summary>Thrown when the <c>docker run</c> a <see cref="Sandbox"/> starts exits with a non-zero code.</summary>
public sealed class SandboxException(string standardError)
    : Exception($"docker run failed: {standardError}")
{
}
