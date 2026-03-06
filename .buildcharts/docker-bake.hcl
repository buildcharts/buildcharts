variable "VERSION" {
  default = "1.0.0"
}
variable "COMMIT" {
  default = "local"
}

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
  output = [
    "type=cacheonly,mode=max",
    "type=local,dest=.buildcharts/output/nuget"
  ]
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

target "test" {
  inherits = ["_common"]
  target = "test"
  name = "${item.name}"
  output = [
    "type=cacheonly,mode=max",
    "type=local,dest=.buildcharts/output/test"
  ]
  matrix = {
    item = [
      {
        name = "test",
        src  = "tests/BuildCharts.Tool/BuildCharts.Tests.csproj"
      },
    ]
  }
  args = {
    BUILDCHARTS_SRC = item.src
    BUILDCHARTS_TYPE = "test"
  }
  contexts = {
    build = "target:build"
  }
  dockerfile = "./.buildcharts/dotnet-test/Dockerfile"
}

group "default" {
  targets = ["build", "nuget", "docker", "test"]
}
