using System.Net;
using System.Text;
using System.Text.Json;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitHubClientSteps(ExceptionState exceptionState, RecordingHandler handler) : IDisposable
{
    private readonly RecordingHandler _handler = handler;
    private GitHubClient? _client;
    private JsonElement _returned;
    private GitHubIssue? _issue;

    public void Dispose() => _handler.Dispose();

    [Given("a GitHub client built with the token {string}")]
    public void GivenAGitHubClientBuiltWithTheToken(string token) =>
        _client = new GitHubClient(new HttpClient(_handler), token);

    [Given("GitHub answers with the status code {int} and JSON whose {string} is {string}")]
    public void GivenGitHubAnswersWithJson(int status, string name, string value) =>
        _handler.Answer(status, JsonSerializer.Serialize(new Dictionary<string, string> { [name] = value }));

    [Given("GitHub answers with a status code outside 200 to 299 and a body")]
    public void GivenGitHubAnswersOutside() => _handler.Answer(500, "server exploded");

    [Given("GitHub answers with the status code {int} and a body")]
    public void GivenGitHubAnswersWithStatusAndBody(int status) => _handler.Answer(status, "Not Found body");

    [Given("GitHub answers the read of an issue with the title {string}, the body {string}, the user login {string} and the labels {string} and {string}")]
    public void GivenIssueAnswer(string title, string body, string login, string first, string second) =>
        _handler.Answer(200, IssueJson(title, JsonSerializer.Serialize(body), login, [first, second]));

    [Given("GitHub answers the read of an issue with the body null")]
    public void GivenNullBody() => _handler.Answer(200, IssueJson("t", "null", "u", ["bug"]));

    [Given("GitHub answers the read of an issue with an empty list of labels")]
    public void GivenEmptyLabels() => _handler.Answer(200, IssueJson("t", "\"b\"", "u", []));

    [When("I send a {string} request for the path {string}")]
    public async Task WhenISend(string method, string path) => await TrySend(method, path, null);

    [When("I send a {string} request for the path {string} with a JSON body whose {string} is {string}")]
    public async Task WhenISendWithBody(string method, string path, string name, string value) =>
        await TrySend(method, path, JsonSerializer.Serialize(new Dictionary<string, string> { [name] = value }));

    [When("I read issue {int} of the repo {string} of the owner {string}")]
    public async Task WhenIRead(int number, string repo, string owner)
    {
        try
        {
            _issue = await _client!.ReadIssueAsync(owner, repo, number);
        }
        catch (GitHubRequestException ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [When("I post the comment {string} on issue {int} of the repo {string} of the owner {string}")]
    public async Task WhenIPost(string text, int number, string repo, string owner) =>
        await _client!.PostCommentAsync(owner, repo, number, text);

    [Then("the stand-in received one request")]
    public void ThenOneRequest() => Assert.Single(_handler.Requests);

    [Then("the recorded request has the method {string} and the address {string}")]
    public void ThenMethodAndAddress(string method, string address)
    {
        RecordedRequest request = _handler.Requests[^1];
        Assert.Equal(method, request.Method);
        Assert.Equal(address, request.Uri);
    }

    [Then("the recorded request holds the header {string} with the value {string}")]
    public void ThenHeader(string name, string value) =>
        Assert.Equal(value, _handler.Requests[^1].Headers[name]);

    [Then("the content of the recorded request is JSON whose {string} is {string}")]
    public void ThenContentJson(string name, string value)
    {
        using JsonDocument document = JsonDocument.Parse(_handler.Requests[^1].Content!);
        Assert.Equal(value, document.RootElement.GetProperty(name).GetString());
    }

    [Then("the returned JSON has {string} equal to {string}")]
    public void ThenReturnedJson(string name, string value) =>
        Assert.Equal(value, _returned.GetProperty(name).GetString());

    [Then("the issue has the title {string}, the body {string} and the author {string}")]
    public void ThenIssue(string title, string body, string author)
    {
        Assert.Equal(title, _issue!.Title);
        Assert.Equal(body, _issue.Body);
        Assert.Equal(author, _issue.Author);
    }

    [Then("the issue has the labels {string} and {string}")]
    public void ThenLabels(string first, string second) => Assert.Equal([first, second], _issue!.Labels);

    [Then("the issue has the empty string as its body")]
    public void ThenEmptyBody() => Assert.Equal("", _issue!.Body);

    [Then("the issue has an empty list of labels")]
    public void ThenEmptyLabels() => Assert.Empty(_issue!.Labels);

    [Then("the send fails with a GitHubRequestException")]
    public void ThenFails() => Assert.IsType<GitHubRequestException>(exceptionState.Exception);

    [Then("the exception message names the method {string} and the path {string}")]
    public void ThenNames(string method, string path)
    {
        Assert.Contains(method, exceptionState.Exception!.Message);
        Assert.Contains(path, exceptionState.Exception.Message);
    }

    [Then("the exception message holds the status code and the body of the answer")]
    public void ThenHolds()
    {
        Assert.Contains("500", exceptionState.Exception!.Message);
        Assert.Contains("server exploded", exceptionState.Exception.Message);
    }

    [Then("the exception gives the status code {int}")]
    public void ThenStatus(int status) =>
        Assert.Equal(status, ((GitHubRequestException)exceptionState.Exception!).StatusCode);

    private async Task TrySend(string method, string path, string? body)
    {
        try
        {
            _returned = await _client!.SendAsync(new HttpMethod(method), path, body);
        }
        catch (GitHubRequestException ex)
        {
            exceptionState.Exception = ex;
        }
    }

    private static string IssueJson(string title, string bodyJson, string login, string[] labels)
    {
        string labelJson = string.Join(",", labels.Select(l => $"{{\"id\":1,\"color\":\"fff\",\"name\":{JsonSerializer.Serialize(l)}}}"));
        return $"{{\"id\":9,\"state\":\"open\",\"title\":{JsonSerializer.Serialize(title)},\"body\":{bodyJson},\"user\":{{\"id\":2,\"login\":{JsonSerializer.Serialize(login)}}},\"labels\":[{labelJson}]}}";
    }
}

public sealed record RecordedRequest(string Method, string Uri, Dictionary<string, string> Headers, string? Content);

public sealed class RecordingHandler : HttpMessageHandler
{
    private int _status = 200;
    private string _body = "{\"title\":\"t\",\"body\":\"b\",\"user\":{\"login\":\"u\"},\"labels\":[]}";

    public List<RecordedRequest> Requests { get; } = [];

    public void Answer(int status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value));
        string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsoluteUri, headers, content));
        return new HttpResponseMessage((HttpStatusCode)_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
    }
}
