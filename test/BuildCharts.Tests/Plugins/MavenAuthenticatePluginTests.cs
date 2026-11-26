using AutoFixture;
using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Plugins;
using BuildCharts.Tool.Plugins.MavenAuthenticate_v1;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class MavenAuthenticatePluginTests : TestBase
{
    private const string TOKEN_VARIABLE = "SYSTEM_ACCESSTOKEN";
    private const string ENDPOINTS_VARIABLE = "ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS";
    private const string PROVIDER_TOKEN_VARIABLE = "ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN";
    private const string WORKING_FILE_VARIABLE = "MAVEN_AUTHENTICATE_WORKING_FILE";
    private const string POM_FILE_VARIABLE = "MAVEN_AUTHENTICATE_POM_FILE";
    private const string HELPER_VERSION_VARIABLE = "MAVEN_AUTHENTICATE_HELPER_VERSION";
    private const string FEED_ID = "HSB.IDP.Artifact";
    private const string FEED_URL = "https://pkgs.dev.azure.com/example/project/_packaging/HSB.IDP.Artifact/maven/v1";
    private const string HTTP_FEED_URL = "http://pkgs.dev.azure.com/example/project/_packaging/HSB.IDP.Artifact/maven/v1";
    private const string LEGACY_FEED_URL = "https://hsbdev.pkgs.visualstudio.com/_packaging/HSB.IDP.Artifact/maven/v1";
    private const string JOB_TOKEN = "job-token<&";
    private const string SECRET_PATH = ".buildcharts/secrets/settings.xml";

    private string _directory;
    private string _originalDirectory;
    private Dictionary<string, string> _environment;
    private MavenAuthenticatePlugin Sut => Fixture.Freeze<MavenAuthenticatePlugin>();

    [TestInitialize]
    public void TestInitialize()
    {
        Fixture.Customize<MavenAuthenticatePlugin>(composer => composer.OmitAutoProperties());
        _originalDirectory = Directory.GetCurrentDirectory();
        _directory = Path.Combine(Path.GetTempPath(), "buildcharts-maven-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Directory.SetCurrentDirectory(_directory);
        _environment = new[]
        {
            TOKEN_VARIABLE, ENDPOINTS_VARIABLE, PROVIDER_TOKEN_VARIABLE, WORKING_FILE_VARIABLE, POM_FILE_VARIABLE,
            HELPER_VERSION_VARIABLE, "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", "VSS_NUGET_ACCESSTOKEN", "AZURE_ARTIFACTS_ENV_ACCESS_TOKEN",
        }.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        foreach (var name in _environment.Keys)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Environment.SetEnvironmentVariable(TOKEN_VARIABLE, JOB_TOKEN);
        File.WriteAllText("pom.xml", $"""
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <repositories>
                <repository><id>{FEED_ID}</id><url>{FEED_URL}</url></repository>
              </repositories>
            </project>
            """);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Directory.SetCurrentDirectory(_originalDirectory);
        foreach (var (name, value) in _environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        Directory.Delete(_directory, true);
    }

    [TestMethod]
    public void LoadPlugins_ShouldRecognizeMavenAuthenticate()
    {
        // Act
        var result = PluginManager.LoadPlugins(["MavenAuthenticate@v1"]);

        // Assert
        Assert.IsInstanceOfType<MavenAuthenticatePlugin>(result.Single());
    }

    [TestMethod]
    public async Task OnAfterGenerateAsync_ShouldAddSettingsSecret()
    {
        // Arrange
        var hcl = new StringBuilder("target \"_common\" {\n  secret = [\"type=file,id=npmrc,src=.buildcharts/secrets/npmrc\"]\n}\n");
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Act
        await Sut.OnAfterGenerateAsync(new BuildConfig(), new ChartConfig(), hcl, default);

        // Assert
        var result = hcl.ToString();
        StringAssert.Contains(result, "type=file,id=settings.xml,src=.buildcharts/secrets/settings.xml");
        StringAssert.Contains(result, "type=file,id=npmrc,src=.buildcharts/secrets/npmrc");
        Assert.IsFalse(result.Contains(JOB_TOKEN, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(null, ".mvn/settings.xml")]
    [DataRow("custom/settings.xml", "custom/settings.xml")]
    public async Task OnAfterGenerateAsync_ShouldCopyWorkingFile(string workingFile, string settingsPath)
    {
        // Arrange
        const string settings = "<settings>\r\n</settings>\r\n";
        var hcl = new StringBuilder("target \"_common\" {}\n");
        File.Delete("pom.xml");
        Environment.SetEnvironmentVariable(WORKING_FILE_VARIABLE, workingFile);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
        await File.WriteAllTextAsync(settingsPath, settings);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(settingsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Act
        await Sut.OnAfterGenerateAsync(new BuildConfig(), new ChartConfig(), hcl, default);

        // Assert
        var result = await File.ReadAllTextAsync(SECRET_PATH);
        Assert.AreEqual(settings, result);
        StringAssert.Contains(hcl.ToString(), "type=file,id=settings.xml,src=.buildcharts/secrets/settings.xml");
        if (!OperatingSystem.IsWindows())
        {
            Assert.AreEqual(File.GetUnixFileMode(settingsPath), File.GetUnixFileMode(SECRET_PATH));
        }
    }

    [TestMethod]
    public async Task OnAfterGenerateAsync_ShouldSkipSecretWithoutSettings()
    {
        // Arrange
        const string original = "target \"_common\" {}\n";
        var hcl = new StringBuilder(original);
        File.Delete("pom.xml");
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Act
        await Sut.OnAfterGenerateAsync(new BuildConfig(), new ChartConfig(), hcl, default);

        // Assert
        var result = hcl.ToString();
        Assert.AreEqual(original, result);
        Assert.IsFalse(File.Exists(SECRET_PATH));
    }

    [TestMethod]
    [DataRow(null, "pom.xml", FEED_URL)]
    [DataRow("custom-pom.xml", "custom-pom.xml", FEED_URL)]
    [DataRow(null, "pom.xml", LEGACY_FEED_URL)]
    [DataRow(null, "pom.xml", "https://hsbdev.pkgs.visualstudio.com/project/_packaging/HSB.IDP.Artifact/maven/v1")]
    public async Task OnBeforeGenerateAsync_ShouldDiscoverPomRepositories(string pomFile, string pomPath, string feedUrl)
    {
        // Arrange
        Environment.SetEnvironmentVariable(POM_FILE_VARIABLE, pomFile);
        await File.WriteAllTextAsync(pomPath, $"""
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <repositories>
                <repository><id>{FEED_ID}</id><url>{feedUrl}</url></repository>
                <repository><id>central</id><url>https://repo.maven.apache.org/maven2</url></repository>
              </repositories>
              <pluginRepositories>
                <pluginRepository>
                  <id>plugins</id><url>https://pkgs.dev.azure.com/example/_packaging/plugins/maven/v1</url>
                </pluginRepository>
              </pluginRepositories>
              <distributionManagement>
                <repository><id>releases</id><url>{feedUrl}/</url></repository>
                <snapshotRepository>
                  <id>snapshots</id><url>https://pkgs.dev.azure.com/example/project/_packaging/snapshots/maven/v1</url>
                </snapshotRepository>
              </distributionManagement>
            </project>
            """);

        // Act
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Assert
        var result = XDocument.Load(SECRET_PATH).Root;
        var ns = result.Name.Namespace;
        var server = result.Element(ns + "servers").Elements(ns + "server").Single();
        Assert.AreEqual(FEED_ID, (string)server.Element(ns + "id"));
        Assert.AreEqual(JOB_TOKEN, (string)server.Element(ns + "password"));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldKeepExistingCredentialsWithoutEnvironmentCredentials()
    {
        // Arrange
        const string settings = """
            <settings><servers>
              <server><id>HSB.IDP.Artifact</id><username>existing-user</username><password>existing-token</password></server>
            </servers></settings>
            """;
        Environment.SetEnvironmentVariable(TOKEN_VARIABLE, null);
        Environment.SetEnvironmentVariable(HELPER_VERSION_VARIABLE, "invalid");
        Directory.CreateDirectory(".mvn");
        await File.WriteAllTextAsync(".mvn/settings.xml", settings);

        // Act
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Assert
        var result = XDocument.Load(SECRET_PATH).Root.Element("servers").Element("server");
        Assert.AreEqual("existing-user", (string)result.Element("username"));
        Assert.AreEqual("existing-token", (string)result.Element("password"));
        Assert.IsFalse(Directory.Exists(".buildcharts/plugins"));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldMergeSettings()
    {
        // Arrange
        const string template = """
            <settings xmlns="http://maven.apache.org/SETTINGS/1.2.0">
              <mirrors><mirror><id>existing-mirror</id></mirror></mirrors>
              <servers>
                <server><id>HSB.IDP.Artifact</id><username>old-user</username><configuration /></server>
                <server><id>existing-server</id><username>existing-user</username></server>
              </servers>
            </settings>
            """;
        Directory.CreateDirectory(".mvn");
        await File.WriteAllTextAsync(".mvn/settings.xml", template);

        // Act
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Assert
        var result = XDocument.Load(SECRET_PATH).Root;
        XNamespace ns = "http://maven.apache.org/SETTINGS/1.2.0";
        var servers = result.Element(ns + "servers").Elements(ns + "server").ToDictionary(server => (string)server.Element(ns + "id"));
        Assert.AreEqual("AzureDevOps", (string)servers[FEED_ID].Element(ns + "username"));
        Assert.AreEqual(JOB_TOKEN, (string)servers[FEED_ID].Element(ns + "password"));
        Assert.IsNotNull(servers[FEED_ID].Element(ns + "configuration"));
        Assert.AreEqual("existing-user", (string)servers["existing-server"].Element(ns + "username"));
        Assert.IsNotNull(result.Element(ns + "mirrors"));
        Assert.AreEqual(template, await File.ReadAllTextAsync(".mvn/settings.xml"));
    }

    [TestMethod]
    [DataRow(FEED_URL)]
    [DataRow(LEGACY_FEED_URL)]
    public async Task OnBeforeGenerateAsync_ShouldPreferEndpointCredentialsForMatchingFeeds(string feedUrl)
    {
        // Arrange
        await File.WriteAllTextAsync("pom.xml", $"""
            <project><repositories>
              <repository><id>{FEED_ID}</id><url>{feedUrl}</url></repository>
              <repository><id>unmatched</id><url>https://pkgs.dev.azure.com/example/project/_packaging/unmatched/maven/v1</url></repository>
            </repositories></project>
            """);
        Environment.SetEnvironmentVariable(ENDPOINTS_VARIABLE, JsonSerializer.Serialize(new
        {
            endpointCredentials = new[] { new { endpoint = feedUrl + "/", username = "feed-user", password = "feed-token" } },
        }));

        // Act
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Assert
        var result = XDocument.Load(SECRET_PATH).Root;
        var ns = result.Name.Namespace;
        var server = result.Element(ns + "servers").Elements(ns + "server").Single();
        Assert.AreEqual(FEED_ID, (string)server.Element(ns + "id"));
        Assert.AreEqual("feed-user", (string)server.Element(ns + "username"));
        Assert.AreEqual("feed-token", (string)server.Element(ns + "password"));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldPreferProviderTokenOverJobToken()
    {
        // Arrange
        const string providerToken = "provider-token";
        Environment.SetEnvironmentVariable(PROVIDER_TOKEN_VARIABLE, providerToken);

        // Act
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Assert
        var result = XDocument.Load(SECRET_PATH).Root;
        var ns = result.Name.Namespace;
        var server = result.Element(ns + "servers").Element(ns + "server");
        Assert.AreEqual(providerToken, (string)server.Element(ns + "password"));
    }

    [TestMethod]
    [DataRow("${feed.url}")]
    [DataRow("ftp://pkgs.dev.azure.com/example/_packaging/feed/maven/v1")]
    [DataRow("https://user@pkgs.dev.azure.com/example/_packaging/feed/maven/v1")]
    public async Task OnBeforeGenerateAsync_ShouldRejectInvalidRepositoryUrl(string url)
    {
        // Arrange
        await File.WriteAllTextAsync("pom.xml", $"""
            <project><repositories>
              <repository><id>{FEED_ID}</id><url>{url}</url></repository>
            </repositories></project>
            """);

        // Act
        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.OnBeforeGenerateAsync(new BuildConfig(), default));

        // Assert
        StringAssert.Contains(result.Message, "must be an absolute HTTP or HTTPS URL without embedded credentials");
        Assert.IsFalse(File.Exists(SECRET_PATH));
    }

    [TestMethod]
    [DataRow(FEED_URL, HTTP_FEED_URL)]
    [DataRow(HTTP_FEED_URL, FEED_URL)]
    public async Task OnBeforeGenerateAsync_ShouldRejectMixedSchemesForTheSameHost(string firstUrl, string secondUrl)
    {
        // Arrange
        await File.WriteAllTextAsync("pom.xml", $"""
            <project>
              <repositories>
                <repository><id>{FEED_ID}</id><url>{firstUrl}</url></repository>
                <repository><id>another-id</id><url>{secondUrl}</url></repository>
              </repositories>
            </project>
            """);

        // Act
        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.OnBeforeGenerateAsync(new BuildConfig(), default));

        // Assert
        StringAssert.Contains(result.Message, "Do not mix HTTP and HTTPS Maven repositories");
        Assert.IsFalse(File.Exists(SECRET_PATH));
    }

    [TestMethod]
    [DataRow("https://pkgs.dev.azure.com.example.com/example/_packaging/feed/maven/v1")]
    [DataRow("https://hsbdev.pkgs.visualstudio.com.example.com/_packaging/feed/maven/v1")]
    public async Task OnBeforeGenerateAsync_ShouldSkipUnlistedFeedDomain(string url)
    {
        // Arrange
        await File.WriteAllTextAsync("pom.xml", $"""
            <project><repositories>
              <repository><id>{FEED_ID}</id><url>{url}</url></repository>
            </repositories></project>
            """);

        // Act
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Assert
        Assert.IsFalse(File.Exists(SECRET_PATH));
    }

    [TestMethod]
    [DataRow(FEED_URL, "https://repo.example.com/maven", false)]
    [DataRow("https://repo.example.com/maven", FEED_URL, true)]
    public async Task OnBeforeGenerateAsync_ShouldUseLastUrlForSameServerId(string firstUrl, string lastUrl, bool shouldWriteSettings)
    {
        // Arrange
        await File.WriteAllTextAsync("pom.xml", $"""
            <project><repositories>
              <repository><id>{FEED_ID}</id><url>{firstUrl}</url></repository>
              <repository><id>{FEED_ID}</id><url>{lastUrl}</url></repository>
            </repositories></project>
            """);

        // Act
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Assert
        Assert.AreEqual(shouldWriteSettings, File.Exists(SECRET_PATH));
    }

    [TestMethod]
    public async Task OnBeforeGenerateAsync_ShouldUseProviderForMissingServerId()
    {
        // Arrange
        Environment.SetEnvironmentVariable(TOKEN_VARIABLE, null);
        Environment.SetEnvironmentVariable(HELPER_VERSION_VARIABLE, "invalid");
        await File.WriteAllTextAsync("pom.xml", $"""
            <project><repositories>
              <repository><id>{FEED_ID}</id><url>{FEED_URL}</url></repository>
              <repository><id>another-id</id><url>{FEED_URL}</url></repository>
            </repositories></project>
            """);
        Directory.CreateDirectory(".mvn");
        await File.WriteAllTextAsync(".mvn/settings.xml", $"""
            <settings><servers>
              <server><id>{FEED_ID}</id><username>existing-user</username><password>existing-token</password></server>
            </servers></settings>
            """);

        // Act
        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut.OnBeforeGenerateAsync(new BuildConfig(), default));

        // Assert
        StringAssert.Contains(result.Message, "exact Azure Artifacts Credential Provider release version");
        Assert.IsFalse(File.Exists(SECRET_PATH));
    }

    [TestMethod]
    [DataRow(FEED_URL)]
    [DataRow(HTTP_FEED_URL)]
    [DataRow("https://pkgs.dev.azure.com:8443/example/_packaging/feed/maven/v1")]
    public async Task OnBeforeGenerateAsync_ShouldWriteSettings(string feedUrl)
    {
        // Arrange
        var pom = await File.ReadAllTextAsync("pom.xml");
        await File.WriteAllTextAsync("pom.xml", pom.Replace(FEED_URL, feedUrl));

        // Act
        await Sut.OnBeforeGenerateAsync(new BuildConfig(), default);

        // Assert
        var result = XDocument.Load(SECRET_PATH).Root;
        var ns = result.Name.Namespace;
        var server = result.Element(ns + "servers").Element(ns + "server");
        Assert.AreEqual(FEED_ID, (string)server.Element(ns + "id"));
        Assert.AreEqual(JOB_TOKEN, (string)server.Element(ns + "password"));
        Assert.IsFalse(Directory.Exists(".buildcharts/plugins"));
    }
}
