# Vendored Altinn Access Management built with the localtest overrides.
# Build context: the component folder (localtest/components/access-management).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
# Redirect MSBuild's Directory.Build.props/targets lookup to the localtest overrides: defines LOCALTEST for the
# vendored projects and compiles the adjacent fakes/ folder into the host. No vendored csproj is edited.
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
RUN dotnet publish vendor/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/Altinn.AccessManagement.csproj \
      -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5080 \
    Localtest__AccessManagement__PolicyStorePath=/policy-store
EXPOSE 5080
HEALTHCHECK --interval=5s --timeout=3s --retries=40 CMD wget -q -O /dev/null http://localhost:5080/health || exit 1
ENTRYPOINT ["dotnet", "Altinn.AccessManagement.dll"]
