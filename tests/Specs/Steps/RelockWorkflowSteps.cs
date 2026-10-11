using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class RelockWorkflowSteps
{
    private static string Root => StarterChecks.RepoRoot();

    private static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');

    [Given("the repository checkout")]
    public static void GivenTheRepositoryCheckout() => Assert.True(Directory.Exists(Root));

    [Then("the file {string} holds this text")]
    public static void ThenTheFileHoldsThisText(string path, string text)
    {
        string file = Path.Combine(Root, path);
        Assert.True(File.Exists(file), $"{path} does not exist");
        string[] actual = Lines(File.ReadAllText(file));
        string[] expected = Lines(text);
        for (int i = 0; i < Math.Max(actual.Length, expected.Length); i++)
        {
            string a = i < actual.Length ? actual[i] : "<end>";
            string e = i < expected.Length ? expected[i] : "<end>";
            Assert.True(a == e, $"line {i + 1} differs: expected '{e}', got '{a}'");
        }
    }

    [Then("the file {string} does not contain {string}")]
    public static void ThenTheFileDoesNotContain(string path, string text)
        => Assert.DoesNotContain(text, File.ReadAllText(Path.Combine(Root, path)), StringComparison.Ordinal);
}
