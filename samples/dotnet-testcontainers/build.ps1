$env:VERSION = "1.0.0"
$env:COMMIT = (& git rev-parse HEAD)

dotnet tool restore
dotnet buildcharts generate

# Enable to start a watch on the buildcharts-dind container to see the build progress in real-time.
# if (docker ps --format "{{.Names}}" | Select-String -SimpleMatch "buildcharts-dind") {
#   Start-Process -FilePath "powershell" -ArgumentList "-NoExit", "-Command", "docker exec -it buildcharts-dind watch docker ps" | Out-Null
# }

docker buildx bake --file .buildcharts/docker-bake.hcl --no-cache

& "$PSScriptRoot\..\Parse-Trx.ps1" ".buildcharts/output/test"
