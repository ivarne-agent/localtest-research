# Vendored Altinn Register built with the localtest overrides.
# Build context: the component folder (localtest/components/register).
#
# Mirrors vendor/Dockerfile (the upstream one) line for line: same pinned base images, build and publish, tzdata, the
# non-root dotnet user. Every difference is marked "localtest:". When re-pinning, diff vendor/Dockerfile against the
# previous pin and carry the change over here (see vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:d8ee39817ca03a3757288e83c37ed73cc969a286c603b827c7cbe33add1c2d1c AS build
WORKDIR /app

# Copy everything and build
# localtest: the context is the component folder, so upstream's build context is vendor/ here, next to the overrides
# and fakes/. The two variables redirect MSBuild's Directory.Build.props/targets lookup to the overrides: they import
# the vendored ones, define LOCALTEST and reference the fakes project. No vendored csproj is edited.
COPY . .
ENV DirectoryBuildPropsPath=/app/localtest.build.props \
    DirectoryBuildTargetsPath=/app/localtest.build.targets
RUN cd ./vendor/src/apps/Altinn.Register/src/Altinn.Register \
    && dotnet build Altinn.Register.csproj -c Release -o /app_output \
    && dotnet publish Altinn.Register.csproj -c Release -o /app_output

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:c4b29bf368004ad9076c1ab9bc91fb373561e3905b4345637e14e8b8c57e3be8 AS final
EXPOSE 5020
WORKDIR /app
COPY --from=build /app_output .

# Install tzdata for timezone support, which is needed for correct handling of dates and times in the application
RUN apk add --no-cache tzdata

# setup the user and group
# the user will have no password, using shell /bin/false and using the group dotnet
RUN addgroup -g 3000 dotnet && adduser -u 1000 -G dotnet -D -s /bin/false dotnet
# update permissions of files if neccessary before becoming dotnet user
USER dotnet
RUN mkdir /tmp/logtelemetry

# localtest: settings production gets from its deployment. No HEALTHCHECK: podman builds OCI images, which drop the
# instruction (only a compose healthcheck: takes effect).
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5020 \
    Localtest__Register__TestDataPath=/testdata/testData.json

ENTRYPOINT ["dotnet", "Altinn.Register.dll"]
