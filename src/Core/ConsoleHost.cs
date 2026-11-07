namespace Core;

/// <summary>What the console program touches outside itself: the environment, the programs it runs, GitHub, the streams, the delay,
/// the clock, and the function that gives the start of a run's backend from its options.</summary>
public sealed record ConsoleHost(
    Func<string, string?> Environment,
    CommandRunner Runner,
    HttpMessageHandler Handler,
    TextWriter Out,
    TextWriter Error,
    string RootDirectory,
    Func<TimeSpan, CancellationToken, Task> Delay,
    TimeProvider Time,
    Func<BackendOptions, StartBackend> Backend);
