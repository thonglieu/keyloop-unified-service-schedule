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

psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    --set=ON_ERROR_STOP=1 --set=app_password="$APP_DB_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE scheduler_app LOGIN PASSWORD %L', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'scheduler_app')
\gexec

SELECT 'CREATE DATABASE keyloop_scheduler_demo OWNER scheduler_app'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'keyloop_scheduler_demo')
\gexec

REVOKE ALL ON DATABASE keyloop_scheduler_demo FROM PUBLIC;
GRANT CONNECT, CREATE, TEMPORARY ON DATABASE keyloop_scheduler_demo TO scheduler_app;
SQL
