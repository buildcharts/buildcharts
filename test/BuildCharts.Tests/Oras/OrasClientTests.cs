using BuildCharts.Tool.Oras;

namespace BuildCharts.Tests.Oras;

[TestClass]
public sealed class OrasClientTests : TestBase
{
    [TestMethod]
    [DataRow("https://registry.example.com", "https://registry.example.com/token")]
    [DataRow("https://registry.example.com:5443", "https://registry.example.com:5443/token")]
    [DataRow("https://registry-1.docker.io", "https://registry-1.docker.io/token")]
    public async Task CreateRealmValidator_ShouldAllowRealm_WhenSameRegistry(string registry, string realm)
    {
        // Arrange
        var registryUri = new Uri(registry);
        var realmUri = new Uri(realm);

        // Act
        var result = await OrasClient.CreateRealmValidator().IsRealmAllowedAsync(registryUri, realmUri);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    [DataRow("https://registry-1.docker.io")]
    [DataRow("https://docker.io")]
    [DataRow("https://index.docker.io")]
    [DataRow("https://REGISTRY-1.DOCKER.IO")]
    [DataRow("https://registry-1.docker.io:443")]
    [DataRow("https://registry-1.docker.io.")]
    [DataRow("https://registry.example.com")]
    [DataRow("https://registry-1.docker.io.example.com")]
    [DataRow("https://registry-1.docker.io:5443")]
    [DataRow("http://registry-1.docker.io")]
    public async Task CreateRealmValidator_ShouldAllowRealm_WhenTrustedHost(string registry)
    {
        // Arrange
        var registryUri = new Uri(registry);
        var realmUri = new Uri("https://auth.docker.io/token");

        // Act
        var result = await OrasClient.CreateRealmValidator().IsRealmAllowedAsync(registryUri, realmUri);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    [DataRow("http://auth.docker.io/token")]
    [DataRow("https://auth.docker.io:5443/token")]
    [DataRow("https://user:password@auth.docker.io/token")]
    [DataRow("https://unrelated.example/token")]
    [DataRow("https://auth.docker.io.example.com/token")]
    [DataRow("file:///token")]
    public async Task CreateRealmValidator_ShouldRejectUnsafeRealms(string realm)
    {
        // Arrange
        var registryUri = new Uri("https://registry-1.docker.io");
        var realmUri = new Uri(realm);

        // Act
        var result = await OrasClient.CreateRealmValidator().IsRealmAllowedAsync(registryUri, realmUri);

        // Assert
        Assert.IsFalse(result);
    }
}