using System.Text;
using System.Text.RegularExpressions;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class AgentPromptTests
{
    private const string Fixed = "Lead.\n";

    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private static AgentPrompt Prompt(params string[] pieces) => new(pieces, parts => Fixed + string.Concat(parts.Select(part => $"<{part}>")));

    [Fact]
    public void APromptUnderTheLimitIsUnchanged()
    {
        AgentPrompt prompt = Prompt("one", "two");

        Assert.Equal("Lead.\n<one><two>", prompt.Fit(1000));
        Assert.Equal(prompt.Text, prompt.Fit(Bytes(prompt.Text)));
    }

    [Fact]
    public void OnePieceIsCutToTheLargestSizeThatFits()
    {
        AgentPrompt prompt = Prompt(new string('a', 1000));

        string text = Assert.IsType<string>(prompt.Fit(100));

        Assert.Equal(Fixed + "<" + new string('a', 100 - Bytes(Fixed) - 2 - 5) + "[cut]>", text);
        Assert.Equal(100, Bytes(text));
    }

    [Fact]
    public void SeveralPiecesAreCutToTheSameSizeAndAShortPieceStaysWhole()
    {
        AgentPrompt prompt = Prompt(new string('a', 1000), new string('b', 800), "short");

        string text = Assert.IsType<string>(prompt.Fit(500));

        int a = Regex.Match(text, "a{2,}").Length;
        int b = Regex.Match(text, "b{2,}").Length;
        Assert.Equal(a, b);
        Assert.Contains("<short>", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split("[cut]").Length - 1);
        Assert.True(Bytes(text) <= 500);
        Assert.True(Bytes(Fixed + $"<{new string('a', a + 1)}[cut]><{new string('b', b + 1)}[cut]><short>") > 500);
    }

    [Theory]
    [InlineData("é")]
    [InlineData("\U0001F600")]
    public void ACutEndsOnAWholeRune(string letter)
    {
        AgentPrompt prompt = Prompt(string.Concat(Enumerable.Repeat(letter, 1000)));

        for (int limit = 50; limit < 60; limit++)
        {
            string text = Assert.IsType<string>(prompt.Fit(limit));
            Assert.True(Bytes(text) <= limit);
            Assert.DoesNotContain('�', Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)));
            Assert.Contains(letter + "[cut]", text, StringComparison.Ordinal);
            Assert.True(Bytes(text) > limit - Bytes(letter));
        }
    }

    [Fact]
    public void APromptWithNoCuttablePieceOverTheLimitGivesNull()
    {
        var prompt = new AgentPrompt([], _ => new string('a', 101));

        Assert.Null(prompt.Fit(100));
        Assert.Equal(new string('a', 101), prompt.Fit(101));
    }

    [Fact]
    public void AFixedPartOverTheLimitGivesNullEvenWithAPieceToCut()
    {
        AgentPrompt prompt = Prompt(new string('a', 1000));

        Assert.Null(prompt.Fit(Bytes(Fixed)));
    }

    [Fact]
    public void TheMarkerCountsTowardTheLimit()
    {
        AgentPrompt prompt = Prompt(new string('a', 20));
        int limit = Bytes(Fixed) + 2 + 10;

        string text = Assert.IsType<string>(prompt.Fit(limit));

        Assert.Equal(Fixed + "<aaaaa[cut]>", text);
        Assert.Equal(limit, Bytes(text));
    }
}
