using BuildCharts.Tool.Chart;

namespace BuildCharts.Tests.Chart;

[TestClass]
public class ChartReferenceTests
{
    [TestMethod]
    public void TryParse_WithTagReference_ShouldParseExpectedValues()
    {
        // Arrange
        var reference = "registry-1.docker.io/buildcharts/dotnet-build:0.0.1";

        // Act
        var result = ChartReference.TryParse(reference, out var chartReference);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual("registry-1.docker.io", chartReference.Registry);
        Assert.AreEqual("buildcharts/dotnet-build", chartReference.RepositoryPath);
        Assert.AreEqual("buildcharts", chartReference.RepositoryParentPath);
        Assert.AreEqual("dotnet-build", chartReference.ChartName);
        Assert.AreEqual("0.0.1", chartReference.Tag);
        Assert.IsNull(chartReference.Digest);
        Assert.IsFalse(chartReference.IsDigest);
        Assert.AreEqual("oci://registry-1.docker.io/buildcharts/dotnet-build", chartReference.RepositoryFullPath);
        Assert.AreEqual("dotnet-build-0.0.1.tgz", chartReference.Filename);
    }

    [TestMethod]
    public void TryParse_WithDigestReference_ShouldParseExpectedValues()
    {
        // Arrange
        var digest = "sha256:4da50de6250055a119d51c620e2ed825529d281b2d27a9e2bb1f17b912d1a11c";
        var reference = $"registry-1.docker.io/buildcharts/dotnet-build@{digest}";

        // Act
        var success = ChartReference.TryParse(reference, out var chartReference);

        // Assert
        Assert.IsTrue(success);
        Assert.AreEqual("registry-1.docker.io", chartReference.Registry);
        Assert.AreEqual("buildcharts/dotnet-build", chartReference.RepositoryPath);
        Assert.AreEqual("dotnet-build", chartReference.ChartName);
        Assert.IsNull(chartReference.Tag);
        Assert.AreEqual(digest, chartReference.Digest);
        Assert.IsTrue(chartReference.IsDigest);
        Assert.AreEqual("dotnet-build@sha256-4da50de6250055a119d51c620e2ed825529d281b2d27a9e2bb1f17b912d1a11c.tgz", chartReference.Filename);
    }

    [TestMethod]
    public void TryParse_WithTagAndDigest_ShouldPreferDigestAndStripTagFromRepositoryPath()
    {
        // Arrange
        var digest = "sha256:4da50de6250055a119d51c620e2ed825529d281b2d27a9e2bb1f17b912d1a11c";
        var reference = $"registry-1.docker.io/buildcharts/dotnet-build:0.0.1@{digest}";

        // Act
        var result = ChartReference.TryParse(reference, out var chartReference);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual("buildcharts/dotnet-build", chartReference.RepositoryPath);
        Assert.AreEqual("dotnet-build", chartReference.ChartName);
        Assert.IsNull(chartReference.Tag);
        Assert.AreEqual(digest, chartReference.Digest);
        Assert.IsTrue(chartReference.IsDigest);
    }

    [TestMethod]
    public void TryParse_WithAliasAndOciPrefix_ShouldParseAlias()
    {
        // Arrange
        var reference = "sdk=oci://registry-1.docker.io/buildcharts/dotnet-build:1.2.3";

        // Act
        var result = ChartReference.TryParse(reference, out var chartReference);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual("sdk", chartReference.Alias);
        Assert.AreEqual("registry-1.docker.io", chartReference.Registry);
        Assert.AreEqual("buildcharts/dotnet-build", chartReference.RepositoryPath);
        Assert.AreEqual("1.2.3", chartReference.Tag);
    }

    [TestMethod]
    public void TryParse_WithoutTagOrDigest_ShouldDefaultToLatestTag()
    {
        // Arrange
        var reference = "registry-1.docker.io/buildcharts/dotnet-build";

        // Act
        var result = ChartReference.TryParse(reference, out var chartReference);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual("latest", chartReference.Tag);
        Assert.IsFalse(chartReference.IsDigest);
        Assert.AreEqual("dotnet-build-latest.tgz", chartReference.Filename);
    }

    [TestMethod]
    public void TryParse_WithInvalidReference_ShouldReturnFalse()
    {
        // Arrange
        const string reference = "registry-1.docker.io";

        // Act
        var result = ChartReference.TryParse(reference, out var chartReference);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual(default, chartReference);
    }
}
