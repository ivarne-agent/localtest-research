# Vendored Altinn Events built with the localtest overrides.
# Build context: the component folder (localtest/components/events).
#
# Mirrors vendor/Dockerfile (the upstream one) line for line: same pinned base images, the DbTools build, build and
# publish, the Migration copy from the build stage, the non-root dotnet user. Every difference is marked "localtest:".
# When re-pinning, diff vendor/Dockerfile against the previous pin and carry the change over here (see
# vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-alpine3.24@sha256:230c3fbae0ceedc70bca04eed2bfb5694e8b248cadbb81010f41b7a993601f55 AS build

# localtest: upstream copies src/Events, src/DbTools and src/Events.Common to /Events, /DbTools and /Events.Common.
# Here they keep the component layout under /src (vendor/ next to the overrides, which decide "vendored" by that
# path), with fakes/ and the overrides beside them. The two variables redirect MSBuild's Directory.Build.props/targets
# lookup to the overrides: they define LOCALTEST and compile fakes/ into the host. No vendored csproj is edited.
WORKDIR /src
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
COPY vendor/src/Events vendor/src/Events
COPY vendor/src/DbTools vendor/src/DbTools
COPY vendor/src/Events.Common vendor/src/Events.Common
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets

WORKDIR /src/vendor/src/DbTools
RUN dotnet build ./DbTools.csproj -c Release -o /app_tools

# Build the Events project
WORKDIR /src/vendor/src/Events
RUN dotnet build ./Altinn.Platform.Events.csproj -c Release -o /app_output
RUN dotnet publish ./Altinn.Platform.Events.csproj -c Release -o /app_output

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-alpine3.24@sha256:819bd3649d8bf53f35527c68d62e17b348ef6f24ae73073f497f00a878348062 AS final

EXPOSE 5080
WORKDIR /app
COPY --from=build /app_output .

COPY --from=build /src/vendor/src/Events/Migration ./Migration

# setup the user and group
# the user will have no password, using shell /bin/false and using the group dotnet
RUN addgroup -g 3000 dotnet && adduser -u 1000 -G dotnet -D -s /bin/false dotnet
# update permissions of files if neccessary before becoming dotnet user
USER dotnet
RUN mkdir /tmp/logtelemetry

# localtest: settings production gets from its deployment. Development's Debug logging was mostly the claim loop's
# Npgsql lines, so the default is Information (the environment still wins). No HEALTHCHECK: podman builds OCI images,
# which drop the instruction (only a compose healthcheck: takes effect).
ENV ASPNETCORE_ENVIRONMENT=Development \
    Logging__LogLevel__Default=Information \
    ASPNETCORE_URLS=http://+:5080

ENTRYPOINT ["dotnet", "Altinn.Platform.Events.dll"]
