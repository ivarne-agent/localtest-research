# Vendored Altinn Events built with the localtest overrides.
# Build context: the component folder (localtest/components/events).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
# Redirect MSBuild's Directory.Build.props/targets lookup to the localtest overrides: defines LOCALTEST for the
# vendored projects and compiles the adjacent fakes/ folder into the host. No vendored csproj is edited.
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
RUN dotnet publish vendor/src/Events/Altinn.Platform.Events.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
# No Migration/ folder: the repositories are in memory (fakes/Persistence), so Postgres and Yuniql are switched off.
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5080
EXPOSE 5080
HEALTHCHECK --interval=5s --timeout=3s --retries=30 CMD wget -q -O /dev/null http://localhost:5080/health || exit 1
ENTRYPOINT ["dotnet", "Altinn.Platform.Events.dll"]
