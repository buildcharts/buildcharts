$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$env:VERSION = "1.0.0"
$env:COMMIT = (& git rev-parse HEAD)

dotnet tool restore
dotnet buildcharts generate
docker buildx bake --file .buildcharts/docker-bake.hcl --no-cache --set build.context=src --set build.dockerfile=../.buildcharts/npm-build/Dockerfile

docker run --rm --publish 8080:8080 --env ENVIRONMENT=Development docker.io/eddietisma/npm-docker:$($env:VERSION)-$($env:COMMIT)
