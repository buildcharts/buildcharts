using BuildCharts.Tool.Docker.Json;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BuildCharts.Tool.Docker.Models;

public sealed class BuildxInspect
{
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    [JsonPropertyName("Ref")]
    public string Ref { get; set; }

    [JsonPropertyName("Dockerfile")]
    public string Dockerfile { get; set; }

    [JsonPropertyName("VCSRepository")]
    public string VCSRepository { get; set; }

    [JsonPropertyName("VCSRevision")]
    public string VCSRevision { get; set; }

    [JsonPropertyName("Target")]
    public string Target { get; set; }

    [JsonPropertyName("NamedContexts")]
    public List<NamedContext> NamedContexts { get; set; } = [];

    [JsonPropertyName("StartedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("CompletedAt")]
    public DateTimeOffset CompletedAt { get; set; }

    [JsonPropertyName("Duration")]
    public long Duration { get; set; }

    [JsonPropertyName("Status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("NumCompletedSteps")]
    public int NumCompletedSteps { get; set; }

    [JsonPropertyName("NumTotalSteps")]
    public int NumTotalSteps { get; set; }

    [JsonPropertyName("NumCachedSteps")]
    public int NumCachedSteps { get; set; }

    [JsonPropertyName("BuildArgs")]
    public List<BuildArg> BuildArgs { get; set; } = [];

    [JsonPropertyName("Config")]
    public Config Config { get; set; } = new();

    [JsonPropertyName("Materials")]
    public List<Material> Materials { get; set; } = [];

    [JsonPropertyName("Attachments")]
    public List<Attachment> Attachments { get; set; } = [];
}


public sealed class NamedContext
{
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    [JsonPropertyName("Value")]
    public string Value { get; set; }
}

public sealed class BuildArg
{
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    [JsonPropertyName("Value")]
    public string Value { get; set; }
}

public sealed class Config
{
    [JsonPropertyName("ImageResolveMode")]
    public string ImageResolveMode { get; set; }

    [JsonPropertyName("RestRaw")]
    [JsonConverter(typeof(RestRawDictConverter))]
    public Dictionary<string, string> RestRaw { get; set; } = [];

    [JsonPropertyName("Env")]
    public List<string> Env { get; set; }

    [JsonPropertyName("Cmd")]
    public List<string> Cmd { get; set; }

    [JsonPropertyName("WorkingDir")]
    public string WorkingDir { get; set; }
}

public sealed class Material
{
    [JsonPropertyName("URI")]
    public string URI { get; set; } = "";

    [JsonPropertyName("Digests")]
    public List<string> Digests { get; set; } = [];
}

public sealed class Attachment
{
    [JsonPropertyName("Digest")]
    public string Digest { get; set; } = "";

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "";
}