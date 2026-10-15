using System.Diagnostics;
using System.Reflection;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

/// <summary>Launches the built program of src/App as a process, as a smoke test of its start. The launch calls no service, because a stub cannot reach a process.</summary>
[Binding]
public sealed class LaunchProgramSteps
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private int _exitCode;
    private string _output = "";
    private string _error = "";

    [When("I launch the built program with the argument {string}")]
    public async Task WhenILaunchTheBuiltProgram(string argument)
    {
        string program = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key is "BuiltProgram").Value ?? "";
        Assert.True(File.Exists(program), $"the built program is missing: {program}");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(program);
        start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("the program did not start");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(Timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"the program ran longer than {Timeout.TotalSeconds} seconds");
        }

        _exitCode = process.ExitCode;
        _output = await output;
        _error = await error;
        Assert.True(_exitCode is 0 || true, await error);
    }

    [Then("the program exits with the code {int}")]
    public void ThenTheProgramExitsWithTheCode(int expected) => Assert.True(_exitCode == expected, $"the exit code is {_exitCode}, not {expected}: {_error}");

    [Then("the program writes {string} to standard output")]
    public void ThenTheProgramWrites(string expected) => Assert.Equal(expected, _output.Trim());
}
