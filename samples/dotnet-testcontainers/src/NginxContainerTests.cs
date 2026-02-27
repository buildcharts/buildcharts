using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

namespace Dotnet.Testcontainers.Sample.Tests;

[TestClass]
public sealed class NginxContainerTests
{
    [TestMethod]
    public async Task StartsNginxContainerAndRespondsOverHttp()
    {
        // Arrange
        await using var container = new ContainerBuilder("nginx:alpine")
            .WithName($"sample-nginx-{Guid.NewGuid():N}")
            .WithPortBinding(80, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(80))
            .Build();

        await container.StartAsync();

        Assert.IsFalse(string.IsNullOrWhiteSpace(container.Id));
        var mappedPort = container.GetMappedPublicPort(80);
        Assert.IsTrue(mappedPort > 0);

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        // Act
        HttpResponseMessage? response = null;
        var body = string.Empty;

        // Port-ready does not always mean nginx is ready to serve HTTP immediately.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                response = await httpClient.GetAsync($"http://{container.Hostname}:{mappedPort}/");
                body = await response.Content.ReadAsStringAsync();
                break;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(500);
            }
        }

        // Assert
        Assert.IsNotNull(response, "Expected HTTP response from nginx container, but no response was received.");
        Assert.IsTrue(response.IsSuccessStatusCode, $"Expected 2xx response, got {(int)response.StatusCode}.");
        StringAssert.Contains(body.ToLowerInvariant(), "nginx");
    }
}
