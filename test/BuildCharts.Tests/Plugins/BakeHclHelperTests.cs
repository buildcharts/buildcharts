using BuildCharts.Tool.Plugins.Helpers;
using BuildCharts.Tool.Plugins.NuGetAuthenticate_v1;
using System.Text;

namespace BuildCharts.Tests.Plugins;

[TestClass]
public sealed class BakeHclHelperTests : TestBase
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AddExtraHosts_ShouldComposeWithSecrets(bool secretFirst)
    {
        var builder = new StringBuilder("target \"_common\" {}\ntarget \"test\" {\n  args = {\n    VALUE = \"} [\"\n  }\n}\ntarget \"other\" {}\n");
        if (secretFirst)
        {
            BakeHclHelper.AddSecret(builder, "type=env,id=custom");
        }

        BakeHclHelper.AddExtraHosts(builder, "172.18.0.2", "172.18.0.1");
        if (!secretFirst)
        {
            BakeHclHelper.AddSecret(builder, "type=env,id=custom");
        }
        var result = builder.ToString();

        Assert.AreEqual(1, result.Split("TESTCONTAINERS_HOST_OVERRIDE = \"172.18.0.2\"").Length - 1);
        Assert.AreEqual(1, result.Split("DOCKER_HOST = \"tcp://172.18.0.2:2375\"").Length - 1);
        Assert.AreEqual(1, result.Split("\"host.docker.internal\" = \"172.18.0.1\"").Length - 1);
        Assert.AreEqual(1, result.Split("\"type=env,id=custom\"").Length - 1);
        StringAssert.Contains(result, "VALUE = \"} [\"");
        StringAssert.EndsWith(result, "}\ntarget \"other\" {}\n");
    }

    [TestMethod]
    [DataRow("DOCKER_HOST", "tcp://existing:2375")]
    [DataRow("TESTCONTAINERS_HOST_OVERRIDE", "existing")]
    public void AddExtraHosts_ShouldPreserveExistingOverride(string name, string value)
    {
        var builder = new StringBuilder($"target \"test\" {{\n  args = {{\n    {name} = \"{value}\"\n  }}\n}}\n");

        BakeHclHelper.AddExtraHosts(builder, "172.18.0.2", "172.18.0.1");
        var result = builder.ToString();

        Assert.AreEqual(1, result.Split($"{name} =").Length - 1);
        StringAssert.Contains(result, $"{name} = \"{value}\"");
        StringAssert.Contains(result, "\"host.docker.internal\" = \"172.18.0.1\"");
    }

    [TestMethod]
    [DataRow("172.18.0.2", "172.18.0.1", "tcp://172.18.0.2:2375")]
    [DataRow("172.22.0.17", "192.168.65.254", "tcp://172.22.0.17:2375")]
    [DataRow("fd00::2", "fd00::1", "tcp://[fd00::2]:2375")]
    public void AddExtraHosts_ShouldSeparateDaemonAndHostAddresses(string containerAddress, string gatewayAddress, string dockerHost)
    {
        var builder = new StringBuilder("target \"test\" {\n  args = {}\n}\n");

        BakeHclHelper.AddExtraHosts(builder, containerAddress, gatewayAddress);
        var result = builder.ToString();

        StringAssert.Contains(result, $"DOCKER_HOST = \"{dockerHost}\"");
        StringAssert.Contains(result, $"TESTCONTAINERS_HOST_OVERRIDE = \"{containerAddress}\"");
        StringAssert.Contains(result, $"\"host.docker.internal\" = \"{gatewayAddress}\"");
        Assert.IsFalse(result.Contains("host-gateway", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AddExtraHosts_ShouldSkipMissingTestTarget()
    {
        var text = "target \"_common\" {}\n";
        var builder = new StringBuilder(text);

        BakeHclHelper.AddExtraHosts(builder, "172.18.0.2", "172.18.0.1");
        var result = builder.ToString();

        Assert.AreEqual(text, result);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  secret = []\n")]
    [DataRow("  secret = [\"type=env,id=existing\"]\n")]
    [DataRow("  secret = [\"type=env,id=existing\",]\n")]
    public void AddSecret_ShouldAppendToCommonTarget(string attribute)
    {
        var builder = new StringBuilder("target \"_common\" {\n" + attribute + "}\ntarget \"other\" {}\n");

        BakeHclHelper.AddSecret(builder, "type=file,id=npmrc,src=.buildcharts/secrets/npmrc");
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

        BakeHclHelper.AddSecret(builder, "type=file,id=custom,src=C:\\secrets\\\"feed\"");
        var result = builder.ToString();

        StringAssert.Contains(result, "src=C:\\\\secrets\\\\\\\"feed\\\"\"");
    }

    [TestMethod]
    [DataRow("type=env,id=custom")]
    [DataRow("type=file,id=custom,src=https://example.com/secrets#feed")]
    [DataRow("type=file,id=custom,src=secrets/*feed*/")]
    public void AddSecret_ShouldIgnoreDuplicateSecret(string secret)
    {
        var builder = new StringBuilder("target \"_common\" {}\n");
        BakeHclHelper.AddSecret(builder, secret);
        var original = builder.ToString();

        BakeHclHelper.AddSecret(builder, secret);
        var result = builder.ToString();

        Assert.AreEqual(original, result);
    }

    [TestMethod]
    [DataRow("# existing secret")]
    [DataRow("// existing secret")]
    [DataRow("/* existing secret */")]
    [DataRow("# existing secret ] } \"")]
    [DataRow("// existing secret ] } \"")]
    [DataRow("/* existing secret ] } \" */")]
    public void AddSecret_ShouldPreserveTrailingCommaBeforeComment(string comment)
    {
        var builder = new StringBuilder("target \"_common\" {\n  secret = [\n    \"type=env,id=existing\", " + comment + "\n  ]\n}\n");

        BakeHclHelper.AddSecret(builder, "type=env,id=custom");
        var result = builder.ToString();

        Assert.AreEqual("target \"_common\" {\n  secret = [\n    \"type=env,id=existing\", " + comment + "\n  \n    \"type=env,id=custom\"\n  ]\n}\n", result);
    }

    [TestMethod]
    [DataRow("target \"other\" {}")]
    [DataRow("target \"_common\" {")]
    [DataRow("target \"_common\" {\n  secret = [\n}")]
    public void AddSecret_ShouldRejectMissingOrMalformedTarget(string text)
    {
        var builder = new StringBuilder(text);

        var result = Assert.Throws<InvalidOperationException>(() => BakeHclHelper.AddSecret(builder, "type=env,id=custom"));

        Assert.IsNotNull(result);
        Assert.AreEqual(text, builder.ToString());
    }

    [TestMethod]
    [DataRow("{ nested = { value = 1 } }", '{', '}')]
    [DataRow("{ value = \"}\\\"{\" }", '{', '}')]
    [DataRow("[\"escaped\\\" ]\", [\"nested\"]]", '[', ']')]
    public void FindClosingDelimiter_ShouldFindClosingDelimiter(string text, char opening, char closing)
    {
        var result = BakeHclHelper.FindClosingDelimiter(text, 0, opening, closing);

        Assert.AreEqual(text.Length - 1, result);
    }

    [TestMethod]
    [DataRow("{ value = { }", '{', '}')]
    [DataRow("[\"value\"", '[', ']')]
    public void FindClosingDelimiter_ShouldReturnMinusOneForUnclosedDelimiter(string text, char opening, char closing)
    {
        var result = BakeHclHelper.FindClosingDelimiter(text, 0, opening, closing);

        Assert.AreEqual(-1, result);
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
            BakeHclHelper.AddSecret(builder, "type=env,id=custom");
        }

        await plugin.OnAfterGenerateAsync(null, null, builder, default);
        BakeHclHelper.AddSecret(builder, "type=env,id=custom");
        await plugin.OnAfterGenerateAsync(null, null, builder, default);
        var result = builder.ToString();

        Assert.AreEqual(1, result.Split("secret =").Length - 1);
        Assert.AreEqual(1, result.Split("id=custom").Length - 1);
        Assert.AreEqual(1, result.Split("id=ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS,").Length - 1);
        Assert.AreEqual(1, result.Split("id=ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN,").Length - 1);
    }
}
