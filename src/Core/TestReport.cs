using System.Xml;
using System.Xml.Linq;

namespace Core;

/// <summary>The outcome of one test in a TRX file.</summary>
public enum TestOutcome
{
    /// <summary>The test passed.</summary>
    Passed,

    /// <summary>The test failed, or ended in any outcome other than Passed and NotExecuted.</summary>
    Failed,

    /// <summary>The test was not executed.</summary>
    Skipped,
}

/// <summary>One test result of a TRX file.</summary>
/// <param name="ClassName">The class with its namespace.</param>
/// <param name="Method">The method name, without class or arguments.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="Message">The <c>ErrorInfo/Message</c> text, empty when absent.</param>
public sealed record ReportedTest(string ClassName, string Method, TestOutcome Outcome, string Message);

/// <summary>Reads a TRX file as a list of tests.</summary>
public static class TestReport
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>Reads each result of the TRX file at <paramref name="path"/>, in file order.
    /// Throws <see cref="TestReportException"/> when the XML parser refuses the content;
    /// a missing file throws the file system's own exception.</summary>
    public static IReadOnlyList<ReportedTest> Read(string path)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(File.ReadAllText(path));
        }
        catch (XmlException e)
        {
            throw new TestReportException(path, e.Message);
        }

        var definitions = doc.Descendants(Ns + "UnitTest")
            .Where(u => u.Attribute("id") is not null)
            .GroupBy(u => (string)u.Attribute("id")!)
            .ToDictionary(g => g.Key, g => g.First().Element(Ns + "TestMethod"));

        return [.. doc.Descendants(Ns + "UnitTestResult").Select(r =>
        {
            var id = (string?)r.Attribute("testId") ?? "";
            definitions.TryGetValue(id, out var method);
            var outcome = (string?)r.Attribute("outcome") switch
            {
                "Passed" => TestOutcome.Passed,
                "NotExecuted" => TestOutcome.Skipped,
                _ => TestOutcome.Failed,
            };
            var message = r.Element(Ns + "Output")?.Element(Ns + "ErrorInfo")?.Element(Ns + "Message")?.Value ?? "";
            return new ReportedTest(
                (string?)method?.Attribute("className") ?? "",
                (string?)method?.Attribute("name") ?? "",
                outcome,
                message);
        })];
    }
}
