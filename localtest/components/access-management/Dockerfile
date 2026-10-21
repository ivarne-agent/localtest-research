# Vendored Altinn Access Management built with the localtest overrides.
# Build context: the component folder (localtest/components/access-management).
#
# Mirrors the image altinn-auth releases: src/apps/Altinn.AccessManagement/docker-bake.hcl builds
# src/apps/Altinn.AccessManagement/containers/Dockerfile.api with the named contexts aspnet-builder and aspnet-runtime,
# which are the repository's shared containers/Dockerfile.aspnet-builder and -runtime (vendored as vendor/containers/).
# Podman has no bake, so the three files follow each other here and the context names become stage names; otherwise
# their lines are upstream's, in order. (The app folder's own Dockerfile is only used by a manually run publish
# workflow.) Every difference is marked "localtest:". When re-pinning, diff the three files against the previous pin
# and carry the change over here (see vendor/UPSTREAM.md).

# --- containers/Dockerfile.aspnet-builder (bake target aspnet-builder)
# Chiseled has no SDK image; use the matching Ubuntu release for builds.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS aspnet-builder
WORKDIR /src
# localtest: upstream copies the repository's src/ to /src. Here the sources keep the component layout under /src
# (vendor/ next to the overrides, which decide "vendored" by that path), and only vendor/src goes in. The two
# variables redirect MSBuild's Directory.Build.props/targets lookup to the overrides: they import the vendored ones,
# define LOCALTEST and compile fakes/ into the host. No vendored csproj is edited.
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
COPY vendor/src vendor/src
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets

# --- containers/Dockerfile.aspnet-runtime (bake target aspnet-runtime)
# Prepare files outside the runtime: chiseled has no shell or package manager.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS setup
RUN mkdir -p /rootfs/usr/lib /rootfs/etc /rootfs/tmp/logtelemetry \
    && cp -L /usr/lib/*/libgssapi_krb5.so.2 /usr/lib/*/libkrb5.so.3 \
        /usr/lib/*/libk5crypto.so.3 /usr/lib/*/libcom_err.so.2 \
        /usr/lib/*/libkrb5support.so.0 /usr/lib/*/libkeyutils.so.1 \
        /usr/lib/*/libresolv.so.2 /rootfs/usr/lib/ \
    && printf 'root:x:0:0:root:/root:/bin/false\ndotnet:x:1000:3000::/app:/bin/false\n' > /rootfs/etc/passwd \
    && printf 'root:x:0:\ndotnet:x:3000:\n' > /rootfs/etc/group \
    && chown 1000:3000 /rootfs/tmp/logtelemetry
# localtest: the policy store shared with the PDP, owned by the app user (the runtime has no shell to create it).
# And /tmp keeps the base image's mode (1777): copying /rootfs/ over it with buildah (podman build) applies the staged
# directory's 755, so the app user cannot write temporary files and Access Management fails to start. Whether
# BuildKit, which upstream releases with, behaves the same, or production mounts its own /tmp, is not verified.
RUN mkdir -p /rootfs/policy-store && chown 1000:3000 /rootfs/policy-store \
    && chmod 1777 /rootfs/tmp

# The extra variant includes timezone data and ICU. Keep Kerberos support from
# the matching Ubuntu SDK and preserve the existing dotnet UID/GID.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:3ed6558056b54d5c9dfefb7ff473728e022ff2bf1d976cbf8ee07b05ec594374 AS aspnet-runtime
COPY --from=setup /rootfs/ /
WORKDIR /app
USER dotnet

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# --- src/apps/Altinn.AccessManagement/containers/Dockerfile.api
FROM aspnet-builder AS build
# localtest: the host project sits under vendor/ (see the builder stage).
RUN dotnet publish /src/vendor/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement -c Release -o /app

FROM aspnet-runtime AS final
COPY --from=build /app .
# localtest: settings production gets from its deployment; the stack reaches the service on 5080. No HEALTHCHECK:
# podman builds OCI images, which drop the instruction; studioctl polls the health endpoint instead.
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5080 \
    Localtest__AccessManagement__PolicyStorePath=/policy-store
ENTRYPOINT ["dotnet", "Altinn.AccessManagement.dll"]
