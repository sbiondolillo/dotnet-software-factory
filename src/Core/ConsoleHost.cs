namespace Core;

/// <summary>What the console program touches outside itself: the environment, the programs it runs, GitHub, the streams, the delay,
/// the clock, the function that gives the start of a run's backend from its options, and the fixed choices of each settle.</summary>
public sealed record ConsoleHost(
    Func<string, string?> Environment,
    CommandRunner Runner,
    HttpMessageHandler Handler,
    TextWriter Out,
    TextWriter Error,
    string RootDirectory,
    Func<TimeSpan, CancellationToken, Task> Delay,
    TimeProvider Time,
    Func<BackendOptions, StartBackend> Backend,
    SettleSettings Settings);
