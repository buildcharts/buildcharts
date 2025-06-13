using BuildCharts.Tool.Commands;
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

        await Host.CreateDefaultBuilder(args).RunCommandLineApplicationAsync<RootCommand>(args);
    }
}