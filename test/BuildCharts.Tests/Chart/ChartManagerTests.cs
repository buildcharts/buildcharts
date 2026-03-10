using AutoFixture;
using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Configuration.Models;
using BuildCharts.Tool.Oras;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;

namespace BuildCharts.Tests.Chart;

[TestClass]
public class ChartManagerTests : TestBase
{
    private ChartConfig _chartConfig;
    private ChartLock _chartLock;
    private string _chartName;
    private string _manifestDigest;
    private string _outputDir;
    private string _cacheArchivePath;

    private Mock<IOrasClient> OrasClientMock => Fixture.Freeze<Mock<IOrasClient>>();
    private ChartManager Sut => Fixture.Freeze<ChartManager>();

    [TestInitialize]
    public void TestInitialize()
    {
        _chartName = $"dotnet-build-{Guid.NewGuid():N}";
        _manifestDigest = $"sha256:{Guid.NewGuid():N}{Guid.NewGuid():N}";
        _outputDir = Path.Combine(Path.GetTempPath(), "buildcharts-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_outputDir);

        _chartConfig = new ChartConfig
        {
            Dependencies =
            [
                new ChartDependency
                {
                    Repository = "oci://registry-1.docker.io/buildcharts",
                    Name = _chartName,
                    Version = "0.0.1",
                },
            ],
        };

        _chartLock = new ChartLock();

        OrasClientMock
            .Setup(x => x.GetManifestDigestAsync(It.IsAny<ChartReference>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_manifestDigest);

        OrasClientMock
            .Setup(x => x.Pull(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_manifestDigest);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        try
        {
            if (Directory.Exists(_outputDir))
            {
                Directory.Delete(_outputDir, true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [TestMethod]
    public void TryGetBlobCacheForDigest_ShouldReturnFalse_WhenReferenceInvalid()
    {
        // Arrange
        var cacheRoot = Path.Combine(_outputDir, "cache");

        // Act
        var result = Sut.TryGetBlobCacheForDigest("registry-1.docker.io", cacheRoot, out var path);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual(string.Empty, path);
        Assert.IsFalse(Directory.Exists(cacheRoot));
    }

    [TestMethod]
    public void TryGetBlobCacheForDigest_ShouldReturnFalse_WhenReferenceIsTag()
    {
        // Arrange
        var cacheRoot = Path.Combine(_outputDir, "cache");
        var reference = $"registry-1.docker.io/buildcharts/{_chartName}:0.0.1";

        // Act
        var result = Sut.TryGetBlobCacheForDigest(reference, cacheRoot, out var path);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual(string.Empty, path);
        Assert.IsFalse(Directory.Exists(cacheRoot));
    }

    [TestMethod]
    public void TryGetBlobCacheForDigest_ShouldReturnFalse_WhenReferenceWhitespace()
    {
        // Arrange
        var cacheRoot = Path.Combine(_outputDir, "cache");

        // Act
        var result = Sut.TryGetBlobCacheForDigest(" ", cacheRoot, out var path);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual(string.Empty, path);
        Assert.IsFalse(Directory.Exists(cacheRoot));
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldCallDigestAndPull_WhenCacheMiss()
    {
        // Arrange
        var expectedDigestReference = $"registry-1.docker.io/buildcharts/{_chartName}@{_manifestDigest}";
        var expectedCacheRoot = Path.Combine(_outputDir, "cache");

        Fixture.Inject(Options.Create(new ChartOptions
        {
            CachePath = expectedCacheRoot,
        }));

        // Act
        await Sut.UpdateAsync(_chartConfig, _chartLock, outputDir: _outputDir, updateChartLockFile: false);

        // Assert
        OrasClientMock.Verify(x => x.GetManifestDigestAsync(It.Is<ChartReference>(c =>
            c.Registry == "registry-1.docker.io" &&
            c.RepositoryPath == $"buildcharts/{_chartName}" &&
            c.ChartName == _chartName &&
            c.Tag == "0.0.1"), It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.Verify(x => x.Pull(expectedDigestReference, true, _outputDir, expectedCacheRoot, It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldNotCallOras_WhenDependenciesEmpty()
    {
        // Arrange
        _chartConfig.Dependencies = [];

        // Act
        await Sut.UpdateAsync(_chartConfig, _chartLock, updateChartLockFile: false);

        // Assert
        OrasClientMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldNotCallOras_WhenDependenciesNull()
    {
        // Arrange
        _chartConfig.Dependencies = null;

        // Act
        await Sut.UpdateAsync(_chartConfig, _chartLock, updateChartLockFile: false);

        // Assert
        OrasClientMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldNotCallPull_WhenCacheHit()
    {
        // Arrange
        var expectedCacheRoot = Path.Combine(_outputDir, "cache");

        Fixture.Inject(Options.Create(new ChartOptions
        {
            CachePath = expectedCacheRoot,
        }));

        ChartReference.TryParse($"registry-1.docker.io/buildcharts/{_chartName}@{_manifestDigest}", out var digestChartReference);
        _cacheArchivePath = Path.Combine(expectedCacheRoot, digestChartReference.Filename);

        CreateValidCachedChartArchive(_cacheArchivePath);

        // Act
        await Sut.UpdateAsync(_chartConfig, _chartLock, outputDir: _outputDir, updateChartLockFile: false);

        // Assert
        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "chart", "Chart.yaml")));
        OrasClientMock.Verify(x => x.GetManifestDigestAsync(It.IsAny<ChartReference>(), It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.Verify(x => x.Pull(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        OrasClientMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldSkipInvalidDependencies()
    {
        // Arrange
        _chartConfig.Dependencies =
        [
            new ChartDependency { Repository = null, Name = "dotnet-build", Version = "0.0.1" },
            new ChartDependency { Repository = "oci://registry-1.docker.io/buildcharts", Name = null, Version = "0.0.1" },
            new ChartDependency { Repository = "oci://registry-1.docker.io/buildcharts", Name = "dotnet-build", Version = null },
        ];

        // Act
        await Sut.UpdateAsync(_chartConfig, _chartLock, updateChartLockFile: false);

        // Assert
        OrasClientMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldSkipUpdateChartLockFile()
    {
        // Arrange
        var expectedDigestReference = $"registry-1.docker.io/buildcharts/{_chartName}@{_manifestDigest}";
        var expectedCacheRoot = Path.Combine(_outputDir, "cache");

        Fixture.Inject(Options.Create(new ChartOptions
        {
            CachePath = expectedCacheRoot,
        }));

        // Act
        await Sut.UpdateAsync(_chartConfig, _chartLock, outputDir: _outputDir, updateChartLockFile: false);

        // Assert
        Assert.AreEqual(0, _chartLock.Dependencies.Count);
        OrasClientMock.Verify(x => x.GetManifestDigestAsync(It.IsAny<ChartReference>(), It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.Verify(x => x.Pull(expectedDigestReference, true, _outputDir, expectedCacheRoot, It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void UpdateAsync_ShouldThrowArgument_WhenReferenceInvalid()
    {
        // Arrange
        _chartConfig.Dependencies =
        [
            new ChartDependency
            {
                Repository = "oci://",
                Name = "dotnet-build",
                Version = "0.0.1",
            },
        ];

        // Act
        Action action = () => Sut.UpdateAsync(_chartConfig, _chartLock, updateChartLockFile: false).GetAwaiter().GetResult();
        var ex = Assert.Throws<ArgumentException>(action);

        // Assert
        StringAssert.Contains(ex.Message, "Invalid chart reference");
        OrasClientMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void UpdateAsync_ShouldThrowInvalidOperation_WhenLockDigestMismatch()
    {
        // Arrange
        var expectedCacheRoot = Path.Combine(_outputDir, "cache");

        Fixture.Inject(Options.Create(new ChartOptions
        {
            CachePath = expectedCacheRoot,
        }));

        _chartLock.Dependencies =
        [
            new ChartLockDependency
            {
                Name = _chartName,
                Version = "0.0.1",
                Repository = $"oci://registry-1.docker.io/buildcharts/{_chartName}",
                Digest = "sha256:deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
            },
        ];

        // Act
        Action action = () => Sut.UpdateAsync(_chartConfig, _chartLock, updateChartLockFile: false).GetAwaiter().GetResult();
        var ex = Assert.Throws<InvalidOperationException>(action);

        // Assert
        StringAssert.Contains(ex.Message, "digest mismatch");
        OrasClientMock.Verify(x => x.GetManifestDigestAsync(It.IsAny<ChartReference>(), It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.Verify(x => x.Pull(It.IsAny<string>(), true, _outputDir, expectedCacheRoot, It.IsAny<CancellationToken>()), Times.Never);
        OrasClientMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldUpdateLockDependency_WhenExisting()
    {
        // Arrange
        var expectedCacheRoot = Path.Combine(_outputDir, "cache");

        Fixture.Inject(Options.Create(new ChartOptions
        {
            CachePath = expectedCacheRoot,
        }));

        _chartConfig.Dependencies[0].Version = "1.2.3";
        _chartLock.Dependencies =
        [
            new ChartLockDependency
            {
                Name = _chartName,
                Version = "0.0.1",
                Repository = $"oci://registry-1.docker.io/buildcharts/{_chartName}",
                Digest = "sha256:oldoldoldoldoldoldoldoldoldoldoldoldoldoldoldoldoldoldoldoldold",
            },
        ];
        var expectedDigestReference = $"registry-1.docker.io/buildcharts/{_chartName}@{_manifestDigest}";

        // Act
        await Sut.UpdateAsync(_chartConfig, _chartLock, outputDir: _outputDir, useLockFile: false, updateChartLockFile: true);

        // Assert
        Assert.AreEqual(1, _chartLock.Dependencies.Count);
        var dependency = _chartLock.Dependencies[0];
        Assert.AreEqual(_chartName, dependency.Name);
        Assert.AreEqual("1.2.3", dependency.Version);
        Assert.AreEqual($"oci://registry-1.docker.io/buildcharts/{_chartName}", dependency.Repository);
        Assert.AreEqual(_manifestDigest, dependency.Digest);
        OrasClientMock.Verify(x => x.GetManifestDigestAsync(It.IsAny<ChartReference>(), It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.Verify(x => x.Pull(expectedDigestReference, true, _outputDir, expectedCacheRoot, It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldUpdateLockDependency_WhenMissing()
    {
        // Arrange
        var expectedDigestReference = $"registry-1.docker.io/buildcharts/{_chartName}@{_manifestDigest}";
        var expectedCacheRoot = Path.Combine(_outputDir, "cache");

        Fixture.Inject(Options.Create(new ChartOptions
        {
            CachePath = expectedCacheRoot,
        }));

        // Act
        await Sut.UpdateAsync(_chartConfig, _chartLock, outputDir: _outputDir, useLockFile: false, updateChartLockFile: true);

        // Assert
        Assert.AreEqual(1, _chartLock.Dependencies.Count);
        var dependency = _chartLock.Dependencies[0];
        Assert.AreEqual(_chartName, dependency.Name);
        Assert.AreEqual("0.0.1", dependency.Version);
        Assert.AreEqual($"oci://registry-1.docker.io/buildcharts/{_chartName}", dependency.Repository);
        Assert.AreEqual(_manifestDigest, dependency.Digest);
        OrasClientMock.Verify(x => x.GetManifestDigestAsync(It.IsAny<ChartReference>(), It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.Verify(x => x.Pull(expectedDigestReference, true, _outputDir, expectedCacheRoot, It.IsAny<CancellationToken>()), Times.Once);
        OrasClientMock.VerifyNoOtherCalls();
    }

    private static void CreateValidCachedChartArchive(string archivePath)
    {
        var directory = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = File.Create(archivePath);
        using var gzipStream = new GZipOutputStream(fileStream);
        using var tarStream = new TarOutputStream(gzipStream, Encoding.UTF8);

        var content = "apiVersion: v2\nname: test\nversion: 0.1.0\n"u8.ToArray();
        var entry = TarEntry.CreateTarEntry("chart/Chart.yaml");
        entry.Size = content.Length;

        tarStream.PutNextEntry(entry);
        tarStream.Write(content, 0, content.Length);
        tarStream.CloseEntry();
        tarStream.Finish();
    }
}
