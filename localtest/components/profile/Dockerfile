# Vendored Altinn Profile built with the localtest overrides.
# Build context: the component folder (localtest/components/profile).
#
# Mirrors vendor/Dockerfile (the upstream one) line for line: same pinned base images, the layered restore, publish,
# the Migration copy, the non-root dotnet user, /tmp/logtelemetry. Every difference is marked "localtest:". When
# re-pinning, diff that file against the previous pin and carry the change over here (see vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-alpine3.24@sha256:230c3fbae0ceedc70bca04eed2bfb5694e8b248cadbb81010f41b7a993601f55 AS build
WORKDIR /app

# localtest: upstream's build context is the repository root and its tree sits directly under /app. Here the same tree
# keeps its layout under /app/vendor (vendor/ next to the overrides, which decide "vendored" by that path), with fakes/
# and the overrides beside it. The two variables redirect MSBuild's Directory.Build.props/targets lookup to the
# overrides: they define LOCALTEST and compile fakes/ into the host. No vendored csproj is edited.
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
ENV DirectoryBuildPropsPath=/app/localtest.build.props \
    DirectoryBuildTargetsPath=/app/localtest.build.targets

# localtest: paths under vendor/
COPY vendor/src/Altinn.Profile/*.csproj ./vendor/src/Altinn.Profile/
COPY vendor/src/Altinn.Profile.Models/*.csproj ./vendor/src/Altinn.Profile.Models/
COPY vendor/src/Altinn.Profile.Core/*.csproj ./vendor/src/Altinn.Profile.Core/
COPY vendor/src/Altinn.Profile.Integrations/*.csproj ./vendor/src/Altinn.Profile.Integrations/

RUN dotnet restore ./vendor/src/Altinn.Profile/Altinn.Profile.csproj

COPY vendor/src ./vendor/src
RUN dotnet publish -c Release -o /app_output ./vendor/src/Altinn.Profile/Altinn.Profile.csproj

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-alpine3.24@sha256:819bd3649d8bf53f35527c68d62e17b348ef6f24ae73073f497f00a878348062 AS final
EXPOSE 5030
WORKDIR /app

COPY --from=build /app_output .
COPY --from=build /app/vendor/src/Altinn.Profile.Integrations/Migration ./Migration

# setup the user and group
# the user will have no password, using shell /bin/false and using the group dotnet
RUN addgroup -g 3000 dotnet && adduser -u 1000 -G dotnet -D -s /bin/false dotnet

# update permissions of files if neccessary before becoming dotnet user
USER dotnet
RUN mkdir /tmp/logtelemetry

# localtest: settings production gets from its deployment (upstream's docker-compose.yml sets ASPNETCORE_URLS).
# Development makes the JWT handler read the OpenID metadata over http. Information instead of Development's Debug
# logging (the environment still wins). No HEALTHCHECK: podman builds OCI images, which drop the instruction.
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5030 \
    Logging__LogLevel__Default=Information

ENTRYPOINT ["dotnet", "Altinn.Profile.dll"]
