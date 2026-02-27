$env:VERSION = "1.0.0"
$env:COMMIT = (& git rev-parse HEAD)

dotnet tool restore
dotnet buildcharts generate
docker buildx bake --file .buildcharts/docker-bake.hcl --no-cache

& "$PSScriptRoot\..\Parse-Trx.ps1" ".buildcharts/output/test"
