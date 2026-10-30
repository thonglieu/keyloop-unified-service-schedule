#!/bin/sh
set -eu
LC_ALL=C
export LC_ALL

: "${APP_DB_PASSWORD:?APP_DB_PASSWORD is required}"
: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}"

case "$APP_DB_PASSWORD$POSTGRES_PASSWORD" in
    *[!A-Za-z0-9]*)
        echo "Reviewer database passwords must contain only ASCII letters and digits." >&2
        exit 1
        ;;
esac

exec /usr/local/bin/docker-entrypoint.sh "$@"
