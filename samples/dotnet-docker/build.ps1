$env:VERSION = "1.0.0"
$env:COMMIT = (& git rev-parse HEAD)

dotnet tool restore
dotnet buildcharts generate
docker buildx bake --file .buildcharts/docker-bake.hcl --no-cache
docker run docker.io/eddietisma/dotnet-docker:$($env:VERSION)-$($env:COMMIT)