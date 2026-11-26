$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$env:VERSION = "1.0.0"
$env:COMMIT = (& git rev-parse HEAD)

dotnet run --no-launch-profile --project ../../src/BuildCharts.Tool -- generate
docker buildx bake --file .buildcharts/docker-bake.hcl --no-cache

$artifactPath = ".buildcharts/output/maven"
Get-Item "$artifactPath/java-maven-$env:VERSION.jar", "$artifactPath/java-maven-$env:VERSION.pom" |
  Select-Object Name, Length, LastWriteTime
