dotnet tool install --global dotnet-buildcharts --add-source ./.buildcharts/output/nuget
buildcharts generate
dotnet tool uninstall --global dotnet-buildcharts
$env:VERSION="1.0.0"; $env:COMMIT="abc123"; docker buildx bake --file .buildcharts/docker-bake.hcl
dotnet tool install --global dotnet-buildcharts --add-source ./.buildcharts/output/nuget