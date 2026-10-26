using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class WorkTreeSteps(WorkTreeState workTree)
{
    private string Entry(string relativePath)
    {
        string path = Path.Combine(workTree.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    [Given("a work tree in a new directory that holds a directory {string}")]
    public void GivenDirectory(string relativePath) => Directory.CreateDirectory(Entry(relativePath));

    [Given("a work tree in a new directory that holds a directory {string} with no directory {string} in it")]
    public void GivenDirectoryWithout(string relativePath, string child)
    {
        GivenDirectory(relativePath);
        Assert.False(Directory.Exists(Path.Combine(workTree.Path, relativePath, child)));
    }

    [Given("a work tree in a new directory that holds a file {string}")]
    public void GivenFile(string relativePath) => File.WriteAllText(Entry(relativePath), "gitdir: ../.git/modules/lib\n");

    [Given("a work tree in a new directory that holds a file {string} with the lines:")]
    public void GivenFileWithLines(string relativePath, DataTable lines)
        => File.WriteAllLines(Entry(relativePath), lines.Rows.Select(row => row["line"]));

    [Then("the file {string} in that work tree holds the lines:")]
    public void ThenTheFileHoldsTheLines(string relativePath, DataTable lines)
        => Assert.Equal(lines.Rows.Select(row => row["line"]), File.ReadAllLines(Path.Combine(workTree.Path, relativePath)));

    [Given("a work tree in a new directory that holds a symlink {string}")]
    public void GivenSymlink(string relativePath)
    {
        string target = Directory.CreateDirectory(Path.Combine(workTree.Path, "link-target")).FullName;
        File.CreateSymbolicLink(Entry(relativePath), target);
    }
}
