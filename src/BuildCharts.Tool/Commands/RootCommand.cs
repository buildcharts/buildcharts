using McMaster.Extensions.CommandLineUtils;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

[Command(Name = "buildcharts")]
[Subcommand(typeof(GenerateCommand))]
[Subcommand(typeof(InitCommand))]
[Subcommand(typeof(PullCommand))]
[Subcommand(typeof(SummaryCommand))]
[Subcommand(typeof(VersionCommand))]
public class RootCommand
{
    public Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken cancellationToken)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}