using AutoFixture;
using BuildCharts.Tool.Plugins.NpmAuthenticate_v1.Helpers;
using Moq;
using Moq.Protected;
using System.Net;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class VstsNpmAuthHelperTests : TestBase
{
    private string _directory;
    private string _originalHelperVersion;
    private HttpClient _http;
    private Mock<HttpMessageHandler> HandlerMock => Fixture.Freeze<Mock<HttpMessageHandler>>();

    [TestInitialize]
    public void TestInitialize()
    {
        _originalHelperVersion = Environment.GetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION");
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION", null);
        _directory = Path.Combine(Path.GetTempPath(), "buildcharts-npm-helper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        HandlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        _http = new HttpClient(HandlerMock.Object);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION", _originalHelperVersion);
        _http.Dispose();
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task EnsureInstalledAsync_ShouldDownloadAndCacheTheOverriddenVersion()
    {
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION", "0.42.0");
        using var archive = new MemoryStream();
        using (var gzip = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, leaveOpen: true))
        using (var executable = new MemoryStream(Encoding.UTF8.GetBytes("selected-helper")))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "package/bin/vsts-npm-auth.exe") { DataStream = executable });
        }
        var bytes = archive.ToArray();
        var integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(bytes));
        HandlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri.AbsolutePath.EndsWith(".tgz", StringComparison.Ordinal)
                    ? new ByteArrayContent(bytes)
                    : new StringContent(JsonSerializer.Serialize(new { dist = new { integrity } })),
            });

        var result = await VstsNpmAuthHelper.EnsureInstalledAsync(_http, _directory, default);
        var cached = await VstsNpmAuthHelper.EnsureInstalledAsync(_http, _directory, default);

        Assert.AreEqual(Path.Combine(_directory, "0.42.0", "package", "bin", "vsts-npm-auth.exe"), result);
        Assert.AreEqual("selected-helper", await File.ReadAllTextAsync(result));
        Assert.AreEqual(result, cached);
        HandlerMock.Protected().Verify("SendAsync", Times.Exactly(2), ItExpr.Is<HttpRequestMessage>(request => request.RequestUri.AbsoluteUri == "https://registry.npmjs.org/vsts-npm-auth/0.42.0"), ItExpr.IsAny<CancellationToken>());
        HandlerMock.Protected().Verify("SendAsync", Times.Once(), ItExpr.Is<HttpRequestMessage>(request => request.RequestUri.AbsoluteUri == "https://registry.npmjs.org/vsts-npm-auth/-/vsts-npm-auth-0.42.0.tgz"), ItExpr.IsAny<CancellationToken>());
    }

    [TestMethod]
    public async Task EnsureInstalledAsync_ShouldIgnoreIncompleteInstallation()
    {
        var executable = Path.Combine(_directory, VstsNpmAuthHelper.Version, "package", "bin", "vsts-npm-auth.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable));
        await File.WriteAllTextAsync(executable, "incomplete-installation");

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => VstsNpmAuthHelper.EnsureInstalledAsync(_http, _directory, default));

        StringAssert.Contains(result.Message, "SHA-512");
        HandlerMock.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [TestMethod]
    public async Task EnsureInstalledAsync_ShouldIgnoreOtherHelperVersions()
    {
        var oldExecutable = Path.Combine(_directory, "0.42.0", "package", "bin", "vsts-npm-auth.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(oldExecutable));
        await File.WriteAllTextAsync(oldExecutable, "old-version");

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => VstsNpmAuthHelper.EnsureInstalledAsync(_http, _directory, default));

        StringAssert.Contains(result.Message, "SHA-512");
        HandlerMock.Protected().Verify("SendAsync", Times.Once(), ItExpr.Is<HttpRequestMessage>(request => request.RequestUri.AbsoluteUri == "https://registry.npmjs.org/vsts-npm-auth/-/vsts-npm-auth-0.43.0.tgz"), ItExpr.IsAny<CancellationToken>());
        Assert.IsTrue(File.Exists(oldExecutable));
    }

    [TestMethod]
    public async Task EnsureInstalledAsync_ShouldRejectCorruptDownloads()
    {
        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => VstsNpmAuthHelper.EnsureInstalledAsync(_http, _directory, default));

        StringAssert.Contains(result.Message, "SHA-512");
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_directory).Length);
    }

    [TestMethod]
    [DataRow("../other-folder")]
    [DataRow("latest")]
    [DataRow("0.43.0/path")]
    public async Task EnsureInstalledAsync_ShouldRejectInvalidVersionOverrides(string version)
    {
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION", version);

        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => VstsNpmAuthHelper.EnsureInstalledAsync(_http, _directory, default));

        StringAssert.Contains(result.Message, "exact package version");
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_directory).Length);
        HandlerMock.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("0.43.0")]
    public async Task EnsureInstalledAsync_ShouldReuseThePinnedHelper(string versionOverride)
    {
        Environment.SetEnvironmentVariable("NPM_AUTHENTICATE_HELPER_VERSION", versionOverride);
        var executable = Path.Combine(_directory, VstsNpmAuthHelper.Version, "package", "bin", "vsts-npm-auth.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable));
        await File.WriteAllTextAsync(executable, "cached-helper");
        await File.WriteAllTextAsync(Path.Combine(_directory, VstsNpmAuthHelper.Version, "installed.sha512"), "ROZc5HXaWNNO++tVnS5PNzEqI1cLEiaUq0uPJ+azGRTVJAVvECzGu8ideed0iDFg0/sNuzUp5gshfenDyAIrMQ==");

        var result = await VstsNpmAuthHelper.EnsureInstalledAsync(_http, _directory, default);

        Assert.AreEqual(executable, result);
        HandlerMock.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }
}
