# The localtest app (today: the proxy), published with Native AOT: one self-contained binary on runtime-deps, no .NET
# runtime. Build context: this folder (localtest/localtest).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
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

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data /frontend
ENV ASPNETCORE_URLS=http://+:8000 \
    Proxy__AppRoutesFile=/data/app-routes.json \
    Proxy__FrontendRoot=/frontend
VOLUME /data
EXPOSE 8000
ENTRYPOINT ["/app/Localtest"]
