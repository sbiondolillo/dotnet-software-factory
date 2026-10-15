using Xunit;

namespace Integration;

/// <summary>
/// The xUnit collection of every class that writes to GitHub. xUnit runs the tests of one
/// collection in sequence, so one run sends its GitHub writes one at a time. GitHub's secondary
/// limit for content creation counts per account, and a burst of parallel writes reaches it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GitHubWrites
{
    public const string Name = "GitHub writes";
}
