# The Access Management GUI (Altinn/altinn-access-management-frontend): the SPA built by Vite and served, with its own
# API, by the .NET BFF, built from the vendored tree with the localtest patches.
# Build context: the component folder (localtest/components/access-management-ui).
#
# Mirrors vendor/Dockerfile (upstream's) stage for stage: the Node build of the SPA, the .NET publish of the BFF, the
# runtime image with the bundle under wwwroot/accessmanagement/. Every difference is marked "localtest:". When
# re-pinning, diff that file against the previous pin and carry the change over here (see vendor/UPSTREAM.md).

# localtest: the patched tree. The SDK image of the backend stage (same digest, so no extra pull) has git; vendor/ stays
# upstream's tree and the patches apply here (git apply --check first, so a patch that no longer applies fails the build).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:d8ee39817ca03a3757288e83c37ed73cc969a286c603b827c7cbe33add1c2d1c AS source
WORKDIR /src
COPY vendor/ .
COPY patches/ /patches/
RUN git apply --check -p1 /patches/*.patch && git apply -p1 /patches/*.patch

# Building accessmanagement frontend
# localtest: upstream's node:22-slim with its registry, which Podman needs for a short name outside a terminal.
FROM docker.io/library/node:22-slim@sha256:de170b39439eb0e5a3444ad7d60b015614a381e10fecae2e30ec9e735122f38d AS generate-accessmanagement-frontend

WORKDIR /build
# localtest: upstream has `COPY . .` with the repository root as the context; here the patched tree from the source stage.
COPY --from=source /src .
# localtest: corepack and Yarn download through Node, which trusts the sandbox's TLS-intercepting proxy only with
# NODE_EXTRA_CA_CERTS; the bundle is mounted there in the sandbox and absent in CI.
RUN if [ -r /run/agent/tls/ca-bundle.pem ]; then export NODE_EXTRA_CA_CERTS=/run/agent/tls/ca-bundle.pem; fi; \
    corepack enable && \
    yarn --immutable && \
    yarn build

# Building the backend
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:d8ee39817ca03a3757288e83c37ed73cc969a286c603b827c7cbe33add1c2d1c AS generate-accessmanagement-backend

WORKDIR /build
# localtest: upstream copies backend/ to /build. Here the patched backend keeps the component layout (vendor/ next to the
# overrides, which decide "vendored" by that path), and the two variables redirect MSBuild's Directory.Build.props and
# .targets lookup to the overrides: they import the vendored ones, define LOCALTEST and compile fakes/ into the host.
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
COPY --from=source /src/backend vendor/backend
ENV DirectoryBuildPropsPath=/build/localtest.build.props \
    DirectoryBuildTargetsPath=/build/localtest.build.targets
RUN dotnet publish vendor/backend/src/Altinn.AccessManagement.UI/Altinn.AccessManagement.UI/Altinn.AccessManagement.UI.csproj -c Release -o /app_output

# Building the final image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:27b6b84beeede74fd16886177d360799c8e4299ceadfbd64eef57bafead7878a AS final

WORKDIR /app
# localtest: the stack's port convention, 8080 (upstream 80, production's ingress in front).
EXPOSE 8080

# Installing package for time zone functionality
RUN apk add --no-cache tzdata

# localtest: Production instead of upstream's Development, which production's deployment overrides anyway. Development
# loads the SPA from Vite's dev server (localhost:5173) and appsettings.Development.json (at22, the Access Management mock,
# every feature flag on); Production has no appsettings file of its own, so appsettings.json's feature set applies, all
# mocks and flags off (the owner's decision, TODO.md task 68). Port 8080, the stack's convention.
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=generate-accessmanagement-backend /app_output .
COPY --from=generate-accessmanagement-frontend /build/dist/assets ./wwwroot/accessmanagement/assets
COPY --from=generate-accessmanagement-frontend /build/src/localizations ./wwwroot/accessmanagement/localizations
COPY --from=generate-accessmanagement-frontend /build/dist/.vite/manifest.json ./wwwroot/accessmanagement

RUN mkdir /tmp/logtelemetry

ENTRYPOINT ["dotnet", "Altinn.AccessManagement.UI.dll"]
