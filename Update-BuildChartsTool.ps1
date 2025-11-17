Remove-Item .buildcharts/output/nuget -Recurse -Force -ErrorAction SilentlyContinue
dotnet tool uninstall --global dotnet-buildcharts
$env:VERSION="1.0.0"; $env:COMMIT=(& git rev-parse HEAD); docker buildx bake --file .buildcharts/docker-bake.hcl --no-cache
dotnet tool install --global dotnet-buildcharts --add-source ./.buildcharts/output/nuget