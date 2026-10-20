# Vendored Altinn Correspondence (the API host) built with the localtest overrides.
# Build context: the component folder (localtest/components/correspondence).
#
# Mirrors vendor/Dockerfile (the upstream one) line for line: the same base images, the layered restore of the six
# projects, publish, port 2525, the non-root dotnet user and /mnt/storage. Every difference is marked "localtest:". When
# re-pinning, diff that file against the previous pin and carry the change over here (see vendor/UPSTREAM.md).
# localtest: upstream's floating sdk:10.0 and aspnet:10.0 are pinned by digest (what 10.0 pointed at on 2026-10-20,
# 10.0.402 and 10.0.13), like every other component's images; bump them on a re-pin.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /app

# localtest: upstream's build context is the repository root. Here the same tree keeps its layout under /app/vendor
# (vendor/ next to the overrides, which decide "vendored" by that path), with fakes/ and the overrides beside it. The two
# variables redirect MSBuild's Directory.Build.props/targets lookup to the overrides: they define LOCALTEST and compile
# fakes/ into the host. No vendored csproj is edited.
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
ENV DirectoryBuildPropsPath=/app/localtest.build.props \
    DirectoryBuildTargetsPath=/app/localtest.build.targets

# Copy csproj files explicitly and restore as distinct layers
# localtest: from vendor/, which is upstream's repository root here
COPY ["vendor/src/Altinn.Correspondence.Common/Altinn.Correspondence.Common.csproj", "vendor/src/Altinn.Correspondence.Common/"]
COPY ["vendor/src/Altinn.Correspondence.Core/Altinn.Correspondence.Core.csproj", "vendor/src/Altinn.Correspondence.Core/"]
COPY ["vendor/src/Altinn.Correspondence.Persistence/Altinn.Correspondence.Persistence.csproj", "vendor/src/Altinn.Correspondence.Persistence/"]
COPY ["vendor/src/Altinn.Correspondence.Integrations/Altinn.Correspondence.Integrations.csproj", "vendor/src/Altinn.Correspondence.Integrations/"]
COPY ["vendor/src/Altinn.Correspondence.Application/Altinn.Correspondence.Application.csproj", "vendor/src/Altinn.Correspondence.Application/"]
COPY ["vendor/src/Altinn.Correspondence.API/Altinn.Correspondence.API.csproj", "vendor/src/Altinn.Correspondence.API/"]
RUN dotnet restore ./vendor/src/Altinn.Correspondence.API/Altinn.Correspondence.API.csproj

# Copy everything else and build
COPY vendor/src ./vendor/src
RUN dotnet publish -c Release -o out ./vendor/src/Altinn.Correspondence.API/Altinn.Correspondence.API.csproj

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS final
WORKDIR /app
EXPOSE 2525
ENV ASPNETCORE_URLS=http://+:2525

COPY --from=build /app/out .

# Create non-root user (Debian-based; tolerate if it already exists)
RUN groupadd -r dotnet || true \
    && useradd -r -g dotnet -d /app -s /usr/sbin/nologin dotnet || true

RUN mkdir -p /mnt/storage \
    && chown -R dotnet:dotnet /mnt/storage

# localtest: settings production gets from its deployment. Development applies the EF migrations at start, simulates the
# malware scan and skips the storage account deployment (the localtest defaults in fakes/LocaltestCorrespondence.cs do
# the rest). /mnt/storage is the file-backed blob store (studioctl mounts the correspondence-blobs volume there). No
# HEALTHCHECK: podman builds OCI images, which drop the instruction (studioctl checks /health).
ENV ASPNETCORE_ENVIRONMENT=Development

USER dotnet
ENTRYPOINT [ "dotnet", "Altinn.Correspondence.API.dll" ]
