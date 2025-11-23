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
        src  = "src/dotnet-test.csproj"
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
        src  = "src/dotnet-test.csproj"
        base = "mcr.microsoft.com/dotnet/sdk:10.0"
      },
    ]
  }
  args = {
    BUILDCHARTS_SRC = item.src
    BUILDCHARTS_TYPE = "test"
  }
  contexts = {
    build = "target:build"
    base = "docker-image://${item.base}"
  }
  dockerfile = "./.buildcharts/dotnet-test/Dockerfile"
}

group "default" {
  targets = ["build", "test"]
}
