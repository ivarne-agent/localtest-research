# Vendored Altinn Storage built with the localtest overrides.
# Build context: the component folder (localtest/components/storage).
#
# Mirrors vendor/Dockerfile (the upstream one) line for line: same pinned base images, the DbTools build that
# regenerates the latest migration's functions-and-procedures script during publish, the Migration copy from the build
# stage, the non-root user. Every difference is marked "localtest:". When re-pinning, diff vendor/Dockerfile against
# the previous pin and carry the change over here (see vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0-resolute@sha256:d8051d88928fb40f74c9aca6896a57851cdced277f4102e1c5aa6d4983e27bad AS build

# localtest: upstream copies src/Storage and src/DbTools to /Storage and /DbTools. Here they keep the component layout
# under /src (vendor/ next to the overrides), because the overrides decide "vendored" by that path. vendor/'s root
# Directory.Build.props is left out, as upstream's build context leaves out the repository root.
WORKDIR /src
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
COPY vendor/src/Storage vendor/src/Storage
COPY vendor/src/DbTools vendor/src/DbTools
# localtest: redirect MSBuild's Directory.Build.props/targets lookup to the overrides: defines LOCALTEST for the
# vendored projects and compiles fakes/ into the host. No vendored csproj is edited.
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets

WORKDIR /src/vendor/src/DbTools/
RUN dotnet build ./DbTools.csproj -c Release -o /app_tools

WORKDIR ../Storage/

RUN dotnet publish ./Altinn.Platform.Storage.csproj -c Release -o /app_output

# localtest: the blob folder, created here because the final image has no shell.
RUN mkdir -p /data/blobs

# -extra includes ICU and tzdata, needed for non-English cultures such as "no-NB"
FROM mcr.microsoft.com/dotnet/aspnet:10.0-resolute-chiseled-extra@sha256:e662e2bbca99a18b0922af83d794a212495e737e15ee8b56ffde9fcd24b0cba5 AS final
EXPOSE 5010
WORKDIR /app
COPY --from=build /app_output .
COPY --from=build /src/vendor/src/Storage/Migration ./Migration
# localtest: blobs live in a volume owned by the app user; settings production gets from its deployment.
# No HEALTHCHECK: podman builds OCI images, which drop the instruction; studioctl polls the health endpoint instead.
COPY --from=build --chown=$APP_UID /data/blobs /data/blobs
VOLUME /data/blobs
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5010 \
    Localtest__Storage__BlobRootPath=/data/blobs
USER $APP_UID

ENTRYPOINT ["dotnet", "Altinn.Platform.Storage.dll"]
