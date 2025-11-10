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
    public string Reference { get; set; }

    [Option("-u|--untar", Description = "Untar the downloaded chart into the current directory.")]
    public bool Untar { get; set; }

    [Option("--untardir <DIR>", Description = "Destination when using --untar (defaults to current directory).")]
    public string UntarDir { get; set; } = Environment.CurrentDirectory;

    [Option("-o|--output <OUTPUT>", Description = "Output directory (default: current)")]
    public string OutputDir { get; set; } = Environment.CurrentDirectory;

    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        try
        {
            await OrasClient.Pull(Reference, Untar, UntarDir, OutputDir, ct: ct);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}