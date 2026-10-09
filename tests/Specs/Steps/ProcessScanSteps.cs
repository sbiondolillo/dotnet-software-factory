using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ProcessScanSteps(ExceptionState exceptionState)
{
    private static readonly Regex ProcessLeft = new(@"pid (\d+) \(state (\S+), parent (\d+)\)", RegexOptions.Compiled);

    private readonly List<Process> _started = [];

    [Given("another session's program {string} is running, with no test-run marker")]
    public void GivenAnotherSessionsProgramIsRunning(string program) => Start(program, null);

    [Given("another test run's program {string} is running")]
    public void GivenAnotherTestRunsProgramIsRunning(string program) => Start(program, Guid.NewGuid().ToString("N"));

    [When("the check that the {string} program is no longer running runs")]
    public async Task WhenTheCheckRuns(string program)
    {
        try
        {
            await CommandRunnerSteps.ThenTheProgramIsNoLongerRunning(program);
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [Then("the other session's and the other test run's {string} programs are still running")]
    public void ThenTheOtherProgramsAreStillRunning(string program)
    {
        Assert.Equal(2, _started.Count);
        Assert.All(_started, process => Assert.Equal(program, ReadArgv(process.Id)));
    }

    [Then("the exception message names the process id of the {string} program, the state {string} and the process id of its parent {string}")]
    public void ThenTheExceptionMessageNamesTheProcess(string program, string state, string parent)
    {
        Assert.NotNull(exceptionState.Exception);
        Match match = ProcessLeft.Match(exceptionState.Exception.Message);
        Assert.True(match.Success, exceptionState.Exception.Message);
        Assert.Equal(program, ReadArgv(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(state, match.Groups[2].Value);
        Assert.Equal(parent, ReadArgv(int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [AfterScenario]
    public void StopEveryProgram()
    {
        foreach (Process process in _started)
        {
            if (!process.HasExited)
            {
                process.Kill();
            }

            process.Dispose();
        }
    }

    private static string ReadArgv(int pid)
        => string.Join(' ', Encoding.UTF8.GetString(File.ReadAllBytes($"/proc/{pid}/cmdline")).Split('\0', StringSplitOptions.RemoveEmptyEntries));

    private void Start(string program, string? runId)
    {
        string[] argv = program.Split(' ');
        var psi = new ProcessStartInfo { FileName = argv[0], UseShellExecute = false };
        foreach (string argument in argv[1..])
        {
            psi.ArgumentList.Add(argument);
        }

        if (runId is null)
        {
            psi.Environment.Remove(ProcessScan.MarkerName);
        }
        else
        {
            psi.Environment[ProcessScan.MarkerName] = runId;
        }

        _started.Add(Process.Start(psi)!);
    }
}
