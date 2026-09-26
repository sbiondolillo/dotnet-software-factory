using Xunit.Runner.InProc.SystemConsole;

namespace Integration;

/// <summary>The console entry point of the integration suite.</summary>
public static class Program
{
    /// <summary>Runs the in-process console test runner and returns its result.</summary>
    public static int Main(string[] args) => ConsoleRunner.Run(args).GetAwaiter().GetResult();
}
