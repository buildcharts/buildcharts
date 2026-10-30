using BuildCharts.Tool.Plugins.NpmAuthenticate_v1.Helpers;
using System.Text;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class NpmCredentialProviderHelperTests : TestBase
{
    [TestMethod]
    [DataRow("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/")]
    [DataRow("https://pkgs.dev.azure.com/org/project/_packaging/feed/npm/registry/")]
    [DataRow("https://org.pkgs.visualstudio.com/_packaging/feed/npm/registry")]
    public void FormatCredentials_ShouldIncludeBothNpmPaths(string url)
    {
        var registry = new Uri(url);
        var token = "provider-token";
        var prefix = "//" + registry.Authority + registry.AbsolutePath.TrimEnd('/') + "/";
        var feedPrefix = prefix[..^"registry/".Length];

        var result = NpmCredentialProviderHelper.FormatCredentials(registry, token);

        foreach (var path in new[] { prefix, feedPrefix })
        {
            StringAssert.Contains(result, $"{path}:username=buildcharts");
            StringAssert.Contains(result, $"{path}:_password={Convert.ToBase64String(Encoding.UTF8.GetBytes(token))}");
            StringAssert.Contains(result, $"{path}:email=unused@example.com");
        }
        Assert.IsFalse(result.Contains(token, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("token\nother")]
    public void FormatCredentials_ShouldRejectInvalidTokens(string token)
    {
        var result = Assert.Throws<InvalidOperationException>(() => NpmCredentialProviderHelper.FormatCredentials(new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"), token));

        StringAssert.Contains(result.Message, "invalid npm credential");
    }
}
