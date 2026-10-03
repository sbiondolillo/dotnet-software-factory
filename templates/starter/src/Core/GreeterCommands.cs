using System.CommandLine;

namespace Core;

/// <summary>The command tree of the console program: one optional argument, <c>name</c>.</summary>
public static class GreeterCommands
{
    /// <summary>Builds the root command, which greets <c>name</c>, or <c>world</c> with no argument.</summary>
    public static RootCommand Create()
    {
        var name = new Argument<string>("name")
        {
            Description = "Who to greet.",
            DefaultValueFactory = _ => "world",
        };
        var root = new RootCommand("Greets by name.") { name };
        root.SetAction(parse =>
        {
            parse.InvocationConfiguration.Output.WriteLine(Greeter.Greet(parse.GetValue(name)!));
            return 0;
        });
        return root;
    }
}
