using BuildCharts.Tool.Commands;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace BuildCharts.Tool;

public class Program
{
    public async static Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // Enable emojis in console output.

        try
        {
            await Host.CreateDefaultBuilder(args).RunCommandLineApplicationAsync<RootCommand>(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }
}