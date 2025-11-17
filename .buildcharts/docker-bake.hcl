variable "VERSION" {}
variable "COMMIT" {}

target "_common" {
  args = {
    VERSION = "${VERSION}"
    COMMIT = "${COMMIT}"
  }
}

target "build" {
  inherits = ["_common"]
  target = "build"
  name = "${item.name}"
  context = "."
  output = ["type=cacheonly,mode=max"]
  matrix = {
    item = [
      {
        name = "build",
        src  = "buildcharts.sln"
        base = "mcr.microsoft.com/dotnet/sdk:10.0"
      },
    ]
  }
  args = {
    BUILDCHARTS_SRC = item.src
    BUILDCHARTS_TYPE = "build"
  }
  contexts = {
    base = "docker-image://${item.base}"
  }
  dockerfile = "./.buildcharts/dotnet-build/Dockerfile"
}

target "nuget" {
  inherits = ["_common"]
  target = "nuget"
  name = "${item.name}"
  output = ["type=cacheonly,mode=max"]
  matrix = {
    item = [
      {
        name = "nuget",
        src  = "src/BuildCharts.Tool/BuildCharts.Tool.csproj"
      },
    ]
  }
  args = {
    BUILDCHARTS_SRC = item.src
    BUILDCHARTS_TYPE = "nuget"
  }
  contexts = {
    build = "target:build"
  }
  dockerfile = "./.buildcharts/dotnet-nuget/Dockerfile"
}

target "docker" {
  inherits = ["_common"]
  target = "docker"
  name = "${item.name}"
  output = ["type=docker"]
  matrix = {
    item = [
      {
        name = "docker",
        src  = "src/BuildCharts.Tool/BuildCharts.Tool.csproj"
        tags = ["docker.io/buildcharts/buildcharts:${VERSION}-${COMMIT}"]
        base = "mcr.microsoft.com/dotnet/runtime:10.0"
      },
    ]
  }
  args = {
    BUILDCHARTS_SRC = item.src
    BUILDCHARTS_TYPE = "docker"
  }
  tags = "${item.tags}"
  contexts = {
    build = "target:build"
    base = "docker-image://${item.base}"
  }
  dockerfile = "./.buildcharts/dotnet-docker/Dockerfile"
}

target "output" {
  output = ["type=local,dest=.buildcharts/output"]
  contexts = {
    nuget = "target:nuget"
  }
  dockerfile-inline = <<EOF
FROM scratch AS output
COPY --link --from=nuget /output /nuget
EOF
}

group "default" {
  targets = ["build", "nuget", "docker", "output"]
}
