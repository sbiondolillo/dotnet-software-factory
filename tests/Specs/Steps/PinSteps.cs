using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class PinSteps : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pin").FullName;
    private Pin? _pin;
    private PinComparison? _result;

    public void Dispose() => Directory.Delete(_dir, true);

    [Given("a directory holding these files")]
    public void GivenADirectoryHoldingTheseFiles(Table table)
    {
        foreach (var row in table.Rows)
        {
            Write(row[0], row[1]);
        }
    }

    [Given("I have taken a pin of the directory")]
    public void GivenIHaveTakenAPin() => _pin = Pin.Take(_dir);

    [Given("one byte of {string} changes")]
    public void GivenOneByteChanges(string path)
    {
        var full = Path.Combine(_dir, path);
        var bytes = File.ReadAllBytes(full);
        bytes[0] ^= 1;
        File.WriteAllBytes(full, bytes);
    }

    [Given("the directory lacks {string}")]
    public void GivenTheDirectoryLacks(string path) => File.Delete(Path.Combine(_dir, path));

    [Given("the directory gains {string} with content {string}")]
    public void GivenTheDirectoryGains(string path, string content) => Write(path, content);

    [When("I take a pin of the directory")]
    public void WhenITakeAPin() => _pin = Pin.Take(_dir);

    [When("I compare the pin with the directory")]
    public void WhenICompare() => _result = _pin!.Compare(_dir);

    [Then("the pin holds {string} with hash {string}")]
    public void ThenThePinHolds(string path, string hash) => Assert.Equal(hash, _pin!.Hashes[path]);

    [Then("the pin holds no {string}")]
    public void ThenThePinHoldsNo(string path) => Assert.DoesNotContain(path, _pin!.Hashes.Keys);

    [Then("the changed, added and removed lists are all empty")]
    public void ThenAllEmpty()
    {
        Assert.Empty(_result!.Changed);
        Assert.Empty(_result.Added);
        Assert.Empty(_result.Removed);
    }

    [Then("the changed list holds {string} and {string}")]
    public void ThenChangedHoldsTwo(string a, string b) => Assert.Equal([a, b], _result!.Changed);

    [Then("the added and removed lists are empty")]
    public void ThenAddedRemovedEmpty()
    {
        Assert.Empty(_result!.Added);
        Assert.Empty(_result.Removed);
    }

    [Then("the removed list holds {string}")]
    public void ThenRemovedHolds(string path) => Assert.Equal([path], _result!.Removed);

    [Then("the changed and added lists are empty")]
    public void ThenChangedAddedEmpty()
    {
        Assert.Empty(_result!.Changed);
        Assert.Empty(_result.Added);
    }

    [Then("the added list holds {string}")]
    public void ThenAddedHolds(string path) => Assert.Equal([path], _result!.Added);

    [Then("the changed and removed lists are empty")]
    public void ThenChangedRemovedEmpty()
    {
        Assert.Empty(_result!.Changed);
        Assert.Empty(_result.Removed);
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
