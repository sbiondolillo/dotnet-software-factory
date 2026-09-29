namespace Core;

/// <summary>Thrown when the <c>docker build</c> a <see cref="SandboxImage"/> runs exits with a non-zero code.</summary>
public sealed class SandboxImageException(string standardError)
    : Exception($"docker build failed: {standardError}")
{
}
