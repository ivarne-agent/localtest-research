# Vendored Altinn Resource Registry built with the localtest overrides.
# Build context: the component folder (localtest/components/resource-registry).
#
# Mirrors vendor/src/apps/Altinn.ResourceRegistry/Dockerfile (the upstream one): same pinned base images, publish of
# the host project, the non-root dotnet user. Every difference is marked "localtest:". When re-pinning, diff that file
# against the previous pin and carry the change over here (see vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:d8ee39817ca03a3757288e83c37ed73cc969a286c603b827c7cbe33add1c2d1c AS build
# localtest: upstream's build context is the repository root and it copies src/ to /src. Here the same tree keeps its
# layout under /src/vendor/src (vendor/ next to the overrides, which decide "vendored" by that path), with fakes/ and
# the overrides beside it. The two variables redirect MSBuild's Directory.Build.props/targets lookup to the overrides:
# they define LOCALTEST and compile fakes/ into the host. No vendored csproj is edited.
WORKDIR /src
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
COPY vendor/src/ vendor/src/
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
WORKDIR /src/vendor/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry
RUN dotnet publish -c Release -o /app

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
EXPOSE 5100
WORKDIR /app

COPY --from=build /app .

# setup the user and group
# the user will have no password, using shell /bin/false and using the group dotnet
RUN addgroup -g 3000 dotnet && adduser -u 1000 -G dotnet -D -s /bin/false dotnet
# update permissions of files if neccessary before becoming dotnet user
USER dotnet
RUN mkdir /tmp/logtelemetry

# localtest: settings production gets from its deployment. No HEALTHCHECK: podman builds OCI images, which drop the
# instruction; studioctl polls the health endpoint instead.
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5100

ENTRYPOINT ["dotnet", "Altinn.ResourceRegistry.dll"]
