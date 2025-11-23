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
        src  = "src/dotnet-nuget.csproj"
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
        src  = "src/dotnet-nuget.csproj"
        SOURCELINK_STRICT = "0"
      },
    ]
  }
  args = {
    BUILDCHARTS_SRC = item.src
    BUILDCHARTS_TYPE = "nuget"
    SOURCELINK_STRICT = "${item.SOURCELINK_STRICT}"
  }
  contexts = {
    build = "target:build"
  }
  dockerfile = "./.buildcharts/dotnet-nuget/Dockerfile"
}

group "default" {
  targets = ["build", "nuget"]
}
