# Vendored workflow engine (Altinn/altinn-studio src/Runtime/workflow-engine-app) built with the localtest overrides.
# Build context: the component folder (localtest/components/workflow-engine). Mirrors the upstream
# vendor/src/Runtime/workflow-engine-app/Dockerfile: same pinned base images, same restore/publish of the host.
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS base
USER $APP_UID
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=9090
EXPOSE 9090

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
ARG BUILD_CONFIGURATION=Release
ARG BUILD_VERSION=localtest
WORKDIR /src
# The overrides decide "vendored" by path (vendor/ next to them), so the component layout is kept; vendor/src is the
# upstream build context src/, so relative project references resolve unchanged.
COPY localtest.build.props localtest.build.targets ./
COPY vendor/ vendor/
# Redirect MSBuild's Directory.Build.props/targets lookup to the localtest overrides: they import the nearest
# vendored props/targets and define LOCALTEST for the vendored projects. No vendored csproj is edited.
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
WORKDIR /src/vendor/src/Runtime/workflow-engine-app/src/WorkflowEngine.App
RUN dotnet restore "WorkflowEngine.App.csproj"
RUN dotnet publish "WorkflowEngine.App.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:CSharpier_Bypass=true /p:InformationalVersion=$BUILD_VERSION

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Development
# The aspnet image has no curl or wget; probe over bash's /dev/tcp as studioctl does. The versionless /health/ready
# is a redirect, so the probe asks the versioned path.
HEALTHCHECK --interval=2s --timeout=5s --retries=60 CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/9090 && printf 'GET /api/v1/health/ready HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "WorkflowEngine.App.dll"]
