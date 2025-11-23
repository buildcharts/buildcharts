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
        src  = "src/dotnet-docker.csproj"
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

target "docker" {
  inherits = ["_common"]
  target = "docker"
  name = "${item.name}"
  output = ["type=docker"]
  matrix = {
    item = [
      {
        name = "docker",
        src  = "src/dotnet-docker.csproj"
        tags = ["docker.io/eddietisma/dotnet-docker:${VERSION}-${COMMIT}"]
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

group "default" {
  targets = ["build", "docker"]
}
