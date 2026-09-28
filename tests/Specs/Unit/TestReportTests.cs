using Core;
using Xunit;

namespace Specs.Unit;

public sealed class TestReportTests : IDisposable
{
    private const string Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".trx");

    public void Dispose() => File.Delete(_path);

    private IReadOnlyList<ReportedTest> ReadXml(string results, string defs)
    {
        File.WriteAllText(_path, $"<TestRun xmlns=\"{Ns}\"><Results>{results}</Results><TestDefinitions>{defs}</TestDefinitions></TestRun>");
        return TestReport.Read(_path);
    }

    private static string Def(string id, string cls, string name) =>
        $"<UnitTest id=\"{id}\"><TestMethod className=\"{cls}\" name=\"{name}\" /></UnitTest>";

    [Fact]
    public void NotXmlThrowsWithPathAndParserMessage()
    {
        File.WriteAllText(_path, "This is not a test report.");
        var e = Assert.Throws<TestReportException>(() => TestReport.Read(_path));
        var parser = Assert.ThrowsAny<System.Xml.XmlException>(() => System.Xml.Linq.XDocument.Parse("This is not a test report."));
        Assert.StartsWith(_path, e.Message, StringComparison.Ordinal);
        Assert.Contains(parser.Message, e.Message, StringComparison.Ordinal);
        Assert.Equal(_path, e.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<TestRun><Results>")]
    public void EmptyOrMalformedThrows(string content)
    {
        File.WriteAllText(_path, content);
        Assert.Throws<TestReportException>(() => TestReport.Read(_path));
    }

    [Fact]
    public void MissingFileThrowsFileNotFound() =>
        Assert.Throws<FileNotFoundException>(() => TestReport.Read(_path));

    [Fact]
    public void FailedMessageExcludesStackTrace()
    {
        var r = ReadXml(
            "<UnitTestResult testId=\"a\" outcome=\"Failed\"><Output><ErrorInfo><Message>one\ntwo\nActual:   2</Message><StackTrace>at X</StackTrace></ErrorInfo></Output></UnitTestResult>",
            Def("a", "C", "M"));
        Assert.Equal("one\ntwo\nActual:   2", Assert.Single(r).Message);
    }

    [Fact]
    public void FailedWithoutErrorInfoHasEmptyMessage()
    {
        var r = ReadXml("<UnitTestResult testId=\"a\" outcome=\"Failed\" />", Def("a", "C", "M"));
        var t = Assert.Single(r);
        Assert.Equal(TestOutcome.Failed, t.Outcome);
        Assert.Equal("", t.Message);
    }

    [Fact]
    public void SkippedReturnsReason()
    {
        var r = ReadXml(
            "<UnitTestResult testId=\"a\" outcome=\"NotExecuted\"><Output><ErrorInfo><Message>held back</Message></ErrorInfo></Output></UnitTestResult>",
            Def("a", "C", "M"));
        var t = Assert.Single(r);
        Assert.Equal(TestOutcome.Skipped, t.Outcome);
        Assert.Equal("held back", t.Message);
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("Error")]
    [InlineData("Aborted")]
    public void OtherOutcomesReadAsFailed(string outcome)
    {
        var r = ReadXml($"<UnitTestResult testId=\"a\" outcome=\"{outcome}\" />", Def("a", "C", "M"));
        Assert.Equal(TestOutcome.Failed, Assert.Single(r).Outcome);
    }

    [Fact]
    public void NoResultsGivesEmptyList() => Assert.Empty(ReadXml("", ""));

    [Fact]
    public void TwoResultsOfOneMethodAreTwoTests()
    {
        var r = ReadXml(
            "<UnitTestResult testId=\"a\" testName=\"M(x: 1)\" outcome=\"Passed\" /><UnitTestResult testId=\"b\" testName=\"M(x: 2)\" outcome=\"Failed\" />",
            Def("a", "C", "M") + Def("b", "C", "M"));
        Assert.Equal(["M", "M"], r.Select(t => t.Method));
        Assert.Equal([TestOutcome.Passed, TestOutcome.Failed], r.Select(t => t.Outcome));
    }

    [Fact]
    public void ThreeTestsInFileOrder()
    {
        var r = ReadXml(
            "<UnitTestResult testId=\"a\" outcome=\"Passed\" /><UnitTestResult testId=\"b\" outcome=\"Failed\" /><UnitTestResult testId=\"c\" outcome=\"NotExecuted\" />",
            Def("a", "Specs.Features.GreetingFeature", "X") + Def("b", "Specs.Unit.GreeterTests", "Y") + Def("c", "Specs.Unit.GreeterTests", "Z"));
        Assert.Equal(["Specs.Features.GreetingFeature", "Specs.Unit.GreeterTests", "Specs.Unit.GreeterTests"], r.Select(t => t.ClassName));
        Assert.Equal([TestOutcome.Passed, TestOutcome.Failed, TestOutcome.Skipped], r.Select(t => t.Outcome));
    }
}
