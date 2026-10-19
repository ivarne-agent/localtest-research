# Vendored Altinn Authentication built with the localtest overrides.
# Build context: the component folder (localtest/components/authentication).
#
# Mirrors vendor/src/apps/Altinn.Authentication/Dockerfile (the upstream one) line for line: same pinned base images,
# the publish from the host project folder, the non-root dotnet user. Every difference is marked "localtest:". When
# re-pinning, diff that file against the previous pin and carry the change over here (see vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:3cc3bbbbf93d82104892f42aa9106b6be4d120346dea0649643a97c801525256 AS build
WORKDIR /src
# localtest: upstream copies the repository's src/ to /src. Here the sources keep the component layout under /src
# (vendor/ next to the overrides, which decide "vendored" by that path), and only vendor/src goes in, so the vendored
# root .editorconfig and global.json stay out as upstream's build leaves them out. The two variables redirect
# MSBuild's Directory.Build.props/targets lookup to the overrides: they import the vendored ones, define LOCALTEST
# and compile fakes/ into the host. No vendored csproj is edited.
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
COPY vendor/src vendor/src
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
WORKDIR /src/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:f62a272ac1b46e83f56b8ed0416572f31cd1128e2c4a5e63eb34d348e4a36095 AS final
EXPOSE 5040
WORKDIR /app
COPY --from=build /app .

# setup the user and group
# the user will have no password, using shell /bin/false and using the group dotnet
RUN addgroup -g 3000 dotnet && adduser -u 1000 -G dotnet -D -s /bin/false dotnet
# update permissions of files if neccessary before becoming dotnet user
USER dotnet
RUN mkdir /tmp/logtelemetry

# localtest: settings production gets from its deployment. The stack reaches the service on 5101 (the localtest
# app's upstream table), not upstream's 5040. No HEALTHCHECK: podman builds OCI images, which drop the instruction.
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5101

ENTRYPOINT ["dotnet", "Altinn.Platform.Authentication.dll"]
