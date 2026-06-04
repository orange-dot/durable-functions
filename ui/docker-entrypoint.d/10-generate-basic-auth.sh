#!/bin/sh
set -eu

if [ -z "${UI_BASIC_AUTH_USERNAME:-}" ]; then
    echo "UI_BASIC_AUTH_USERNAME must be set" >&2
    exit 1
fi

if [ -z "${UI_BASIC_AUTH_PASSWORD:-}" ]; then
    echo "UI_BASIC_AUTH_PASSWORD must be set" >&2
    exit 1
fi

if [ -z "${ORCHESTRATION_FUNCTION_KEY:-}" ]; then
    echo "ORCHESTRATION_FUNCTION_KEY must be set" >&2
    exit 1
fi

if echo "$UI_BASIC_AUTH_USERNAME" | grep -q ':'; then
    echo "UI_BASIC_AUTH_USERNAME cannot contain ':'" >&2
    exit 1
fi

HASHED_PASSWORD=$(openssl passwd -apr1 "$UI_BASIC_AUTH_PASSWORD")
printf '%s:%s\n' "$UI_BASIC_AUTH_USERNAME" "$HASHED_PASSWORD" > /etc/nginx/.htpasswd
chmod 600 /etc/nginx/.htpasswd
