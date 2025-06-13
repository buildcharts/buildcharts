using BuildCharts.Tool.Scaffolding.Detection;
using BuildCharts.Tool.Scaffolding.Generation;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

[Command(Name = "init", Description = "Scaffolds")]
public class InitCommand
{
    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        try
        {
            var projectType = await ProjectTypeDetector.DetectAsync(ct);
            if (projectType != ProjectType.DotNet)
            {
                Console.Error.WriteLine("Error: Unknown project type");
                return 1;
            }

            var buildConfig = await DotnetProvider.CreateBuildConfig("build.yml", ct);
            await Helm.CreateChart("charts/buildcharts/Chart.yaml", ct);

            var gitProvider = await GitProviderDetector.DetectAsync(ct);
            if (gitProvider == GitProvider.GitHub)
            {
                await GitHub.CreateWorkflow(".github/workflows/buildcharts.yml", ct);
            }

            Console.WriteLine("buildcharts initialized");
            Console.WriteLine("");
            Console.WriteLine("✅ Generated files:");
            Console.WriteLine("   • \u001b[2mbuild.yml\u001b[22m");
            Console.WriteLine("   • \u001b[2mcharts/buildcharts/Chart.yaml\u001b[22m");
            Console.WriteLine();
            Console.WriteLine("✅ Targets:");

            foreach (var (path, types) in buildConfig.OrderBy(p => p.Key))
            {
                Console.WriteLine($"   • \x1b[2m{path}\x1b[22m → {string.Join(", ", types.Types)}");
            }

            Console.WriteLine("");

            if (gitProvider == GitProvider.GitHub)
            {
                Console.WriteLine("✅ Detected GitHub from .git folder:");
                Console.WriteLine("   • \u001b[2m.github/workflows/buildcharts.yml\u001b[22m");
                Console.WriteLine("");
            }

            Console.WriteLine("👉 Next steps:");
            Console.WriteLine($"   • Edit {Highlight("build.yml")} to customize build pipeline");
            Console.WriteLine($"   • Run {Highlight("buildcharts generate")} to generate build pipeline");
            Console.WriteLine($"   • Run {Highlight("docker buildx bake")} to run build pipeline");
            Console.WriteLine();
            Console.WriteLine("💡 Tips:");
            Console.WriteLine($"   • Run {Highlight("buildcharts update")} to auto-sync chart dependencies");
            Console.WriteLine($"   • Customize default base images and tags in {Highlight("charts/buildcharts/Chart.yaml")}");
            Console.WriteLine();

            // TODO: Add .buildcharts to .gitignore
            // TODO: Add .buildcharts to .dockerignore

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 0;
        }
    }

    string Highlight(string cmd) =>
        $"\u001b[36m`{cmd}`\u001b[0m";
}