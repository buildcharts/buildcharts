using BuildCharts.Tool.Plugins.Helpers;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class AzureArtifactsCredentialProviderTests : TestBase
{
    private const string VERSION_VARIABLE = "NUGET_AUTHENTICATE_HELPER_VERSION";
    private string _directory;
    private string _originalVersion;

    [TestInitialize]
    public void TestInitialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "buildcharts-provider-tests", Guid.NewGuid().ToString("N"));
        _originalVersion = Environment.GetEnvironmentVariable(VERSION_VARIABLE);
        Environment.SetEnvironmentVariable(VERSION_VARIABLE, null);
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Environment.SetEnvironmentVariable(VERSION_VARIABLE, _originalVersion);
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    [DataRow("latest")]
    [DataRow("../1.4.1")]
    [DataRow("1.4")]
    public async Task EnsureInstalledAsync_ShouldRejectInvalidVersion(string version)
    {
        Environment.SetEnvironmentVariable(VERSION_VARIABLE, version);

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => AzureArtifactsCredentialProvider.EnsureInstalledAsync(_directory, default, version));

        StringAssert.Contains(result.Message, "exact Azure Artifacts Credential Provider release version");
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_directory).Length);
    }

    [TestMethod]
    [DataRow(null, "latest")]
    [DataRow("", "latest")]
    [DataRow(" ", "latest")]
    [DataRow("1.4.1", "1.4.1")]
    [DataRow("v1.4.1", "1.4.1")]
    [DataRow(" 1.4.1 ", "1.4.1")]
    [DataRow("1.4.1-beta.1", "1.4.1-beta.1")]
    public async Task EnsureInstalledAsync_ShouldReuseSelectedVersion(string version, string directory)
    {
        Environment.SetEnvironmentVariable(VERSION_VARIABLE, version);
        var selectedFolder = Path.Combine(_directory, directory);
        Directory.CreateDirectory(selectedFolder);
        var provider = Path.Combine(selectedFolder, "CredentialProvider.Microsoft.dll");
        await File.WriteAllTextAsync(provider, "cached-provider");

        Environment.SetEnvironmentVariable(VERSION_VARIABLE, "invalid");
        var result = await AzureArtifactsCredentialProvider.EnsureInstalledAsync(_directory, default, version);

        Assert.AreEqual(selectedFolder, result);
        Assert.AreEqual("cached-provider", await File.ReadAllTextAsync(provider));
    }
}
