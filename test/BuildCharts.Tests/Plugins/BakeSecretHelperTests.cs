using BuildCharts.Tool.Plugins.Helpers;
using BuildCharts.Tool.Plugins.NuGetAuthenticate_v1;
using System.Text;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class BakeSecretHelperTests : TestBase
{
    [TestMethod]
    [DataRow("")]
    [DataRow("  secret = []\n")]
    [DataRow("  secret = [\"type=env,id=existing\"]\n")]
    [DataRow("  secret = [\"type=env,id=existing\",]\n")]
    public void AddSecret_ShouldAppendToCommonTarget(string attribute)
    {
        var builder = new StringBuilder("target \"_common\" {\n" + attribute + "}\ntarget \"other\" {}\n");

        BakeSecretHelper.AddSecret(builder, "type=file,id=npmrc,src=.buildcharts/secrets/npmrc");
        var result = builder.ToString();

        Assert.AreEqual(1, result.Split("secret =").Length - 1);
        StringAssert.Contains(result, "\"type=file,id=npmrc,src=.buildcharts/secrets/npmrc\"");
        StringAssert.EndsWith(result, "}\ntarget \"other\" {}\n");
        if (attribute.Contains("id=existing"))
        {
            StringAssert.Contains(result, "\"type=env,id=existing\",");
        }
    }

    [TestMethod]
    public void AddSecret_ShouldEscapeQuotesAndBackslashes()
    {
        var builder = new StringBuilder("target \"_common\" {\n  args = { VALUE = \"} [\" }\n}\n");

        BakeSecretHelper.AddSecret(builder, "type=file,id=custom,src=C:\\secrets\\\"feed\"");
        var result = builder.ToString();

        StringAssert.Contains(result, "src=C:\\\\secrets\\\\\\\"feed\\\"\"");
    }

    [TestMethod]
    public void AddSecret_ShouldIgnoreDuplicateSecret()
    {
        var builder = new StringBuilder("target \"_common\" {}\n");
        BakeSecretHelper.AddSecret(builder, "type=env,id=custom");
        var original = builder.ToString();

        BakeSecretHelper.AddSecret(builder, "type=env,id=custom");
        var result = builder.ToString();

        Assert.AreEqual(original, result);
    }

    [TestMethod]
    [DataRow("target \"other\" {}")]
    [DataRow("target \"_common\" {")]
    [DataRow("target \"_common\" {\n  secret = [\n}")]
    public void AddSecret_ShouldRejectMissingOrMalformedTarget(string text)
    {
        var builder = new StringBuilder(text);

        var result = Assert.Throws<InvalidOperationException>(() => BakeSecretHelper.AddSecret(builder, "type=env,id=custom"));

        Assert.IsNotNull(result);
        Assert.AreEqual(text, builder.ToString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OnAfterGenerateAsync_ShouldComposeNuGetWithOtherSecrets(bool nugetFirst)
    {
        var builder = new StringBuilder("target \"_common\" {}\n");
        var plugin = new NuGetAuthenticatePlugin();
        if (!nugetFirst)
        {
            BakeSecretHelper.AddSecret(builder, "type=env,id=custom");
        }

        await plugin.OnAfterGenerateAsync(null, null, builder, default);
        BakeSecretHelper.AddSecret(builder, "type=env,id=custom");
        await plugin.OnAfterGenerateAsync(null, null, builder, default);
        var result = builder.ToString();

        Assert.AreEqual(1, result.Split("secret =").Length - 1);
        Assert.AreEqual(1, result.Split("id=custom").Length - 1);
        Assert.AreEqual(1, result.Split("id=ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS,").Length - 1);
        Assert.AreEqual(1, result.Split("id=ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN,").Length - 1);
    }
}
