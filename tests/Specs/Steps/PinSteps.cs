using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class PinSteps : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pin-").FullName;
    private Pin? _pin;
    private PinComparison? _comparison;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Given("a directory holds the file {string} with the content {string}")]
    [Given("the directory holds the file {string} with the content {string}")]
    public void GivenTheDirectoryHolds(string path, string content)
    {
        string full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Given("I have taken a pin of the directory")]
    [When("I take a pin of the directory")]
    public void TakePin() => _pin = Pin.Take(_root);

    [Given("the file {string} is deleted from the directory")]
    public void GivenDeleted(string path) => File.Delete(Path.Combine(_root, path));

    [When("one byte of {string} changes")]
    public void WhenOneByteChanges(string path)
    {
        string full = Path.Combine(_root, path);
        byte[] bytes = File.ReadAllBytes(full);
        bytes[0] ^= 1;
        File.WriteAllBytes(full, bytes);
    }

    [When("I compare the directory with the pin")]
    public void WhenCompare() => _comparison = _pin!.Compare(_root);

    [Then("the pin holds {string} with the hash {string}")]
    public void ThenPinHolds(string path, string hash) => Assert.Equal(hash, _pin!.Hashes[path]);

    [Then("the pin holds no {string}")]
    public void ThenPinHoldsNo(string path) => Assert.False(_pin!.Hashes.ContainsKey(path));

    [Then("the changed, added and removed lists are all empty")]
    public void ThenAllEmpty()
    {
        Assert.Empty(_comparison!.Changed);
        Assert.Empty(_comparison.Added);
        Assert.Empty(_comparison.Removed);
    }

    [Then("the changed list is empty")]
    public void ThenChangedEmpty() => Assert.Empty(_comparison!.Changed);

    [Then("the added list is empty")]
    public void ThenAddedEmpty() => Assert.Empty(_comparison!.Added);

    [Then("the removed list is empty")]
    public void ThenRemovedEmpty() => Assert.Empty(_comparison!.Removed);

    [Then("the changed list holds {string} and {string}")]
    public void ThenChangedHolds(string a, string b) => Assert.Equal(Sorted(a, b), _comparison!.Changed);

    [Then("the removed list holds {string}")]
    public void ThenRemovedHolds(string a) => Assert.Equal([a], _comparison!.Removed);

    [Then("the added list holds {string}")]
    public void ThenAddedHolds(string a) => Assert.Equal([a], _comparison!.Added);

    [Then("the added list holds {string}, {string}, {string} and {string}")]
    public void ThenAddedHolds4(string a, string b, string c, string d) =>
        Assert.Equal(Sorted(a, b, c, d), _comparison!.Added);

    private static string[] Sorted(params string[] paths)
    {
        Array.Sort(paths, StringComparer.Ordinal);
        return paths;
    }
}
