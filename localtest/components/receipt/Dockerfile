# Vendored Altinn Receipt built with the localtest overrides.
# Build context: the component folder (localtest/components/receipt).
#
# Mirrors vendor/Dockerfile (the upstream one) line for line: same pinned base images, the Node stage that bundles the
# React frontend, the SDK stage that publishes the host, the chiseled runtime as $APP_UID on port 5060. Every
# difference is marked "localtest:". When re-pinning, diff vendor/Dockerfile against the previous pin and carry the
# change over here (see vendor/UPSTREAM.md).
FROM node:24.21-alpine3.24@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1 AS build-receipt-frontend

WORKDIR /build

# localtest: the frontend is under vendor/.
COPY vendor/src/frontend/package.json .
COPY vendor/src/frontend/yarn.lock .
COPY vendor/src/frontend/.yarn/ ./.yarn/
COPY vendor/src/frontend/.yarnrc.yml .

COPY vendor/src/frontend/ ./

# Install
RUN corepack enable
# localtest: a build behind a TLS-intercepting proxy (the agent sandbox) provides its CA bundle at this path; Node only
# trusts it through NODE_EXTRA_CA_CERTS, exported for this step alone. Elsewhere (CI) the file is absent and nothing
# changes.
RUN if [ -r /run/agent/tls/ca-bundle.pem ]; then export NODE_EXTRA_CA_CERTS=/run/agent/tls/ca-bundle.pem; fi; \
    yarn --immutable

# Build runtime
RUN yarn run build


FROM mcr.microsoft.com/dotnet/sdk:10.0-resolute@sha256:d8051d88928fb40f74c9aca6896a57851cdced277f4102e1c5aa6d4983e27bad AS build
# Copy receipt backend
# localtest: upstream copies src/backend/Altinn.Receipt to /Receipt. Here it keeps the component layout under /src
# (vendor/ next to the overrides, which decide "vendored" by that path), with fakes/ and the overrides beside it. The
# two variables redirect MSBuild's Directory.Build.props/targets lookup to the overrides: they define LOCALTEST and
# compile fakes/ into the host. No vendored csproj is edited.
WORKDIR /src
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
COPY vendor/src/backend/Altinn.Receipt vendor/src/backend/Altinn.Receipt
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
WORKDIR /src/vendor/src/backend/Altinn.Receipt

# Build and publish
RUN dotnet publish Altinn.Platform.Receipt.csproj -c Release -o /app_output


FROM mcr.microsoft.com/dotnet/aspnet:10.0-resolute-chiseled@sha256:3d54e6f9c938851e25de6b7ab541cf6436f70f8f008a2da01266d3bbc4361f98 AS final
EXPOSE 5060
WORKDIR /app
COPY --from=build /app_output .
COPY --from=build-receipt-frontend /build/dist/receipt.js ./wwwroot/receipt/js/react/receipt.js
COPY --from=build-receipt-frontend /build/dist/receipt.css ./wwwroot/receipt/css/receipt.css
USER $APP_UID

# localtest: settings production gets from its deployment. Development is what lets the cookie scheme read
# Authentication's OpenID metadata over http. The platform endpoints, the host name and the public origin for the
# attachment links come from studioctl's environment. No HEALTHCHECK: podman builds OCI images, which drop it, and
# studioctl polls /health on the host port.
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5060

ENTRYPOINT ["dotnet", "Altinn.Platform.Receipt.dll"]
