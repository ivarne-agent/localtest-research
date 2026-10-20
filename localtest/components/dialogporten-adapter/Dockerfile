# Vendored Altinn Dialogporten adapter built with the localtest overrides.
# Build context: the component folder (localtest/components/dialogporten-adapter):
#   podman build -t localtest-dialogporten-adapter:dev .
#
# Mirrors vendor/Dockerfile (the upstream one) line for line: same pinned base images, restore of the WebApi project,
# `dotnet build -c Release -o out` (not publish, as upstream), the dotnet user with uid/gid 3000 and /tmp/logtelemetry.
# Every difference is marked "localtest:". When re-pinning, diff vendor/Dockerfile against the previous pin and carry
# the change over here (see vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
# localtest: upstream's build context is the repository root with the build in /app. Here the same files keep their
# layout under /src/vendor (vendor/ next to the overrides, which decide "vendored" by that path), with fakes/, the
# overrides and the component's .editorconfig beside it. The two variables redirect MSBuild's Directory.Build.props/
# targets lookup to the overrides, which import upstream's Directory.Build.props, define LOCALTEST and compile fakes/
# into the host. No vendored csproj is edited. As upstream, global.json stays out of the build context.
WORKDIR /src
COPY localtest.build.props localtest.build.targets .editorconfig ./
COPY fakes/ fakes/
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
WORKDIR /src/vendor

COPY ["vendor/Directory.Build.props", "."]

# Main project
COPY vendor/src/Altinn.DialogportenAdapter.WebApi/*.csproj ./src/Altinn.DialogportenAdapter.WebApi/
# Dependencies
COPY vendor/src/Altinn.DialogportenAdapter.Contracts/*.csproj ./src/Altinn.DialogportenAdapter.Contracts/
# Restore project
RUN dotnet restore ./src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj

# Copy everything else and build
COPY vendor/src ./src
# localtest: -o /app/out instead of out under the working directory, so the final stage copies the same path as upstream
RUN dotnet build -c Release -o /app/out ./src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS final
WORKDIR /app
EXPOSE 5011

COPY --from=build /app/out .

RUN groupadd --gid 3000 dotnet && useradd --uid 3000 --gid dotnet --no-create-home --shell /bin/false dotnet
USER dotnet
RUN mkdir /tmp/logtelemetry

# localtest: the settings production gets from its deployment. Development only selects appsettings.Development.json
# (overridden by the localtest defaults) and the optional appsettings.local.json; no LocalDevelopment section exists, so
# the adapter's own mocks stay off. No HEALTHCHECK: podman builds OCI images, which drop the instruction.
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5011
ENTRYPOINT [ "dotnet", "Altinn.DialogportenAdapter.WebApi.dll" ]
