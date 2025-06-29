using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BuildCharts.Tool.Docker.Models;

public sealed class BuildxLog
{
    [JsonPropertyName("vertexes")]
    public List<Vertex> Vertexes { get; set; } = [];

    [JsonPropertyName("statuses")]
    public List<Status> Statuses { get; set; } = [];
}

public sealed class Vertex
{
    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    [JsonPropertyName("inputs")]
    public List<string> Inputs { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;


    [JsonPropertyName("started")]
    public DateTimeOffset? Started { get; set; }

    [JsonPropertyName("completed")]
    public DateTimeOffset? Completed { get; set; }

    [JsonPropertyName("cached")]
    public bool? Cached { get; set; }
}

public sealed class Status
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("vertex")]
    public string Vertex { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("current")]
    public long? Current { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("started")]
    public DateTimeOffset Started { get; set; }

    [JsonPropertyName("completed")]

    public DateTimeOffset Completed { get; set; }
}