using AutoFixture;
using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Plugins;
using BuildCharts.Tool.Plugins.NpmAuthenticate_v1;
using BuildCharts.Tool.Plugins.NuGetAuthenticate_v1;
using System.Text;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class NpmAuthenticatePluginTests : TestBase
{
    private string _directory;
    private string _originalDirectory;
    private Dictionary<string, string> _environment;
    private NpmAuthenticatePlugin Sut => Fixture.Freeze<NpmAuthenticatePlugin>();

    [TestInitialize]
    public void TestInitialize()
    {
        _originalDirectory = Directory.GetCurrentDirectory();
        _directory = Path.Combine(Path.GetTempPath(), "buildcharts-npm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Directory.SetCurrentDirectory(_directory);
        _environment = new[] { "NPM_AUTHENTICATE_WORKING_FILE", "SYSTEM_ACCESSTOKEN", "ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN", "SYSTEM_COLLECTIONURI", "SYSTEM_TEAMFOUNDATIONCOLLECTIONURI", "SYSTEM_SERVERTYPE", "NPM_AUTHENTICATE_CUSTOM_ENDPOINTS", "ENDPOINT_AUTH_SYSTEMVSSCONNECTION", "TF_BUILD", "GITHUB_ACTIONS", "CI" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
        foreach (var name in _environment.Keys)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        Environment.SetEnvironmentVariable("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI", "https://server.example/collection/");
        Environment.SetEnvironmentVariable("SYSTEM_ACCESSTOKEN", "job-token");
        File.WriteAllText(".npmrc", "registry=https://server.example/collection/feed/\r\n", new UTF8Encoding(true));
    }

    [TestCleanup]
    public void TestCleanup()
    {
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
        var hcl = new StringBuilder("target \"_common\" {\n  args = {\n    VALUE = \"} ${EXAMPLE}\"\n  }\n}\n");
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
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldLeaveSourceBytesUnchanged()
    {
        var original = await File.ReadAllBytesAsync(".npmrc");

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await File.ReadAllTextAsync(".buildcharts/secrets/npmrc");

        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(".npmrc"));
        Assert.IsFalse(Directory.Exists(".buildcharts/plugins"));
        StringAssert.Contains(result, "//server.example/collection/feed/:_authToken=job-token");
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldRemoveStaleCredentialsWhenAuthenticationFails()
    {
        Directory.CreateDirectory(".buildcharts/secrets");
        await File.WriteAllTextAsync(".buildcharts/secrets/npmrc", "stale-token");
        Environment.SetEnvironmentVariable("SYSTEM_ACCESSTOKEN", null);

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.OnBeforeGenerateAsync(new BuildConfig(), default));

        StringAssert.Contains(result.Message, "credential is missing");
        Assert.IsFalse(File.Exists(".buildcharts/secrets/npmrc"));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldSupportAnExplicitWorkingFile()
    {
        File.Move(".npmrc", "packages.npmrc");
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_WORKING_FILE", "packages.npmrc");

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);
        var result = await File.ReadAllTextAsync(".buildcharts/secrets/npmrc");

        StringAssert.Contains(result, "_authToken=job-token");
        Assert.IsTrue(File.Exists("packages.npmrc"));
    }
}