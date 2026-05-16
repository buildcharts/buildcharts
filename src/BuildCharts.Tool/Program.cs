using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Commands;
using BuildCharts.Tool.Oras;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BuildCharts.Tool;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        // Enable emojis in console output.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            return await Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton<IOrasClient, OrasClient>();
                    services.AddSingleton<ChartManager>();
                    services.AddOptions<ChartOptions>();
                })
                .RunCommandLineApplicationAsync<RootCommand>(args, app =>
                {
                    if (args.Length > 0 && !args.Any(IsInfoOption))
                    {
                        app.Out = TextWriter.Null;
                    }
                });
        }
        catch (UnrecognizedCommandParsingException ex)
        {
            var message = ex.Command.Name == "buildcharts" && !args[0].StartsWith('-') 
                ? $"unknown command '{string.Join(" ", args)}'"
                : ex.Message;

            await Console.Error.WriteLineAsync($"buildcharts: {message}");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync("Run 'buildcharts --help' for more information");

            return 1;
        }
    }

    private static bool IsInfoOption(string arg)
    {
        return arg is "--help" or "-h" or "-?" or "--version" or "-v";
    }
}
