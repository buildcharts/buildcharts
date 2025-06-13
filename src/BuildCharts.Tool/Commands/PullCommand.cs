using BuildCharts.Tool.Oras;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

[Command("pull", Description = "Pull a build chart from an OCI registry")]
public class PullCommand
{
    [Argument(0, Name = "reference", Description = "OCI reference in the form registry/repo[:tag]")]
    [Required]
    public string Reference { get; set; } = default!;

    //[Option("-o|--output <OUTPUT>", Description = "Output directory (default: current)")]
    //public string OutputDir { get; set; } = ".";

    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        try
        {
            await OrasClient.Pull(Reference);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to pull: {ex.Message}");
            return 1;
        }
    }
}