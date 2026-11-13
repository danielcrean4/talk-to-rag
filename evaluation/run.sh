#!/bin/sh
# Scores retrieval on the sample documents in a stack of its own, then removes it.
# The normal stack and its data are not touched. Fails when a score is under evaluation/minimum_scores.json. Needs Docker and Ollama with nomic-embed-text.
set -eu

root="$(cd "$(dirname "$0")/.." && pwd)"
api="http://127.0.0.1:5001"
compose="docker compose -p talk-to-rag-eval -f $root/compose.yaml -f $root/compose.eval.yaml"

trap '$compose down --volumes >/dev/null 2>&1' EXIT

$compose up -d --build --wait

TALK_TO_RAG_API="$api" "$root/sample/load.sh" >/dev/null
token="$(curl -fsS "$api/api/auth/session" | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')"
tries=0
until ! curl -fsS "$api/api/sources" -H "authorization: Bearer $token" | grep -Eq '"status":"(Pending|Processing)"'; do
  tries=$((tries + 1))
  [ "$tries" -gt 120 ] && { echo "sample documents were not ingested in time" >&2; exit 1; }
  sleep 2
done

cd "$root/services/ai"
for mode in hybrid semantic keyword; do
  uv run python -m talk_to_rag.evaluation --api-url "$api" --mode "$mode" \
    --minimums "$root/evaluation/minimum_scores.json" "$@"
done
