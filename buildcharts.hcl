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
  context = "."
  target = "build"
  dockerfile-inline = <<BUILDCHARTS_EOF
###################################
# Target: build
###################################
FROM base AS build

WORKDIR /src
COPY . .

ENV DOTNET_CLI_TELEMETRY_OPTOUT=true
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
ENV DOTNET_NOLOGO=true

ARG BUILDCHARTS_SRC
ARG VERSION
ARG CONFIGURATION=Release
ARG COMMIT

RUN dotnet clean \
    --configuration $CONFIGURATION

RUN dotnet restore \
    --locked-mode \
    --use-lock-file

RUN dotnet build /src/$BUILDCHARTS_SRC \
    --no-restore \
    --configuration $CONFIGURATION \
    -p:Version=$VERSION \
    -p:ContinuousIntegrationBuild=true \
    -p:SourceRevisionId=$COMMIT
BUILDCHARTS_EOF
  args = {
    BUILDCHARTS_SRC = "buildcharts.sln"
    BUILDCHARTS_TYPE = "build"
  }
  contexts = {
    base = "docker-image://mcr.microsoft.com/dotnet/sdk:9.0"
  }
  output = ["type=cacheonly,mode=max"]
}

target "nuget" {
  inherits = ["_common"]
  target = "nuget"
  dockerfile-inline = <<BUILDCHARTS_EOF
###################################
# Target: nuget
###################################
FROM build AS nuget

ARG BUILDCHARTS_SRC
ARG VERSION
ARG CONFIGURATION=Release
ARG PACK_OUTPUT=/output

RUN dotnet pack /src/$BUILDCHARTS_SRC \
    --no-build \
    --configuration $CONFIGURATION \
    --output $PACK_OUTPUT \
    /p:PackageVersion=$VERSION
BUILDCHARTS_EOF
  args = {
    BUILDCHARTS_SRC = "src/BuildCharts.Tool/BuildCharts.Tool.csproj"
    BUILDCHARTS_TYPE = "nuget"
  }
  contexts = {
    build = "target:build"
  }
  output = ["type=cacheonly,mode=max"]
}

target "docker" {
  inherits = ["_common"]
  target = "docker"
  dockerfile-inline = <<BUILDCHARTS_EOF
###################################
# Target: pre-docker
###################################
FROM build AS publish

ARG BUILDCHARTS_SRC
ARG CONFIGURATION=Release
ARG PUBLISH_OUTPUT=/output

RUN dotnet publish /src/$BUILDCHARTS_SRC \
    --no-build \
    --no-restore \
    --configuration $CONFIGURATION \
    --output $PUBLISH_OUTPUT

RUN DLL_NAME=$(basename "$BUILDCHARTS_SRC" .csproj).dll && \
    echo '#!/bin/sh' > /entrypoint.sh && \
    echo "exec dotnet /app/$DLL_NAME \"\$@\"" >> /entrypoint.sh && \
    chmod +x /entrypoint.sh

###################################
# Target: docker
###################################
FROM base AS docker

WORKDIR /app
COPY --link --from=publish /entrypoint.sh /entrypoint.sh
COPY --link --from=publish /output .
ENTRYPOINT ["/entrypoint.sh"]
BUILDCHARTS_EOF
  args = {
    BUILDCHARTS_SRC = "src/BuildCharts.Tool/BuildCharts.Tool.csproj"
    BUILDCHARTS_TYPE = "docker"
  }
  contexts = {
    build = "target:build"
    base = "docker-image://mcr.microsoft.com/dotnet/aspnet:9.0"
  }
  tags = [
    "docker.io/buildcharts/buildcharts:${VERSION}-${COMMIT}"
  ]
  output = ["type=docker"]
}

target "output" {
  dockerfile-inline = <<BUILDCHARTS_EOF
FROM scratch AS output
COPY --link --from=nuget /output /nuget
BUILDCHARTS_EOF
  contexts = {
    nuget = "target:nuget"
  }
  output = [
    "type=local,dest=.buildcharts/output"
  ]
}

group "default" {
  targets = ["build", "nuget", "docker", "output"]
}
