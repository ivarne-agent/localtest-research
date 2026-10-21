#!/bin/sh
# localtest: the BFF image's command (Dockerfile.bff); upstream's is `pnpm --filter bff run start`, which this ends with.
#
# 1. MASKINPORTEN_JWK, when unset: a fresh RSA key with a kid, as the base64 JSON JWK the BFF reads. The BFF signs its
#    Maskinporten client assertion with it (packages/bff/src/auth/maskinporten.ts) for the profile page's username lookup
#    in the Register; the localtest app's fake Maskinporten reads the assertion's claims and not its signature, so any key
#    does and none is committed.
# 2. The TypeORM migrations, which production runs as a separate job with this image and MIGRATION_RUN=true
#    (.azure/applications/bff-migration-job). They are idempotent and run on every start; a failure exits non-zero and
#    stops the container.
# 3. The server, upstream's command.
set -eu
cd /app
if [ -z "${MASKINPORTEN_JWK:-}" ]; then
  MASKINPORTEN_JWK=$(node -e '
const { generateKeyPairSync } = require("node:crypto");
const jwk = generateKeyPairSync("rsa", { modulusLength: 2048 }).privateKey.export({ format: "jwk" });
process.stdout.write(Buffer.from(JSON.stringify({ ...jwk, kid: "localtest-arbeidsflate" })).toString("base64"));
')
  export MASKINPORTEN_JWK
fi
MIGRATION_RUN=true pnpm --filter bff run start
exec pnpm --filter bff run start
