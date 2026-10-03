using System.CommandLine;

namespace Core;

/// <summary>The command tree of the console program: one optional argument, <c>name</c>.</summary>
public static class GreetCommand
{
    /// <summary>The root command, which prints the greeting of its argument <c>name</c>.</summary>
    public static RootCommand Create()
    {
        var name = new Argument<string>("name")
        {
            Description = "Who to greet.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "world",
        };
        var root = new RootCommand("Prints a greeting.");
        root.Arguments.Add(name);
        root.SetAction(result =>
        {
            result.InvocationConfiguration.Output.WriteLine(Greeter.Greet(result.GetRequiredValue(name)));
            return 0;
        });
        return root;
    }
}
