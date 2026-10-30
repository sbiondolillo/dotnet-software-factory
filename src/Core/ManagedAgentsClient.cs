namespace Core;

/// <summary>Sends every request of the factory to the Managed Agents API: sessions, events, resources and files.</summary>
public sealed class ManagedAgentsClient(
    HttpClient http,
    string apiKey,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    TimeProvider? time = null)
{
    /// <summary>The beta header every request carries.</summary>
    public const string Beta = "managed-agents-2026-04-01";

    internal HttpClient Http { get; } = http;

    internal string ApiKey { get; } = apiKey;

    internal Func<TimeSpan, CancellationToken, Task> Delay { get; } = delay ?? ((wait, token) => Task.Delay(wait, token));

    internal TimeProvider Time { get; } = time ?? TimeProvider.System;
}
