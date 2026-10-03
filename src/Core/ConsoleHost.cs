namespace Core;

/// <summary>What the console program touches outside itself: the environment, the programs it runs, GitHub, the streams, the delay and the clock.</summary>
public sealed record ConsoleHost(
    Func<string, string?> Environment,
    CommandRunner Runner,
    HttpMessageHandler Handler,
    TextWriter Out,
    TextWriter Error,
    string RootDirectory,
    Func<TimeSpan, CancellationToken, Task> Delay,
    TimeProvider Time);
