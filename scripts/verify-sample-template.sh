#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="/raid/elvogel/repos/dr-moustache"
FEED="/tmp/drm-feed"
TRY="/tmp/drm-try2"

rm -rf "$FEED" "$TRY"
mkdir -p "$FEED"

dotnet pack "$REPO_ROOT/DrMoustache.slnx" -c Release -o "$FEED"

dotnet new install "$FEED/DrMoustache.Templates.0.1.0.nupkg"

mkdir -p "$TRY"
cd "$TRY"
dotnet new drmoustache-sample -n WeatherReport
cd WeatherReport

cat > NuGet.config <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local" value="/tmp/drm-feed" />
  </packageSources>
</configuration>
XML

dotnet run

echo "----- dist/summary.md -----"
cat dist/summary.md
echo "----------------------------"

dotnet new uninstall DrMoustache.Templates
