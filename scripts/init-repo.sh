#!/usr/bin/env bash
# Initialises the DrMoustache repository and commits the design spec.
set -euo pipefail

cd "$(dirname "$0")/.."

if [ -d .git ]; then
  echo "Already a git repository; skipping init."
else
  git init -b main
fi

git add .gitignore scripts/init-repo.sh docs/superpowers/specs/
git commit -m "$(cat <<'EOF'
Add DrMoustache design spec

Markdown-aware Mustache rendering: the engine escapes interpolated values
according to their position in the document, so data cannot break table rows
or accidentally become headings.

Data ingestion is entirely the caller's, supplied through a plugin that
declares an attributed record type. The core performs no I/O and knows
nothing about transports.

Composes with Crucible as a pipe rather than as an extension.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"

git --no-pager log --oneline -1
