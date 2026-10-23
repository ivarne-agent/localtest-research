# The localtest app (the front door: proxy by service name, app registration and provisioning, fake ID-porten and
# Maskinporten, the /Home contract), published with Native AOT: one self-contained binary on runtime-deps, no .NET
# runtime. Build context: this folder (localtest/localtest); tests/ is left out (.dockerignore).
# Base images pinned by their multi-arch manifest digests (10.0-alpine on 2026-10-20).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:230c3fbae0ceedc70bca04eed2bfb5694e8b248cadbb81010f41b7a993601f55 AS build
# The native toolchain the AOT compiler links with.
RUN apk add --no-cache clang build-base zlib-dev
WORKDIR /src
COPY Localtest.csproj .
# The runtime identifier of the machine that builds (arm64 on Apple silicon, x64 elsewhere).
RUN case "$(uname -m)" in aarch64|arm64) echo linux-musl-arm64 ;; *) echo linux-musl-x64 ;; esac > /rid \
    && dotnet restore -r "$(cat /rid)"
COPY . .
RUN dotnet publish -c Release -r "$(cat /rid)" --no-restore -o /app --nologo \
    && rm -f /app/*.dbg

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine@sha256:1a5dbee0a1d2bb1d038e212e8ccdbc12783b5cfbed866fe75d126ee80178a52a AS final
WORKDIR /app
COPY --from=build /app .
# The image's non-root app user (1654) owns the data folder (the localtest-data volume: the app and dev-server routes).
# No VOLUME: the runners mount /data (a named volume, or a tmpfs in minimal mode); the declaration only gave every other
# container an anonymous volume.
RUN mkdir -p /data /frontend && chown app:app /data
# The conventional port of the stack's images; the runtime publishes the public port 8000 onto it.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Proxy__AppRoutesFile=/data/app-routes.json \
    Proxy__FrontendRoot=/frontend
EXPOSE 8080
USER app
ENTRYPOINT ["/app/Localtest"]
