using Core;

using var handler = new HttpClientHandler();
var host = new ConsoleHost(
    Environment.GetEnvironmentVariable,
    new CommandRunner(),
    handler,
    Console.Out,
    Console.Error,
    Directory.GetCurrentDirectory(),
    (span, token) => Task.Delay(span, token),
    TimeProvider.System);
return await ConsoleProgram.RunAsync(args, host).ConfigureAwait(false);
