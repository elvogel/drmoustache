#!/usr/bin/env bash
# One-shot: creates the public GitHub repository and pushes main.
# Safe to delete once the remote exists.
set -euo pipefail

cd "$(dirname "$0")/.."

DESC="Markdown-aware Mustache rendering for .NET. Values are escaped by their position in the document, so data cannot reshape the page."

if git remote get-url origin >/dev/null 2>&1; then
  echo "origin already configured: $(git remote get-url origin)"
else
  gh repo create elvogel/drmoustache \
    --public \
    --source=. \
    --remote=origin \
    --description "$DESC"
fi

git push -u origin main

echo "---"
gh repo view elvogel/drmoustache --json url,visibility,defaultBranchRef \
  --template '{{.url}}  ({{.visibility}}, default: {{.defaultBranchRef.name}}){{"\n"}}'
git --no-pager log --oneline -1
