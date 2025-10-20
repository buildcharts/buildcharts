using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildCharts.Tool.Init.Generation;

public static class GitHub
{
    public static async Task CreateWorkflow(string outputPath, CancellationToken ct)
    {
        Directory.CreateDirectory(".github/workflows");

        const string text = """
                            on:
                              push:
                                branches: [main]
                              pull_request:

                            jobs:
                              build:
                                runs-on: ubuntu-latest
                                steps:
                                  - uses: actions/checkout@v4
                                  
                                  - name: Set up BuildCharts
                                    uses: buildcharts/setup-action@v1
                                  
                                  - name: Generate BuildCharts
                                    uses: buildcharts/generate-action@v1

                                  - name: Set up Docker Buildx
                                    uses: docker/setup-buildx-action@v3

                                  - name: Docker build and test
                                    uses: docker/bake-action@v6
                                    with:
                                      files: .buildcharts/docker-bake.hcl
                                    env:
                                      VERSION: ${{ github.ref_name }}
                                      COMMIT: ${{ github.sha }}
                            """;

        await File.WriteAllTextAsync(outputPath, text, ct);
    }
}