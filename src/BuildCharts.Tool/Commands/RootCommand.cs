using McMaster.Extensions.CommandLineUtils;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

[Command(Name = "buildcharts", Description = "buildcharts CLI tool")]
[Subcommand(typeof(GenerateCommand))]
[Subcommand(typeof(InitCommand))]
[Subcommand(typeof(PullCommand))]
[Subcommand(typeof(SummaryCommand))]
[Subcommand(typeof(UpdateCommand))]
[Subcommand(typeof(VersionCommand))]
[VersionOptionFromMember("--version|-v", MemberName = nameof(GetVersion))]
public class RootCommand
{
    public Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken cancellationToken)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }

    public static string GetVersion()
    {
        var infoVersion = VersionCommand.GetProductVersion(typeof(Program).Assembly);
        var parts = infoVersion.Split('+');
        var version = parts[0];
        var build = parts.Length > 1 ? parts[1] : null;

        return build is null
            ? $"buildcharts v{version}"
            : $"buildcharts v{version}, build {build}";
    }
}
