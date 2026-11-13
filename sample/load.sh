#!/bin/sh
# Uploads the sample documents that aren't loaded yet.
# In single-user mode they go to the local user; with accounts, to a sample account it creates.
set -eu

api="${TALK_TO_RAG_API:-http://127.0.0.1:5000}"
email="sample@talk-to-rag.local"
password="sample-local-only"
here="$(cd "$(dirname "$0")" && pwd)"

session="$(curl -fsS "$api/api/auth/session")"
case "$session" in
  *'"singleUser":true'*) ;;
  *)
    curl -s -o /dev/null "$api/api/auth/register" -H 'content-type: application/json' \
      -d "{\"email\":\"$email\",\"displayName\":\"Sample\",\"password\":\"$password\"}"
    session="$(curl -fsS "$api/api/auth/login" -H 'content-type: application/json' \
      -d "{\"email\":\"$email\",\"password\":\"$password\"}")"
    echo "Sign in as $email / $password"
    ;;
esac
token="$(printf '%s' "$session" | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')"
loaded="$(curl -fsS "$api/api/sources" -H "authorization: Bearer $token")"

for file in "$here"/*.md; do
  name="$(basename "$file")"
  [ "$name" = "README.md" ] && continue
  case "$loaded" in
    *"\"$name\""*) echo "already loaded: $name" ;;
    *) curl -fsS -o /dev/null "$api/api/sources" -H "authorization: Bearer $token" -F "file=@$file;type=text/markdown"
       echo "uploaded: $name" ;;
  esac
done
echo "Open $api"
