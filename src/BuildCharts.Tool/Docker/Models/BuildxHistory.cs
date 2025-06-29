using System;
using System.Text.Json.Serialization;

namespace BuildCharts.Tool.Docker.Models;

public record BuildxHistory
{
    [JsonPropertyName("cached_steps")]
    public int CachedSteps { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTime CompletedAt { get; set; }

    [JsonPropertyName("completed_steps")]
    public int CompletedSteps { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("ref")]
    public string Ref { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("total_steps")]
    public int TotalSteps { get; set; }
}