using BuildCharts.Tool.Oras;
using BuildCharts.Tool.Scaffolding;
using BuildCharts.Tool.Scaffolding.Detection;
using BuildCharts.Tool.Scaffolding.Generation;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Commands;

[Command(Name = "init", Description = "Scaffolds")]
public class InitCommand
{
    [Option("--template", Description = "OCI reference to scaffold template")]
    public string? Template { get; set; }

    public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken ct)
    {
        var gitProvider = await GitProviderDetector.DetectAsync(ct);

        string? templateRef = Template;

        if (string.IsNullOrEmpty(templateRef))
        {
            var projectType = await ProjectDetector.DetectAsync(ct);
            if (projectType == ProjectType.DotNet)
            {
                templateRef = "oci://docker.io/buildcharts/templates/dotnet-scaffold:latest";
            }
        }

        if (!string.IsNullOrEmpty(templateRef))
        {
            var scaffoldDir = Path.Combine(".buildcharts", "scaffold");
            await OrasClient.Pull(templateRef, scaffoldDir);
            CopyDirectory(scaffoldDir, Directory.GetCurrentDirectory());
            Console.WriteLine($"Scaffolded using template: {templateRef}");
            return 0;
        }

        var project = await BuildConfig.CreateBuildConfig("build.yml", ct);
        await Helm.CreateChart("charts/buildcharts/Chart.yaml", ct);

        if (gitProvider == GitProvider.GitHub)
        {
            await GitHub.CreateWorkflow(".github/workflows/buildcharts.yml", ct);
        }

        Console.WriteLine("buildcharts initialized");
        Console.WriteLine("");

        Console.WriteLine("✅ Generated files:");
        Console.WriteLine($"   • \u001b[2mbuild.yml\u001b[22m");
        Console.WriteLine("   • \u001b[2mcharts/buildcharts/Chart.yaml\u001b[22m");
        Console.WriteLine();
        Console.WriteLine("✅ Targets:");

        foreach (var (path, types) in project.OrderBy(p => p.Key))
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

    string Highlight(string cmd) =>
        $"\u001b[36m`{cmd}`\u001b[0m";

    static void CopyDirectory(string sourceDir, string destDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destPath = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, overwrite: true);
        }
    }
}