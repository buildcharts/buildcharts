using AutoFixture;
using BuildCharts.Tool.Plugins.NpmAuthenticate_v1;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Text.Json;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class NpmrcAuthenticatorTests : TestBase
{
    private Dictionary<string, string> _environment;
    private HttpClient _http;
    private List<string> _localRequests;
    private string _localCredentials;
    private Mock<HttpMessageHandler> HandlerMock => Fixture.Freeze<Mock<HttpMessageHandler>>();
    private NpmrcAuthenticator Sut => Fixture.Freeze<NpmrcAuthenticator>();

    [TestInitialize]
    public void TestInitialize()
    {
        _environment = new()
        {
            ["SYSTEM_ACCESSTOKEN"] = "job-token",
            ["SYSTEM_TEAMFOUNDATIONCOLLECTIONURI"] = "https://dev.azure.com/example/",
            ["SYSTEM_SERVERTYPE"] = "Hosted",
        };
        HandlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.AbsolutePath.Contains("resourceAreas")
                    ? """{"locationUrl":"https://pkgs.dev.azure.com/example/"}"""
                    : """{"locationServiceData":{"accessMappings":[{"accessPoint":"https://pkgs.dev.azure.com/example/"},{"accessPoint":"https://example.pkgs.visualstudio.com/"}]}}"""),
            });
        _http = new HttpClient(HandlerMock.Object);
        _localRequests = [];
        _localCredentials = "//pkgs.dev.azure.com/example/_packaging/feed/npm/registry/:_authToken=local-token\n//example.pkgs.visualstudio.com/_packaging/feed/npm/registry/:_authToken=legacy-token\n";
        Fixture.Register(() => _http);
        Fixture.Register<Func<string, CancellationToken, Task<string>>>(() => (configuration, _) =>
        {
            _localRequests.Add(configuration);
            return Task.FromResult(_localCredentials);
        });
        Fixture.Register<Func<string, string>>(() => name => _environment.GetValueOrDefault(name));
    }

    [TestCleanup]
    public void TestCleanup() => _http.Dispose();

    [TestMethod]
    public async Task AuthenticateAsync_ShouldAcquireLocalCredentialsForUnconfiguredAzureFeeds()
    {
        _environment.Clear();
        var source = "registry=https://registry.npmjs.org/\n@team:registry=https://pkgs.dev.azure.com/example/_packaging/feed/npm/registry/\n@legacy:registry=https://example.pkgs.visualstudio.com/_packaging/feed/npm/registry/\n//registry.other.example/:_authToken=keep-private\n";

        var result = await Sut.AuthenticateAsync(source, default);

        Assert.AreEqual(1, _localRequests.Count);
        StringAssert.Contains(_localRequests[0], "@team:registry=https://pkgs.dev.azure.com/example/_packaging/feed/npm/registry/");
        StringAssert.Contains(_localRequests[0], "@legacy:registry=https://example.pkgs.visualstudio.com/_packaging/feed/npm/registry/");
        Assert.IsFalse(_localRequests[0].Contains("keep-private", StringComparison.Ordinal));
        Assert.IsFalse(_localRequests[0].Contains("registry.npmjs.org", StringComparison.Ordinal));
        StringAssert.Contains(result, "_authToken=local-token");
        StringAssert.Contains(result, "_authToken=legacy-token");
        StringAssert.Contains(result, "_authToken=keep-private");
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldAuthenticateDiscoveredInternalRegistries()
    {
        var source = "registry=https://registry.npmjs.org/\n@team:registry=https://pkgs.dev.azure.com/example/_packaging/feed/npm/registry\n@old:registry=https://example.pkgs.visualstudio.com/_packaging/feed/npm/registry/\n";

        var result = await Sut.AuthenticateAsync(source, default);

        StringAssert.Contains(result, "//pkgs.dev.azure.com/example/_packaging/feed/npm/registry/:_authToken=job-token");
        StringAssert.Contains(result, "//example.pkgs.visualstudio.com/_packaging/feed/npm/registry/:_authToken=job-token");
        Assert.IsFalse(result.Contains("//registry.npmjs.org/:_authToken", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldEncodeAzureServiceConnectionTokensAsBasicCredentials()
    {
        _environment["NPM_AUTHENTICATE_CUSTOM_ENDPOINTS"] = "feed";
        _environment["ENDPOINT_URL_FEED"] = "https://example.pkgs.visualstudio.com/_packaging/feed/npm/registry";
        _environment["ENDPOINT_AUTH_FEED"] = """{"scheme":"Token","parameters":{"apitoken":"pat-value"}}""";
        HandlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(x => x.RequestUri.Host == "example.pkgs.visualstudio.com"), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                response.Headers.Add("X-TFS-Session", "session");
                return response;
            });

        var result = await Sut.AuthenticateAsync("registry=https://example.pkgs.visualstudio.com/_packaging/feed/npm/registry/", default);

        StringAssert.Contains(result, ":username=VssToken");
        StringAssert.Contains(result, ":_password=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("pat-value")));
        StringAssert.Contains(result, ":email=VssEmail");
        Assert.IsFalse(result.Contains("_authToken=", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldEncodeUsernamePasswordCredentials()
    {
        _environment["NPM_AUTHENTICATE_CUSTOM_ENDPOINTS"] = "external";
        _environment["ENDPOINT_URL_EXTERNAL"] = "https://registry.example/team/";
        _environment["ENDPOINT_AUTH_EXTERNAL"] = JsonSerializer.Serialize(new { scheme = "UsernamePassword", parameters = new { username = "builder", password = "pässword" } });

        var result = await Sut.AuthenticateAsync("@team:registry=https://registry.example/team", default);

        StringAssert.Contains(result, "//registry.example/team/:username=builder");
        StringAssert.Contains(result, "//registry.example/team/:_password=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("pässword")));
        StringAssert.Contains(result, "//registry.example/team/:email=builder");
        StringAssert.Contains(result, "//registry.example/team/:always-auth=true");
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldFailWhenLocationDiscoveryFails()
    {
        HandlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await Assert.ThrowsAsync<HttpRequestException>(() => Sut.AuthenticateAsync("registry=https://registry.npmjs.org/", default));

        Assert.AreEqual(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldKeepCredentialsScopedToExactRegistryUrls()
    {
        _environment["NPM_AUTHENTICATE_CUSTOM_ENDPOINTS"] = "external";
        _environment["ENDPOINT_URL_EXTERNAL"] = "https://registry.example/team/";
        _environment["ENDPOINT_AUTH_EXTERNAL"] = """{"scheme":"Token","parameters":{"apitoken":"external-token"}}""";

        var result = await Sut.AuthenticateAsync("registry=https://registry.example/team-other/\n@evil:registry=https://pkgs.dev.azure.com.evil.example/feed/", default);

        Assert.IsFalse(result.Contains("external-token", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("job-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldNormalizeQuotedRegistriesAndReplaceOnlyMatchingCredentials()
    {
        var source = "# comment\r\nregistry = 'https://pkgs.dev.azure.com/example/feed' ; registry comment\r\n//pkgs.dev.azure.com/example/feed/:username=old\r\n//pkgs.dev.azure.com/example/feed/:_password=old-password\r\n//other.example/:_authToken=keep\r\nstrict-ssl=true\r\n";

        var result = await Sut.AuthenticateAsync(source, default);

        StringAssert.Contains(result, "registry=https://pkgs.dev.azure.com/example/feed/\n");
        StringAssert.Contains(result, "//pkgs.dev.azure.com/example/feed/:_authToken=job-token");
        StringAssert.Contains(result, "//other.example/:_authToken=keep");
        StringAssert.Contains(result, "strict-ssl=true");
        Assert.IsFalse(result.Contains("old-password", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("username=old", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldPreservePublicRegistryConfigurationWithoutCredentials()
    {
        _environment.Clear();

        var result = await Sut.AuthenticateAsync("registry=https://registry.npmjs.org/\nstrict-ssl=true\n", default);

        Assert.AreEqual("registry=https://registry.npmjs.org/\nstrict-ssl=true\n", result);
        HandlerMock.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldRejectCredentialLineInjection()
    {
        _environment["SYSTEM_ACCESSTOKEN"] = "token\n//other.example/:_authToken=leak";

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.AuthenticateAsync("registry=https://pkgs.dev.azure.com/example/feed/", default));

        StringAssert.Contains(result.Message, "line break");
        Assert.IsFalse(result.Message.Contains("leak", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldRejectIncompleteLocalCredentials()
    {
        _environment.Clear();
        _localCredentials = "//other.example/:_authToken=wrong-feed\n";

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.AuthenticateAsync("registry=https://pkgs.dev.azure.com/example/_packaging/feed/npm/registry/", default));

        StringAssert.Contains(result.Message, "every requested Azure Artifacts feed");
    }

    [TestMethod]
    [DataRow("TF_BUILD", "True")]
    [DataRow("GITHUB_ACTIONS", "true")]
    [DataRow("CI", "true")]
    [DataRow("CI", "1")]
    public async Task AuthenticateAsync_ShouldRejectMissingAzureCredentialsInCi(string variable, string value)
    {
        _environment.Clear();
        _environment[variable] = value;

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.AuthenticateAsync("registry=https://pkgs.dev.azure.com/example/_packaging/feed/npm/registry/", default));

        StringAssert.Contains(result.Message, "credentials are missing in CI");
        Assert.AreEqual(0, _localRequests.Count);
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldRejectMixedSchemesForTheSameHost()
    {
        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.AuthenticateAsync("registry=https://registry.example/\n@other:registry=http://registry.example/", default));

        StringAssert.Contains(result.Message, "Do not mix HTTP and HTTPS");
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldRejectUnsupportedServiceConnectionAuthentication()
    {
        _environment["NPM_AUTHENTICATE_CUSTOM_ENDPOINTS"] = "external";
        _environment["ENDPOINT_URL_EXTERNAL"] = "https://registry.example/";
        _environment["ENDPOINT_AUTH_EXTERNAL"] = """{"scheme":"Certificate","parameters":{}}""";

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.AuthenticateAsync("registry=https://registry.example/", default));

        StringAssert.Contains(result.Message, "Use Token or UsernamePassword");
    }

    [TestMethod]
    [DataRow(":_authToken=existing-token\n")]
    [DataRow(":_auth=existing-basic\n")]
    [DataRow(":username=existing-user\n//pkgs.dev.azure.com/example/_packaging/feed/npm/registry/:_password=existing-password\n")]
    public async Task AuthenticateAsync_ShouldSkipLocalHelperForConfiguredAzureCredentials(string credentials)
    {
        _environment.Clear();
        var source = "registry=https://pkgs.dev.azure.com/example/_packaging/feed/npm/registry/\n//pkgs.dev.azure.com/example/_packaging/feed/npm/registry/" + credentials;

        var result = await Sut.AuthenticateAsync(source, default);

        Assert.AreEqual(0, _localRequests.Count);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    [DataRow(null, "provider-token")]
    [DataRow("", "provider-token")]
    [DataRow("job-token", "job-token")]
    public async Task AuthenticateAsync_ShouldUseCredentialProviderTokenAsFallback(string jobToken, string expectedToken)
    {
        _environment["SYSTEM_ACCESSTOKEN"] = jobToken;
        _environment["ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN"] = "provider-token";
        var source = "registry=https://pkgs.dev.azure.com/example/_packaging/feed/npm/registry/\n";

        var result = await Sut.AuthenticateAsync(source, default);

        StringAssert.Contains(result, $":_authToken={expectedToken}");
        Assert.AreEqual(0, _localRequests.Count);
    }
    [TestMethod]
    public async Task AuthenticateAsync_ShouldUseServiceConnectionsBeforeJobCredentials()
    {
        _environment["NPM_AUTHENTICATE_CUSTOM_ENDPOINTS"] = "external";
        _environment["ENDPOINT_URL_EXTERNAL"] = "https://pkgs.dev.azure.com/example/feed/";
        _environment["ENDPOINT_AUTH_EXTERNAL"] = """{"scheme":"UsernamePassword","parameters":{"username":"service","password":"service-password"}}""";

        var result = await Sut.AuthenticateAsync("registry=https://pkgs.dev.azure.com/example/feed/\n@team:registry=https://pkgs.dev.azure.com/example/feed/", default);

        StringAssert.Contains(result, ":username=service");
        Assert.IsFalse(result.Contains("job-token", StringComparison.Ordinal));
        Assert.AreEqual(1, result.Split(":_password=").Length - 1);
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldUseExternalTokensWithoutSendingThemToTheProbe()
    {
        _environment["NPM_AUTHENTICATE_CUSTOM_ENDPOINTS"] = "external";
        _environment["ENDPOINT_URL_EXTERNAL"] = "https://registry.example/";
        _environment["ENDPOINT_AUTH_EXTERNAL"] = """{"scheme":"Token","parameters":{"apitoken":"external-token"}}""";

        var result = await Sut.AuthenticateAsync("registry=https://registry.example/", default);

        StringAssert.Contains(result, "//registry.example/:_authToken=external-token");
        HandlerMock.Protected().Verify("SendAsync", Times.Once(), ItExpr.Is<HttpRequestMessage>(x => x.RequestUri.Host == "registry.example" && x.Headers.Authorization == null && x.Headers.Contains("X-TFS-FedAuthRedirect")), ItExpr.IsAny<CancellationToken>());
    }

    [TestMethod]
    public async Task AuthenticateAsync_ShouldUseSystemVssConnectionForServerRegistries()
    {
        _environment.Remove("SYSTEM_ACCESSTOKEN");
        _environment["SYSTEM_TEAMFOUNDATIONCOLLECTIONURI"] = "https://server.example/collection/";
        _environment["SYSTEM_SERVERTYPE"] = "OnPremises";
        _environment["ENDPOINT_AUTH_SYSTEMVSSCONNECTION"] = """{"scheme":"OAuth","parameters":{"AccessToken":"connection-token"}}""";

        var result = await Sut.AuthenticateAsync("registry=https://server.example/collection/_packaging/feed/npm/registry/", default);

        StringAssert.Contains(result, ":_authToken=connection-token");
        HandlerMock.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }
}