using System.Globalization;

namespace Core;

/// <summary>What <see cref="ApiGate"/> listed: the sorted lines of the public API, or the fault that stopped the listing.</summary>
public sealed record ApiListing(IReadOnlyList<string>? Lines, string? Fault);

/// <summary>What <see cref="ApiGate.CreateAsync"/> gives: the gate, or the text of the comment that stops the run.</summary>
public sealed record ApiGateStart(ApiGate? Gate, string? Stop);

/// <summary>The API gate of a reframe: the public API of the code under src/, listed with the image's API printer in a copy of
/// the head commit, against <paramref name="baseLines"/>, the listing of the base commit.</summary>
public sealed class ApiGate(Sandbox sandbox, IReadOnlyList<string> baseLines, TextWriter? log = null) : IGate
{
    /// <summary>Where sandbox/Dockerfile publishes the printer.</summary>
    public const string Printer = "/opt/api-printer/ApiPrinter.dll";

    /// <inheritdoc/>
    public GateName Name => GateName.Api;

    /// <summary>Lists the public API of the head commit, which is the base commit before the first build, and gives the gate
    /// over that listing, or the text of the comment that stops the run when the listing fails.</summary>
    public static async Task<ApiGateStart> CreateAsync(Sandbox sandbox, TextWriter? log = null, CancellationToken cancellationToken = default)
    {
        ApiListing listing = await ListAsync(sandbox, log, cancellationToken).ConfigureAwait(false);
        return listing.Lines is IReadOnlyList<string> lines
            ? new ApiGateStart(new ApiGate(sandbox, lines, log), null)
            : new ApiGateStart(null, CommentText.Fit("The public API of the base commit could not be listed, so there is no build:", [listing.Fault ?? ""]));
    }

    /// <summary>Gives <c>same</c>, or <c>changed</c> with one Redo text for each lost and each new line, or with the fault of a
    /// listing that failed.</summary>
    public async Task<GateOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        ApiListing listing = await ListAsync(sandbox, log, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> texts = listing.Lines is IReadOnlyList<string> lines ? Compare(baseLines, lines) : [listing.Fault ?? ""];
        return new GateOutcome(texts.Count is 0 ? "same" : "changed", texts);
    }

    /// <summary>Builds a detached copy of the head commit and runs the printer over its src/.</summary>
    public static async Task<ApiListing> ListAsync(Sandbox sandbox, TextWriter? log, CancellationToken cancellationToken = default)
    {
        string copy = Sandbox.ScratchPath($"api-{Guid.NewGuid():N}");
        CommandResult add = await sandbox.RunAsync("git", ["worktree", "add", "--detach", copy, "HEAD"], [], cancellationToken).ConfigureAwait(false);
        if (add.ExitCode is not 0)
        {
            throw new SandboxException(add.StandardError);
        }

        try
        {
            foreach (string[] arguments in new string[][] { ["restore", "--locked-mode"], ["build", "--no-restore"] })
            {
                CommandResult result = await RunDotnetAsync(sandbox, arguments, copy, log, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode is not 0)
                {
                    return new ApiListing(null, string.Create(
                        CultureInfo.InvariantCulture,
                        $"dotnet {arguments[0]} exited with the code {result.ExitCode} in a copy of the head commit\nstandard output: {result.StandardOutput}\nstandard error: {result.StandardError}"));
                }
            }

            CommandResult printed = await RunDotnetAsync(sandbox, [Printer, "src"], copy, log, cancellationToken).ConfigureAwait(false);
            if (printed.ExitCode is not 0)
            {
                return new ApiListing(null, string.Create(
                    CultureInfo.InvariantCulture,
                    $"The API printer exited with the code {printed.ExitCode}\nstandard error: {printed.StandardError}"));
            }

            return new ApiListing([.. printed.StandardOutput.Split('\n').Where(line => line.Length > 0)], null);
        }
        finally
        {
            await sandbox.RunAsync("git", ["worktree", "remove", "--force", copy], [], CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>One text for each line of <paramref name="before"/> that <paramref name="after"/> lacks, then one for each line that is new.</summary>
    public static IReadOnlyList<string> Compare(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        HashSet<string> was = [.. before];
        HashSet<string> now = [.. after];
        return [
            .. before.Where(line => !now.Contains(line)).Order(StringComparer.Ordinal).Select(line => $"lost symbol: {line}"),
            .. after.Where(line => !was.Contains(line)).Order(StringComparer.Ordinal).Select(line => $"new symbol: {line}"),
        ];
    }

    private static async Task<CommandResult> RunDotnetAsync(Sandbox sandbox, IReadOnlyList<string> arguments, string copy, TextWriter? log, CancellationToken cancellationToken)
    {
        CommandResult result = await sandbox.RunAsync("dotnet", arguments, [], copy, cancellationToken).ConfigureAwait(false);
        await ProgressLine.WriteAsync(
            log,
            string.Create(CultureInfo.InvariantCulture, $"command: dotnet {string.Join(' ', arguments)} exited {result.ExitCode}")).ConfigureAwait(false);
        return result;
    }
}
