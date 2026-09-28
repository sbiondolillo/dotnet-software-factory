using System.Security.Cryptography;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class PinTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pintest").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private void Write(string path, string content = "x") => WriteBytes(path, System.Text.Encoding.UTF8.GetBytes(content));

    private void WriteBytes(string path, byte[] bytes)
    {
        var full = Path.Combine(_dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    [Theory]
    [InlineData("obj/global.json")]
    [InlineData("src/obj/Debug/reqnroll.json")]
    [InlineData("bin/global.json")]
    [InlineData("specs/features/bin/a.feature")]
    [InlineData("specs/features/obj/a.feature")]
    [InlineData("Global.json")]
    [InlineData("global.json.bak")]
    [InlineData("reqnroll.jsonc")]
    [InlineData("specs/features.txt")]
    [InlineData("docs/specs/features/a.feature")]
    [InlineData("src/Core/Greeter.cs")]
    public void UnpinnedPathsAreNotPinned(string path)
    {
        Write(path);
        Assert.Empty(Pin.Take(_dir).Hashes);
        var pin = Pin.Take(_dir);
        Write(path + "2");
        var r = pin.Compare(_dir);
        Assert.Empty(r.Added);
    }

    [Theory]
    [InlineData(".editorconfig")]
    [InlineData("global.json")]
    [InlineData("nuget.config")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("reqnroll.json")]
    public void SettingsNamesArePinnedAtAnyDepth(string name)
    {
        Write(name);
        Write("a/b/" + name);
        Assert.Equal(new[] { name, "a/b/" + name }.Order(StringComparer.Ordinal), Pin.Take(_dir).Hashes.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DeepNonFeatureFileUnderSpecsIsPinned()
    {
        Write("specs/features/a/b/notes.md");
        Assert.Contains("specs/features/a/b/notes.md", Pin.Take(_dir).Hashes.Keys);
    }

    [Fact]
    public void HashIsOverRawBytes()
    {
        byte[] bytes = [0xff, 0x0d, 0x0a, 0xfe];
        WriteBytes("global.json", bytes);
        var pin = Pin.Take(_dir);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), pin.Hashes["global.json"]);
        bytes[3] = 0xfd;
        WriteBytes("global.json", bytes);
        Assert.Equal(["global.json"], pin.Compare(_dir).Changed);
    }

    [Fact]
    public void EachPathIsInOneListAndListsAreOrdinal()
    {
        Write("global.json");
        Write("nuget.config");
        Write(".editorconfig");
        var pin = Pin.Take(_dir);
        Write("global.json", "y");
        File.Delete(Path.Combine(_dir, "nuget.config"));
        Write("src/reqnroll.json");
        Write("Directory.Build.props");
        Write("src/Core/Greeter.cs");
        Write(".editorconfig", "z");
        var r = pin.Compare(_dir);
        Assert.Equal([".editorconfig", "global.json"], r.Changed);
        Assert.Equal(["Directory.Build.props", "src/reqnroll.json"], r.Added);
        Assert.Equal(["nuget.config"], r.Removed);
    }

    [Fact]
    public void EmptyDirectoryPinsEmptyAndComparesClean()
    {
        var pin = Pin.Take(_dir);
        Assert.Empty(pin.Hashes);
        var r = pin.Compare(_dir);
        Assert.Empty(r.Changed);
        Assert.Empty(r.Added);
        Assert.Empty(r.Removed);
    }
}
