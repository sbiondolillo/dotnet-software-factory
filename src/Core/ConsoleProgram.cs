using System.CommandLine;

namespace Core;

/// <summary>The console program: the verbs <c>run</c> and <c>feedback</c>, over a <see cref="ConsoleHost"/>.</summary>
public static class ConsoleProgram
{
    /// <summary>Runs the program with <paramref name="args"/> and gives back its exit code.</summary>
    public static async Task<int> RunAsync(string[] args, ConsoleHost host, CancellationToken cancellationToken = default)
    {
        RootCommand root = ConsoleCommands.Create(host);
        var configuration = new InvocationConfiguration { Output = host.Out, Error = host.Error };
        return await root.Parse(args).InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }
}
