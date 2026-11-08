using AutoFixture;
using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Plugins;
using BuildCharts.Tool.Plugins.NpmAuthenticate_v1;
using BuildCharts.Tool.Plugins.NuGetAuthenticate_v1;
using System.Text;
using System.Text.Json;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class NpmAuthenticatePluginTests : TestBase
{
    private const string REGISTRY = "https://pkgs.dev.azure.com/example/_packaging/feed/npm/registry/";
    private const string PREFIX = "//pkgs.dev.azure.com/example/_packaging/feed/npm/registry/:";
    private string _directory;
    private string _originalDirectory;
    private Dictionary<string, string> _environment;
    private TextWriter _originalOutput;
    private TextWriter _originalError;
    private StringWriter _output;
    private StringWriter _error;
    private NpmAuthenticatePlugin Sut => Fixture.Freeze<NpmAuthenticatePlugin>();

    [TestInitialize]
    public void TestInitialize()
    {
        Fixture.Customize<NpmAuthenticatePlugin>(composer => composer.OmitAutoProperties());
        _originalDirectory = Directory.GetCurrentDirectory();
        _directory = Path.Combine(Path.GetTempPath(), "buildcharts-npm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Directory.SetCurrentDirectory(_directory);
        _environment = new[] { "NPM_AUTHENTICATE_WORKING_FILE", "ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", "ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN", "VSS_NUGET_ACCESSTOKEN", "SYSTEM_ACCESSTOKEN", "AZURE_ARTIFACTS_ENV_ACCESS_TOKEN", "NPM_AUTHENTICATE_HELPER_VERSION", "TF_BUILD", "GITHUB_ACTIONS", "CI" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
        foreach (var name in _environment.Keys)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        Environment.SetEnvironmentVariable("SYSTEM_ACCESSTOKEN", "job-token");
        File.WriteAllText(".npmrc", $"registry={REGISTRY}\r\n", new UTF8Encoding(true));
        _originalOutput = Console.Out;
        _originalError = Console.Error;
        _output = new StringWriter();
        _error = new StringWriter();
        Console.SetOut(_output);
        Console.SetError(_error);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Console.SetOut(_originalOutput);
        Console.SetError(_originalError);
        _output.Dispose();
        _error.Dispose();
        Directory.SetCurrentDirectory(_originalDirectory);
        foreach (var entry in _environment)
        {
            Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    public void LoadPlugins_ShouldRecognizeNpmAuthenticate()
    {
        var result = PluginManager.LoadPlugins(["NpmAuthenticate@v1", "npmauthenticate@v1"]);

        Assert.AreEqual(1, result.Count);
        Assert.IsInstanceOfType<NpmAuthenticatePlugin>(result[0]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OnAfterGenerateAsync_ShouldComposeWithNuGetSecrets(bool npmFirst)
    {
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var prepared = await File.ReadAllBytesAsync(".buildcharts/secrets/npmrc");
        var hcl = new StringBuilder("target \"_common\" {\n  args = {\n    VALUE = \"} example\"\n  }\n}\n");
        IBuildChartsPlugin[] plugins = npmFirst ? [Sut, new NuGetAuthenticatePlugin()] : [new NuGetAuthenticatePlugin(), Sut];

        foreach (var plugin in plugins)
        {
            await plugin.OnAfterGenerateAsync(new BuildConfig(), new ChartConfig(), hcl, default);
        }
        await Sut.OnAfterGenerateAsync(new BuildConfig(), new ChartConfig(), hcl, default);
        var result = hcl.ToString();

        Assert.AreEqual(1, result.Split("secret =").Length - 1);
        Assert.AreEqual(1, result.Split("id=npmrc,").Length - 1);
        StringAssert.Contains(result, "id=ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS,");
        StringAssert.Contains(result, "id=ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN,");
        Assert.IsFalse(result.Contains("job-token", StringComparison.Ordinal));
        CollectionAssert.AreEqual(prepared, await File.ReadAllBytesAsync(".buildcharts/secrets/npmrc"));
    }

    [TestMethod]
    [DataRow(".npmrc", "registry=https://registry.npmjs.org/\r\n")]
    [DataRow("packages.npmrc", "; comment\r\nstrict-ssl=true\r\n")]
    public async Task OnAfterGenerateAsync_ShouldCopyOriginalConfig(string workingFile, string content)
    {
        File.WriteAllText(workingFile, content, new UTF8Encoding(true));
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_WORKING_FILE", workingFile);
        var original = await File.ReadAllBytesAsync(workingFile);
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var hcl = new StringBuilder("target \"_common\" {}\n");

        await Sut.OnAfterGenerateAsync(new BuildConfig(), new ChartConfig(), hcl, default);
        var result = await File.ReadAllBytesAsync(".buildcharts/secrets/npmrc");

        CollectionAssert.AreEqual(original, result);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(workingFile));
        StringAssert.Contains(hcl.ToString(), "id=npmrc,src=.buildcharts/secrets/npmrc");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OnBeforeGenerateAsync_ShouldAllowPartialEndpointCredentials(bool matchingEndpoint)
    {
        const string otherRegistry = "https://pkgs.dev.azure.com/example/_packaging/other/npm/registry/";
        File.WriteAllText(".npmrc", $"registry={REGISTRY}\n@other:registry={otherRegistry}\n");
        var endpoint = matchingEndpoint ? REGISTRY : "https://pkgs.dev.azure.com/example/_packaging/unrelated/npm/registry/";
        Environment.SetEnvironmentVariable("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", Endpoints(endpoint, "endpoint-token"));

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        StringAssert.Contains(result, $"registry={REGISTRY}\n@other:registry={otherRegistry}\n");
        Assert.AreEqual(matchingEndpoint ? 2 : 0, result.Split(":_password=").Length - 1);
        Assert.IsFalse(result.Contains("//pkgs.dev.azure.com/example/_packaging/other/npm/registry/:username=", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("job-token")), StringComparison.Ordinal));
        Assert.IsFalse(Directory.Exists(".buildcharts/plugins"));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldKeepCredentialsScopedToMatchingEndpoints()
    {
        const string source = "registry=https://pkgs.dev.azure.com/example/_packaging/feed-other/npm/registry/\n";
        File.WriteAllText(".npmrc", source);
        Environment.SetEnvironmentVariable("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", Endpoints(REGISTRY, "private-password"));

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldKeepTokensOffPublicRegistries()
    {
        File.WriteAllText(".npmrc", $"registry=https://registry.npmjs.org/\n@evil:registry=https://pkgs.dev.azure.com.evil.example/feed/\n@team:registry={REGISTRY}\n");

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        StringAssert.Contains(result, PREFIX + "_password=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("job-token")));
        Assert.IsFalse(result.Contains("//registry.npmjs.org/:username=", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("//pkgs.dev.azure.com.evil.example/feed/:username=", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldLeaveSourceBytesUnchanged()
    {
        var original = await File.ReadAllBytesAsync(".npmrc");

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(".npmrc"));
        Assert.IsFalse(Directory.Exists(".buildcharts/plugins"));
        StringAssert.Contains(result, PREFIX + "_password=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("job-token")));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldNormalizeRegistriesAndReplaceMatchingCredentials()
    {
        File.WriteAllText(".npmrc", $"# comment\r\nregistry = '{REGISTRY.TrimEnd('/')}' ; registry comment\r\n{PREFIX}username=old\r\n{PREFIX}_password=old-password\r\n//other.example/:_authToken=keep\r\nstrict-ssl=true\r\n");

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        StringAssert.Contains(result, $"registry={REGISTRY}\n");
        StringAssert.Contains(result, "//other.example/:_authToken=keep");
        StringAssert.Contains(result, "strict-ssl=true");
        Assert.IsFalse(result.Contains("old-password", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("username=old", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldPreferEndpointCredentialsOverTokens()
    {
        Environment.SetEnvironmentVariable("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", Endpoints(REGISTRY, "endpoint-password", "service"));
        Environment.SetEnvironmentVariable("VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", Endpoints(REGISTRY, "legacy-password"));

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        StringAssert.Contains(result, PREFIX + "username=service");
        StringAssert.Contains(result, PREFIX + "_password=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("endpoint-password")));
        Assert.IsFalse(result.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("job-token")), StringComparison.Ordinal));
        Assert.IsFalse(result.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("legacy-password")), StringComparison.Ordinal));
        StringAssert.Contains(_output.ToString(), "Using ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS from environment");
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldPreferTokenVariablesInNuGetOrder()
    {
        Environment.SetEnvironmentVariable("ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN", "preferred-token");
        Environment.SetEnvironmentVariable("VSS_NUGET_ACCESSTOKEN", "legacy-token");
        Environment.SetEnvironmentVariable("AZURE_ARTIFACTS_ENV_ACCESS_TOKEN", "azure-token");

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        StringAssert.Contains(result, PREFIX + "_password=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("preferred-token")));
        StringAssert.Contains(_output.ToString(), "Using ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN from environment");
        Assert.IsFalse(_output.ToString().Contains("preferred-token", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("token\n//other.example/:_authToken=leak")]
    public async Task OnBeforeGenerateAsync_ShouldRejectInvalidEndpointCredentials(string password)
    {
        Environment.SetEnvironmentVariable("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", Endpoints(REGISTRY, password));

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.OnBeforeGenerateAsync(new BuildConfig(), default));

        StringAssert.Contains(result.Message, "credential is missing or contains a line break");
        Assert.IsFalse(File.Exists(".buildcharts/secrets/npmrc"));
        Assert.IsFalse(_error.ToString().Contains("leak", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldRejectMixedSchemesForTheSameHost()
    {
        File.WriteAllText(".npmrc", "registry=https://registry.example/\n@other:registry=http://registry.example/\n");

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.OnBeforeGenerateAsync(new BuildConfig(), default));

        StringAssert.Contains(result.Message, "Do not mix HTTP and HTTPS");
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldRejectTokenLineInjection()
    {
        Environment.SetEnvironmentVariable("SYSTEM_ACCESSTOKEN", "token\n//other.example/:_authToken=leak");

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.OnBeforeGenerateAsync(new BuildConfig(), default));

        StringAssert.Contains(result.Message, "line break");
        Assert.IsFalse(result.Message.Contains("leak", StringComparison.Ordinal));
        Assert.IsFalse(_error.ToString().Contains("leak", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(":_authToken=existing-token\n")]
    [DataRow(":_auth=existing-basic\n")]
    [DataRow(":username=existing-user\n//pkgs.dev.azure.com/example/_packaging/feed/npm/registry/:_password=existing-password\n")]
    public async Task OnBeforeGenerateAsync_ShouldSkipProviderForExistingCredentials(string credentials)
    {
        Environment.SetEnvironmentVariable("SYSTEM_ACCESSTOKEN", null);
        var source = $"registry={REGISTRY}\n{PREFIX.TrimEnd(':')}{credentials}";
        File.WriteAllText(".npmrc", source);

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        Assert.AreEqual(source, result);
        Assert.IsFalse(Directory.Exists(".buildcharts/plugins"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("# comment\nstrict-ssl=true\n")]
    [DataRow("registry=https://registry.npmjs.org/\nstrict-ssl=true\n")]
    public async Task OnBeforeGenerateAsync_ShouldSkip_WhenNoAzureSources(string source)
    {
        await File.WriteAllTextAsync(".npmrc", source);
        Environment.SetEnvironmentVariable("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS", "invalid-json");

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = _output.ToString();

        Assert.AreEqual(source, await File.ReadAllTextAsync(".npmrc"));
        Assert.IsFalse(Directory.Exists(".buildcharts"));
        StringAssert.Contains(result, "No Azure DevOps package sources found in npm config");
        Assert.IsFalse(result.Contains("Found feed for", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(".npmrc")]
    [DataRow("packages.npmrc")]
    public async Task OnBeforeGenerateAsync_ShouldSkip_WhenNpmrcIsMissing(string workingFile)
    {
        File.Delete(".npmrc");
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_WORKING_FILE", workingFile);
        const string original = "target \"_common\" {}\n";
        var hcl = new StringBuilder(original);

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        await Sut.OnAfterGenerateAsync(new BuildConfig(), new ChartConfig(), hcl, default);
        var result = hcl.ToString();

        Assert.AreEqual(original, result);
        Assert.IsFalse(Directory.Exists(".buildcharts"));
        StringAssert.Contains(_output.ToString(), "No Azure DevOps package sources found in npm config");
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldSupportAnExplicitWorkingFile()
    {
        File.Move(".npmrc", "packages.npmrc");
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_WORKING_FILE", "packages.npmrc");

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        StringAssert.Contains(result, PREFIX + "username=buildcharts");
        Assert.IsTrue(File.Exists("packages.npmrc"));
    }

    [TestMethod]
    [DataRow("ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS")]
    [DataRow("VSS_NUGET_EXTERNAL_FEED_ENDPOINTS")]
    public async Task OnBeforeGenerateAsync_ShouldUseEndpointEnvironmentVariables(string variable)
    {
        Environment.SetEnvironmentVariable(variable, Endpoints(REGISTRY.TrimEnd('/'), "endpoint-token"));

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        StringAssert.Contains(result, PREFIX + "_password=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("endpoint-token")));
        StringAssert.Contains(_output.ToString(), $"Using {variable} from environment");
        Assert.IsFalse(Directory.Exists(".buildcharts/plugins"));
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow("TF_BUILD", "true")]
    [DataRow("GITHUB_ACTIONS", "true")]
    [DataRow("CI", "true")]
    [DataRow("CI", "1")]
    public async Task OnBeforeGenerateAsync_ShouldUseProviderWhenEnvironmentCredentialsAreMissing(string variable, string value)
    {
        Environment.SetEnvironmentVariable("SYSTEM_ACCESSTOKEN", null);
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION", "invalid");
        if (variable != null)
        {
            Environment.SetEnvironmentVariable(variable, value);
        }

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.OnBeforeGenerateAsync(new BuildConfig(), default));

        StringAssert.Contains(result.Message, "exact Azure Artifacts Credential Provider release version");
        Assert.IsFalse(File.Exists(".buildcharts/secrets/npmrc"));
    }

    [TestMethod]
    [DataRow("ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN")]
    [DataRow("VSS_NUGET_ACCESSTOKEN")]
    [DataRow("SYSTEM_ACCESSTOKEN")]
    [DataRow("AZURE_ARTIFACTS_ENV_ACCESS_TOKEN")]
    public async Task OnBeforeGenerateAsync_ShouldUseTokenEnvironmentVariables(string variable)
    {
        Environment.SetEnvironmentVariable("SYSTEM_ACCESSTOKEN", null);
        Environment.SetEnvironmentVariable(variable, "environment-token");

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        StringAssert.Contains(result, PREFIX + "_password=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("environment-token")));
        StringAssert.Contains(_output.ToString(), $"Using {variable} from environment");
        Assert.IsFalse(_output.ToString().Contains("environment-token", StringComparison.Ordinal));
        Assert.IsFalse(Directory.Exists(".buildcharts/plugins"));
    }

    [TestMethod]
    [DataRow("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry")]
    [DataRow("https://pkgs.dev.azure.com/org/project/_packaging/feed/npm/registry/")]
    [DataRow("https://org.pkgs.visualstudio.com/_packaging/feed/npm/registry/")]
    public async Task OnBeforeGenerateAsync_ShouldWriteBothNpmAuthenticationPaths(string url)
    {
        File.WriteAllText(".npmrc", $"registry={url}\n@team:registry={url}\n");
        var prefix = "//" + new Uri(url).Authority + new Uri(url).AbsolutePath.TrimEnd('/') + "/";

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await ReadSecretAsync();

        foreach (var path in new[] { prefix, prefix[..^"registry/".Length] })
        {
            StringAssert.Contains(result, $"{path}:username=buildcharts");
            StringAssert.Contains(result, $"{path}:_password={Convert.ToBase64String(Encoding.UTF8.GetBytes("job-token"))}");
        }
        Assert.AreEqual(2, result.Split(":_password=").Length - 1);
        Assert.IsFalse(result.Contains("job-token", StringComparison.Ordinal));
    }

    private static string Endpoints(string endpoint, string password, string username = "buildcharts") => JsonSerializer.Serialize(new { endpointCredentials = new[] { new { endpoint, username, password } } });

    private static Task<string> ReadSecretAsync() => File.ReadAllTextAsync(".buildcharts/secrets/npmrc");
}
