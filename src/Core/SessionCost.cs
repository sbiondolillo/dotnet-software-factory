namespace Core;

/// <summary>One Managed Agents session of a run: its id, its role and its <c>usage.list_cost</c> in cents.</summary>
public sealed record SessionCost(string Id, string Role, int Cents);

/// <summary>What a Managed Agents session does for the run.</summary>
public enum SessionRole
{
    /// <summary>Plans and builds: a read-write token and a commit tail on each message.</summary>
    Builder,

    /// <summary>Reviews: a read-only token and no tail.</summary>
    Reviewer,
}
