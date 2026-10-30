#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
umask 077

file_setting() {
    [[ -f .env ]] || return 0
    # Read .env as data; never source or execute it.
    awk -v key="$1" '
        $0 ~ "^[[:space:]]*(export[[:space:]]+)?" key "[[:space:]]*=" {
            value=$0; sub(/^[^=]*=/, "", value)
            sub(/[[:space:]]+#.*$/, "", value)
            gsub(/^[[:space:]\r]+|[[:space:]\r]+$/, "", value)
            gsub(/^[\047\042]|[\047\042]$/, "", value)
        }
        END { if (value != "") print value }
    ' .env
}

effective_setting() {
    local setting
    if setting=$(printenv "$1"); then printf '%s' "$setting"; else file_setting "$1"; fi
}

new_password() {
    od -An -N32 -tx1 /dev/urandom | tr -d ' \n'
}

docker compose version
container_os=$(docker info --format '{{.OSType}}') || { echo 'Start Docker with Linux containers, then rerun this command.' >&2; exit 1; }
[[ "$container_os" == linux ]] || { echo 'Switch Docker to Linux containers, then rerun this command.' >&2; exit 1; }
external_identity=false
for name in REVIEWER_JWT_AUTHORITY REVIEWER_JWT_ISSUER REVIEWER_JWT_AUDIENCE; do
    if [[ -n "$(effective_setting "$name")" ]]; then external_identity=true; fi
done
default_auth=true
if [[ "$external_identity" == true ]]; then default_auth=false; fi

if [[ ! -e .env ]]; then
    # noclobber prevents replacing a configuration created by another process.
    (
        set -o noclobber
        printf '%s\n' \
            '# Generated local reviewer configuration. Keep this file for later runs.' \
            "REVIEWER_DB_ADMIN_PASSWORD=$(new_password)" \
            "REVIEWER_DB_PASSWORD=$(new_password)" \
            "REVIEWER_AUTH_ENABLED=$default_auth" \
            'REVIEWER_JWT_AUTHORITY=' \
            'REVIEWER_JWT_ISSUER=' \
            'REVIEWER_JWT_AUDIENCE=' \
            'REVIEWER_API_PORT=5080' > .env
    )
    echo 'Created .env with generated database credentials.'
elif ! grep -Eq '^[[:space:]]*(export[[:space:]]+)?REVIEWER_AUTH_ENABLED[[:space:]]*=' .env &&
     ! printenv REVIEWER_AUTH_ENABLED >/dev/null && [[ "$external_identity" == false ]]; then
    printf '\nREVIEWER_AUTH_ENABLED=true\n' >> .env
    echo 'Enabled local reviewer authentication; existing credentials were preserved.'
fi

for name in REVIEWER_DB_ADMIN_PASSWORD REVIEWER_DB_PASSWORD; do
    value=$(effective_setting "$name")
    if [[ ! "$value" =~ ^[a-zA-Z0-9]+$ ]]; then
        echo "$name must contain ASCII letters and digits. Existing .env was preserved; replace its placeholder manually." >&2
        exit 1
    fi
done
docker compose config --quiet
docker compose up --build --wait --wait-timeout 180 || {
    echo 'Startup failed. Inspect docker compose ps -a and docker compose logs db migrate seed api. Data was preserved.' >&2
    exit 1
}
port=$(effective_setting REVIEWER_API_PORT)
echo "Swagger: http://localhost:${port:-5080}/swagger"
if [[ "$(effective_setting REVIEWER_AUTH_ENABLED)" == true ]]; then
    echo 'In Swagger, execute POST /api/demo/auth to obtain local Admin and Customer tokens.'
    echo 'Paste an access token into Authorize, then test the business endpoints.'
else
    echo 'External authentication mode: use an access token from your configured identity provider.'
fi
echo 'Stop with docker compose down. Keep .env and the database volume for later runs.'
