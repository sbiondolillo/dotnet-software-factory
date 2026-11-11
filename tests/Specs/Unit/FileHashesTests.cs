using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FileHashesTests : IDisposable
{
    private const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("file-hashes-work-");
    private readonly DirectoryInfo _outside = Directory.CreateTempSubdirectory("file-hashes-outside-");

    public void Dispose()
    {
        _work.Delete(recursive: true);
        _outside.Delete(recursive: true);
    }

    private string Work(string relative) => Path.Combine(_work.FullName, relative);

    private Dictionary<string, string> ReadAll() => new FileHashes(_work.FullName).Read(_ => true);

    /// <summary>Reads on a thread of its own, so a read that never ends fails the test in place of hanging it.</summary>
    private Task<Dictionary<string, string>> ReadWithinLimit() =>
        Task.Run(ReadAll, TestContext.Current.CancellationToken).WaitAsync(Limit, TestContext.Current.CancellationToken);

    private static string HashOf(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void ALinkToAFileOutsideHasTheHashOfItsTargetTextWhateverTheBytesOfTheTarget()
    {
        string target = Path.Combine(_outside.FullName, "secret.txt");
        File.WriteAllText(target, "one");
        File.CreateSymbolicLink(Work("a.txt"), target);
        Dictionary<string, string> before = ReadAll();

        File.WriteAllText(target, "two");

        Assert.Equal(HashOf(target), before["a.txt"]);
        Assert.Equal(before, ReadAll());
    }

    [Fact]
    public void ALinkToADirectoryOutsideIsOneEntryWithNoPathBelowIt()
    {
        File.WriteAllText(Path.Combine(_outside.FullName, "x.txt"), "x");
        Directory.CreateDirectory(Work("specs"));
        Directory.CreateSymbolicLink(Work("specs/features"), _outside.FullName);

        Assert.Equal(["specs/features"], ReadAll().Keys);
    }

    [Fact]
    public void ALinkWithADifferentTargetHasADifferentHash()
    {
        File.CreateSymbolicLink(Work("a"), "/one");
        string first = ReadAll()["a"];
        File.Delete(Work("a"));
        File.CreateSymbolicLink(Work("a"), "/two");

        Assert.NotEqual(first, ReadAll()["a"]);
    }

    [Fact]
    public async Task ALinkToDevZeroIsReadWithoutAReadOfTheDevice()
    {
        File.CreateSymbolicLink(Work("specs-x.feature"), "/dev/zero");

        Assert.Equal(HashOf("/dev/zero"), (await ReadWithinLimit())["specs-x.feature"]);
    }

    [Fact]
    public async Task ANamedPipeWithNoWriterIsOneEntryWithTheHashOfEmptyBytes()
    {
        using (var mkfifo = Process.Start("mkfifo", Work("pipe")))
        {
            await mkfifo.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, mkfifo.ExitCode);
        }

        Assert.Equal(new Dictionary<string, string> { ["pipe"] = EmptyHash }, await ReadWithinLimit());
    }

    [Fact]
    public async Task ASparseFileOf3GBGivesTheHashOfItsBytes()
    {
        using (FileStream stream = File.Create(Work("large.bin")))
        {
            stream.SetLength(3L * 1024 * 1024 * 1024);
        }

        Assert.Equal("305b66a59d15b252092fbda9d09711230c429f351897cbd430e7b55a35fd3b97", (await ReadWithinLimit())["large.bin"]);
    }

    [Fact]
    public void ARegularFileHasTheHashOfItsBytesUnderItsPathWithSlashes()
    {
        Directory.CreateDirectory(Work("a/b"));
        File.WriteAllText(Work("a/b/c.txt"), "hello");
        File.WriteAllText(Work("empty.txt"), "");

        Assert.Equal(
            new Dictionary<string, string> { ["a/b/c.txt"] = HashOf("hello"), ["empty.txt"] = EmptyHash },
            ReadAll());
    }

    [Fact]
    public void AnEntryThatTheFilterDropsIsNoEntry()
    {
        Directory.CreateDirectory(Work("a"));
        File.WriteAllText(Work("a/kept.txt"), "k");
        File.WriteAllText(Work("a/dropped.txt"), "d");
        File.CreateSymbolicLink(Work("a/link"), "/dev/zero");

        Assert.Equal(["a/kept.txt"], new FileHashes(_work.FullName).Read(path => path is "a/kept.txt").Keys);
    }
}
