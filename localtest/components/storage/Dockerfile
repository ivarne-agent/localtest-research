# Vendored Altinn Storage built with the localtest overrides.
# Build context: the component folder (localtest/components/storage).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
# Redirect MSBuild's Directory.Build.props/targets lookup to the localtest overrides: defines LOCALTEST for the
# vendored projects and compiles the adjacent fakes/ folder into the host. No vendored csproj is edited.
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
RUN dotnet publish vendor/src/Storage/Altinn.Platform.Storage.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
# Upstream runs on the -extra image for ICU and tzdata (non-English cultures such as nb-NO).
RUN apk add --no-cache icu-libs icu-data-full tzdata
WORKDIR /app
COPY --from=build /app .
# Yuniql runs the production migrations from ./Migration at startup (upstream copies the folder the same way).
COPY vendor/src/Storage/Migration ./Migration
RUN mkdir -p /data/blobs /apps
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5010 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    Localtest__Storage__BlobRootPath=/data/blobs \
    Localtest__Storage__AppsPath=/apps
VOLUME /data/blobs
EXPOSE 5010
HEALTHCHECK --interval=5s --timeout=3s --retries=30 CMD wget -q -O /dev/null http://localhost:5010/health || exit 1
ENTRYPOINT ["dotnet", "Altinn.Platform.Storage.dll"]
