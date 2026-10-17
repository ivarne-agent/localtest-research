# Vendored Altinn Authorization (PDP) built with the localtest overrides.
# Build context: the component folder (localtest/components/authorization).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
# Redirect MSBuild's Directory.Build.props/targets lookup to the localtest overrides: defines LOCALTEST for the
# vendored projects and compiles the adjacent fakes/ folder into the host. No vendored csproj is edited.
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
RUN dotnet publish vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/Altinn.Authorization.csproj \
      -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
# Resource registry stand-ins (the altinn_keyrole_access resource); mount a folder over /policies for your own.
# App policies live in the policy store shared with Access Management (/policy-store), pushed by the app runner.
COPY policies /policies
RUN mkdir -p /policy-store
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5070 \
    Localtest__Authorization__PoliciesPath=/policies \
    Localtest__Authorization__PolicyStorePath=/policy-store
EXPOSE 5070
HEALTHCHECK --interval=5s --timeout=3s --retries=20 CMD wget -q -O /dev/null http://localhost:5070/health || exit 1
ENTRYPOINT ["dotnet", "Altinn.Authorization.dll"]
