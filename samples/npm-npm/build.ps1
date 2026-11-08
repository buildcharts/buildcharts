$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$env:VERSION = "1.0.0"
$env:COMMIT = (& git rev-parse HEAD)

dotnet tool restore
dotnet buildcharts generate
docker buildx bake --file .buildcharts/docker-bake.hcl --no-cache --set build.context=src --set build.dockerfile=../.buildcharts/npm-build/Dockerfile

Get-ChildItem -Path ./.buildcharts/output/npm -Filter *.tgz -File |
  Select-Object Name, Length, LastWriteTime
