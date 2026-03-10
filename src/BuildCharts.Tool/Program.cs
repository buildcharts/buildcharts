using BuildCharts.Tool.Chart;
using BuildCharts.Tool.Commands;
using BuildCharts.Tool.Oras;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace BuildCharts.Tool;

public class Program
{
    public async static Task Main(string[] args)
    {
        // Enable emojis in console output.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        await Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IOrasClient, OrasClient>();
                services.AddSingleton<ChartManager>();
                services.AddOptions<ChartOptions>();
            })
            .RunCommandLineApplicationAsync<RootCommand>(args);
    }
}
