namespace Core;

/// <summary>
/// The session branch holds a commit that the factory refuses, such as a tracked file under <c>.factory/</c>. The run stops, and the
/// message says why.
/// </summary>
public sealed class SessionBranchException(string message) : Exception(message);
