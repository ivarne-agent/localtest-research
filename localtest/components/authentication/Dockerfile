# Vendored Altinn Authentication built with the localtest overrides.
# Build context: the component folder (localtest/components/authentication).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
# Redirect MSBuild's Directory.Build.props/targets lookup to the localtest overrides: defines LOCALTEST for the
# vendored projects and compiles the adjacent fakes/ folder into the host. No vendored csproj is edited.
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
RUN dotnet publish vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Altinn.Platform.Authentication.csproj \
      -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5101
EXPOSE 5101
HEALTHCHECK --interval=5s --timeout=3s --retries=20 CMD wget -q -O /dev/null http://localhost:5101/health || exit 1
ENTRYPOINT ["dotnet", "Altinn.Platform.Authentication.dll"]
