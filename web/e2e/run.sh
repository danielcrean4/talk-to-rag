#!/bin/sh
# Runs the browser tests against a stack of their own, then removes it. They use the Angular app
# that stack's API serves on 5001, built into its image, so no dev server is started.
# The normal stack and its data are not touched. Needs Docker, Node and Ollama with nomic-embed-text.
# It shares port 5001 with `make eval`, so run one at a time.
set -eu

web="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$web/.." && pwd)"
compose="docker compose -p talk-to-rag-e2e -f $root/compose.yaml -f $root/compose.eval.yaml"

trap '$compose down --volumes >/dev/null 2>&1' EXIT

$compose up -d --build --wait

cd "$web"
npm install
npx playwright install chromium
npx playwright test "$@"
