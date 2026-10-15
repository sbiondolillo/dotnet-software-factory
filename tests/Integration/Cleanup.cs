using System.Runtime.ExceptionServices;
using Xunit;

namespace Integration;

/// <summary>
/// The cleanup of one integration test. The body records each thing just after it creates it, and
/// <see cref="RunAsync"/> runs every recorded step after the body, the last recorded first, even when a
/// step before it fails. When the body failed, each error of a step goes to the test output, and the test
/// fails with the body's exception. When the body passed, an error of a step fails the test.
/// </summary>
/// <remarks>
/// A step passes no cancellation token, so it runs after the test is cancelled too.
/// </remarks>
public sealed class Cleanup
{
    private readonly List<(string What, Func<Task> Step)> _steps = [];

    /// <summary>Records a step that removes one thing the body created. <paramref name="what"/> names the step in an error.</summary>
    public void Add(string what, Func<Task> step) => _steps.Add((what, step));

    /// <summary>Records a step that removes one thing the body created. <paramref name="what"/> names the step in an error.</summary>
    public void Add(string what, Action step) => Add(what, () =>
    {
        step();
        return Task.CompletedTask;
    });

    /// <summary>Runs the body, then every step it recorded.</summary>
    public static async Task RunAsync(Func<Cleanup, Task> body)
    {
        var cleanup = new Cleanup();
        ExceptionDispatchInfo? failure = null;
        try
        {
            await body(cleanup);
        }
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
        }

        var errors = new List<Exception>();
        for (int i = cleanup._steps.Count - 1; i >= 0; i--)
        {
            (string what, Func<Task> step) = cleanup._steps[i];
            try
            {
                await step();
            }
            catch (Exception exception)
            {
                errors.Add(new InvalidOperationException($"The cleanup step '{what}' failed: {exception.Message}", exception));
            }
        }

        if (failure is not null)
        {
            foreach (Exception error in errors)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(error.ToString());
            }

            failure.Throw();
        }

        if (errors.Count is not 0)
        {
            throw new AggregateException("The cleanup failed.", errors);
        }
    }
}
