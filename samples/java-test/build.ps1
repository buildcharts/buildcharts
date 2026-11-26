$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$env:VERSION = "1.0.0"

dotnet run --no-launch-profile --project ../../src/BuildCharts.Tool -- generate
docker buildx bake --file .buildcharts/docker-bake.hcl --no-cache

& "$PSScriptRoot/../Parse-JUnit.ps1" ".buildcharts/output/test"
Get-ChildItem -Path .buildcharts/output/test -Recurse -Filter coverage.cobertura.xml -File |
  Select-Object FullName, Length
