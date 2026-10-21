# Vendored workflow engine (Altinn/altinn-studio src/Runtime/workflow-engine-app) built with the localtest overrides.
# Build context: the component folder (localtest/components/workflow-engine).
#
# Mirrors vendor/src/Runtime/workflow-engine-app/Dockerfile (the upstream one) line for line, and .dockerignore
# repeats its Dockerfile.dockerignore's exclusion of the Directory.*.props files. Every difference is marked
# "localtest:". When re-pinning, diff both files against the previous pin and carry the change over here (see
# vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS base
USER $APP_UID
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=9090
EXPOSE 9090

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
ARG BUILD_CONFIGURATION=Release
# localtest: the version the engine reports.
ARG BUILD_VERSION=localtest
WORKDIR /build

# localtest: upstream's build context is the monorepo's src/; here it is vendor/src under /build, next to the
# overrides (which decide "vendored" by that path). The two variables redirect MSBuild's Directory.Build.props/targets
# lookup to the overrides: they define LOCALTEST for the vendored projects. No vendored csproj is edited.
COPY localtest.build.props localtest.build.targets ./
ENV DirectoryBuildPropsPath=/build/localtest.build.props \
    DirectoryBuildTargetsPath=/build/localtest.build.targets
COPY vendor/src/common/dotnet/Altinn.Studio.Common/ vendor/src/common/dotnet/Altinn.Studio.Common/
COPY vendor/src/Runtime/workflow-engine/ vendor/src/Runtime/workflow-engine/
COPY vendor/src/Runtime/workflow-engine-app/ vendor/src/Runtime/workflow-engine-app/

# Prune host build artifacts, including literal backslash dirs such as `bin\Debug`.
# localtest: under vendor/src.
RUN find vendor/src/common vendor/src/Runtime -type d \( -name 'bin*' -o -name 'obj*' \) -prune -exec rm -rf '{}' +

# localtest: the project under vendor/src.
WORKDIR /build/vendor/src/Runtime/workflow-engine-app/src/WorkflowEngine.App
RUN dotnet restore "WorkflowEngine.App.csproj"
RUN dotnet publish "WorkflowEngine.App.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:CSharpier_Bypass=true /p:InformationalVersion=$BUILD_VERSION

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# localtest: settings production gets from its deployment. No HEALTHCHECK: podman builds OCI images, which drop the
# instruction; studioctl polls the health endpoint instead.
ENV ASPNETCORE_ENVIRONMENT=Development
ENTRYPOINT ["dotnet", "WorkflowEngine.App.dll"]
