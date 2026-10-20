# Vendored Altinn Notifications (the orders API host) built with the localtest overrides.
# Build context: the component folder (localtest/components/notifications).
#
# Mirrors vendor/components/api/Dockerfile (the upstream one) line for line: same pinned base images, the layered
# restore, publish, tzdata, the Migration and Views copies, the non-root dotnet user. Every difference is marked
# "localtest:". When re-pinning, diff that file against the previous pin and carry the change over here (see
# vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-alpine3.24@sha256:230c3fbae0ceedc70bca04eed2bfb5694e8b248cadbb81010f41b7a993601f55 AS build

# localtest: upstream's build context is the repository root and it copies to /app. Here the same tree keeps its layout
# under /app/vendor (vendor/ next to the overrides, which decide "vendored" by that path), with fakes/ and the overrides
# beside it. The two variables redirect MSBuild's Directory.Build.props/targets lookup to the overrides: they import the
# vendored ones, define LOCALTEST and compile fakes/ into the host. No vendored csproj is edited.
WORKDIR /app
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
ENV DirectoryBuildPropsPath=/app/localtest.build.props \
    DirectoryBuildTargetsPath=/app/localtest.build.targets

# CPM / build configuration
# localtest: from vendor/, which is upstream's repository root here
COPY vendor/Directory.Packages.props vendor/Directory.Build.props vendor/Directory.Build.targets vendor/

# Copy csproj and restore as distinct layers
# Paths mirror the repo structure so cross-component project references resolve correctly
COPY vendor/components/api/src/Altinn.Notifications/*.csproj ./vendor/components/api/src/Altinn.Notifications/
COPY vendor/components/api/src/Altinn.Notifications.Core/*.csproj ./vendor/components/api/src/Altinn.Notifications.Core/
COPY vendor/components/api/src/Altinn.Notifications.Integrations/*.csproj ./vendor/components/api/src/Altinn.Notifications.Integrations/
COPY vendor/components/api/src/Altinn.Notifications.Persistence/*.csproj ./vendor/components/api/src/Altinn.Notifications.Persistence/
COPY vendor/components/shared/src/Altinn.Notifications.Shared/*.csproj ./vendor/components/shared/src/Altinn.Notifications.Shared/

RUN dotnet restore ./vendor/components/api/src/Altinn.Notifications/Altinn.Notifications.csproj

# Copy everything else and build
COPY vendor/components/api/src ./vendor/components/api/src
COPY vendor/components/shared/src ./vendor/components/shared/src

RUN dotnet publish -c Release -o out ./vendor/components/api/src/Altinn.Notifications/Altinn.Notifications.csproj

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-alpine3.24@sha256:819bd3649d8bf53f35527c68d62e17b348ef6f24ae73073f497f00a878348062 AS final

WORKDIR /app

EXPOSE 5090

# Installing package for time zone functionality
RUN apk add --no-cache tzdata

COPY --from=build /app/out .
COPY --from=build /app/vendor/components/api/src/Altinn.Notifications.Persistence/Migration ./Migration
COPY vendor/components/api/src/Altinn.Notifications/Views ./Views

RUN addgroup -g 3000 dotnet && adduser -u 1000 -G dotnet -D -s /bin/false dotnet

USER dotnet

RUN mkdir /tmp/logtelemetry

# localtest: settings production gets from its deployment. Development makes the JWT handler read the OpenID metadata
# over http; its migrations are looked up in the parent of the working directory, hence the path relative to / (the
# Migration folder above is /app/Migration). Information instead of Development's Debug logging (the environment still
# wins). No HEALTHCHECK: podman builds OCI images, which drop the instruction (only a compose healthcheck: takes effect).
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5090 \
    PostgreSQLSettings__MigrationScriptPath=app/Migration \
    Logging__LogLevel__Default=Information

ENTRYPOINT [ "dotnet", "Altinn.Notifications.dll" ]
