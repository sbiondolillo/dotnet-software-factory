using System.CommandLine;

namespace Core;

/// <summary>The command tree of the console program: one optional argument, <c>name</c>.</summary>
public static class StarterCommands
{
    /// <summary>The root command; its action prints the greeting to <paramref name="output"/>.</summary>
    public static RootCommand Create(TextWriter output)
    {
        var name = new Argument<string>("name")
        {
            Description = "Who to greet.",
            DefaultValueFactory = _ => "world",
        };
        var root = new RootCommand("Greets a name.") { name };
        root.SetAction(parse =>
        {
            output.WriteLine(Greeter.Greet(parse.GetValue(name) ?? "world"));
            return 0;
        });
        return root;
    }
}
