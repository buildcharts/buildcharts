using AutoFixture;
using BuildCharts.Tool.Plugins.NpmAuthenticate_v1.Helpers;
using Moq;
using Moq.Protected;
using System.Net;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class VstsNpmAuthHelperTests : TestBase
{
    private string _directory;
    private HttpClient _http;
    private Mock<HttpMessageHandler> HandlerMock => Fixture.Freeze<Mock<HttpMessageHandler>>();

    [TestInitialize]
    public void TestInitialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "buildcharts-npm-helper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        HandlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        _http = new HttpClient(HandlerMock.Object);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _http.Dispose();
        Directory.Delete(_directory, true);
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
    public async Task EnsureInstalledAsync_ShouldReuseThePinnedHelper()
    {
        var executable = Path.Combine(_directory, VstsNpmAuthHelper.Version, "package", "bin", "vsts-npm-auth.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable));
        await File.WriteAllTextAsync(executable, "cached-helper");
        await File.WriteAllTextAsync(Path.Combine(_directory, VstsNpmAuthHelper.Version, "installed.sha512"), "ROZc5HXaWNNO++tVnS5PNzEqI1cLEiaUq0uPJ+azGRTVJAVvECzGu8ideed0iDFg0/sNuzUp5gshfenDyAIrMQ==");

        var result = await VstsNpmAuthHelper.EnsureInstalledAsync(_http, _directory, default);

        Assert.AreEqual(executable, result);
        HandlerMock.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }
}
