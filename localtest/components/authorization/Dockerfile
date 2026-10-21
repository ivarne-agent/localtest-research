# Vendored Altinn Authorization (PDP) built with the localtest overrides.
# Build context: the component folder (localtest/components/authorization).
#
# Mirrors vendor/src/apps/Altinn.Authorization/Dockerfile (the upstream one) line for line: same pinned base images,
# the publish from the host project folder, tzdata and krb5-libs, the non-root dotnet user. Every difference is marked
# "localtest:". When re-pinning, diff that file against the previous pin and carry the change over here (see
# vendor/UPSTREAM.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:d8ee39817ca03a3757288e83c37ed73cc969a286c603b827c7cbe33add1c2d1c AS build
WORKDIR /app
WORKDIR /src
# localtest: upstream copies the repository's src/ to /src. Here the sources keep the component layout under /src
# (vendor/ next to the overrides, which decide "vendored" by that path), and only vendor/src goes in, so the vendored
# root .editorconfig and global.json stay out as upstream's build leaves them out. The two variables redirect
# MSBuild's Directory.Build.props/targets lookup to the overrides: they import the vendored ones, define LOCALTEST
# and compile fakes/ into the host. No vendored csproj is edited.
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
COPY vendor/src vendor/src
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
WORKDIR /src/vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:27b6b84beeede74fd16886177d360799c8e4299ceadfbd64eef57bafead7878a AS final
EXPOSE 5050
WORKDIR /app
COPY --from=build /app .

# Install tzdata for timezone support, which is needed for correct handling of dates and times in the application
RUN apk add --no-cache tzdata krb5-libs

# setup the user and group
# the user will have no password, using shell /bin/false and using the group dotnet
RUN addgroup -g 3000 dotnet && adduser -u 1000 -G dotnet -D -s /bin/false dotnet
# localtest: the policy store shared with Access Management and the Resource Registry (app policies pushed by the app
# runner), owned by the app user like Access Management's.
RUN mkdir -p /policy-store && chown dotnet:dotnet /policy-store
# update permissions of files if neccessary before becoming dotnet user
USER dotnet
RUN mkdir /tmp/logtelemetry

# localtest: settings production gets from its deployment. The stack reaches the service on 5070, not upstream's 5050.
# Development's Debug logging was mostly JWT cookie lines, so the default is Information (the environment still wins).
# No HEALTHCHECK: podman builds OCI images, which drop the instruction; studioctl polls the health endpoint instead.
ENV ASPNETCORE_ENVIRONMENT=Development \
    Logging__LogLevel__Default=Information \
    ASPNETCORE_URLS=http://+:5070 \
    Localtest__Authorization__PolicyStorePath=/policy-store
ENTRYPOINT ["dotnet", "Altinn.Authorization.dll"]
