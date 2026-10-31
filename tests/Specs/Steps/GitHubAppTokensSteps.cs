using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitHubAppTokensSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly RSA _key = RSA.Create(2048);
    private readonly RecordingHandler _handler = new();
    private GitHubAppTokens? _tokens;
    private DateTimeOffset _now;
    private string? _minted;

    public void Dispose()
    {
        _key.Dispose();
        _handler.Dispose();
    }

    [Given("a GitHubAppTokens with the app id {string} and the installation id {string}, a new test RSA key and the time {string}, over a stub HttpMessageHandler")]
    public void GivenAGitHubAppTokens(string appId, string installationId, string time)
    {
        _now = DateTimeOffset.Parse(time, CultureInfo.InvariantCulture);
        _tokens = new GitHubAppTokens(_handler, appId, installationId, _key.ExportRSAPrivateKeyPem(), new FixedTime(_now));
    }

    [Given("GitHub answers the mint with the status {int} and the token {string}")]
    public void GivenGitHubAnswersTheMint(int status, string token) =>
        _handler.Answer = ((HttpStatusCode)status, JsonSerializer.Serialize(new { token, expires_at = "2026-10-30T13:00:00Z" }));

    [Given("GitHub answers with the status {int} and the body {string}")]
    public void GivenGitHubAnswers(int status, string body) => _handler.Answer = ((HttpStatusCode)status, body);

    [When("a caller mints a token for {string} with write access")]
    public Task WhenMintsWrite(string repo) => MintAsync(repo, write: true);

    [When("a caller mints a token for {string} with read access")]
    public Task WhenMintsRead(string repo) => MintAsync(repo, write: false);

    [When("a caller revokes the token {string}")]
    public async Task WhenRevokes(string token)
    {
        exceptionState.Exception = null;
        try
        {
            await _tokens!.RevokeAsync(token, TestContext.Current.CancellationToken);
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [Then("the stub recorded one request {string}")]
    public void ThenTheStubRecordedOneRequest(string request)
    {
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.Equal(request, $"{recorded.Method} {recorded.Uri}");
    }

    [Then("the request holds the header {string} set to {string}")]
    public void ThenTheRequestHoldsTheHeader(string name, string value)
    {
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.True(recorded.Headers.TryGetValue(name, out string? actual), $"no header {name}");
        Assert.Equal(value, actual);
    }

    [Then("^the request authorizes with \"([^\"]*)\" and a JWT whose header is (.*)$")]
    public void ThenTheRequestAuthorizesWithAJwt(string scheme, string header)
    {
        string[] parts = Jwt(scheme);
        Assert.Equal(3, parts.Length);
        Assert.Equal(header, Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
    }

    [Then("the signature of the JWT verifies with the public key of the test RSA key")]
    public void ThenTheSignatureVerifies()
    {
        string[] parts = Jwt("Bearer");
        using var publicKey = RSA.Create();
        publicKey.ImportFromPem(_key.ExportSubjectPublicKeyInfoPem());
        Assert.True(publicKey.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64UrlDecode(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
    }

    [Then("the JWT's {string} is {string}")]
    public void ThenTheJwtClaimIs(string claim, string value) => Assert.Equal(value, Payload().GetProperty(claim).GetString());

    [Then("the JWT's {string} is {int} seconds before the time and its {string} is {int} seconds after the time")]
    public void ThenTheJwtTimesAre(string issued, int before, string expires, int after)
    {
        JsonElement payload = Payload();
        long now = _now.ToUnixTimeSeconds();
        Assert.Equal(now - before, payload.GetProperty(issued).GetInt64());
        Assert.Equal(now + after, payload.GetProperty(expires).GetInt64());
        Assert.True(payload.GetProperty(expires).GetInt64() - now <= 600, "the expiry is more than 10 minutes away");
    }

    [Then("^the request body is (.*)$")]
    public void ThenTheRequestBodyIs(string body)
    {
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.Equal(body, recorded.Body);
    }

    [Then("the minted token is {string}")]
    public void ThenTheMintedTokenIs(string token) => Assert.Equal(token, _minted);

    [Then("the call throws GitHubRequestException whose message holds {string} and {string}")]
    public void ThenTheCallThrows(string first, string second)
    {
        Assert.IsType<GitHubRequestException>(exceptionState.Exception);
        Assert.Contains(first, exceptionState.Exception.Message, StringComparison.Ordinal);
        Assert.Contains(second, exceptionState.Exception.Message, StringComparison.Ordinal);
    }

    [Then("the message of the exception lacks {string}")]
    public void ThenTheMessageLacks(string text) =>
        Assert.DoesNotContain(text, exceptionState.Exception!.Message, StringComparison.Ordinal);

    private async Task MintAsync(string repo, bool write)
    {
        exceptionState.Exception = null;
        try
        {
            _minted = await _tokens!.MintAsync(repo, write, TestContext.Current.CancellationToken);
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    private string[] Jwt(string scheme)
    {
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        string authorization = recorded.Headers["Authorization"];
        Assert.StartsWith(scheme + " ", authorization, StringComparison.Ordinal);
        return authorization[(scheme.Length + 1)..].Split('.');
    }

    private JsonElement Payload()
    {
        using var document = JsonDocument.Parse(Base64UrlDecode(Jwt("Bearer")[1]));
        return document.RootElement.Clone();
    }

    private static byte[] Base64UrlDecode(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);
        return Convert.FromBase64String(padded);
    }

    private sealed record RecordedRequest(string Method, string Uri, Dictionary<string, string> Headers, string? Body);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) Answer { get; set; } = (HttpStatusCode.OK, "");

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Dictionary<string, string> headers = [];
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.ToString(), headers, body));
            return new HttpResponseMessage(Answer.Status) { Content = new StringContent(Answer.Body, Encoding.UTF8, "application/json") };
        }
    }
}
