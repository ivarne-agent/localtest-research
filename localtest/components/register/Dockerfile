# Vendored Altinn Register built with the localtest overrides.
# Build context: the component folder (localtest/components/register).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
# Redirect MSBuild's Directory.Build.props/targets lookup to the localtest overrides. This is what defines
# LOCALTEST for the vendored projects and links in the fakes; no vendored csproj is edited.
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
RUN dotnet publish vendor/src/apps/Altinn.Register/src/Altinn.Register/Altinn.Register.csproj \
      -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5020 \
    Localtest__Register__TestDataPath=/testdata/testData.json
EXPOSE 5020
HEALTHCHECK --interval=5s --timeout=3s --retries=20 CMD wget -q -O /dev/null http://localhost:5020/health || exit 1
ENTRYPOINT ["dotnet", "Altinn.Register.dll"]
