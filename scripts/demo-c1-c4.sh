#!/usr/bin/env bash
# Runs the C1-C4 defect demonstration against a given checkout of the repo.
#
#   scripts/demo-c1-c4.sh <repo-root> <demo-source.cs>
#
# Builds a throwaway console project inside <repo-root> that references that
# checkout's DrMoustache.Core, runs it, and prints its output. Used to capture
# the "before" (HEAD, extracted to a temp directory) and "after" (working tree)
# behaviour with the same program.
set -euo pipefail

root="$1"
source_file="$2"
demo="$root/demo"

rm -rf "$demo"
mkdir -p "$demo"

cat > "$demo/Demo.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
    <AnalysisLevel>none</AnalysisLevel>
    <NoWarn>$(NoWarn);CS1591;CA1812</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Markdig" />
    <ProjectReference Include="../src/DrMoustache.Core/DrMoustache.Core.csproj" />
    <ProjectReference Include="../src/DrMoustache.Abstractions/DrMoustache.Abstractions.csproj" />
  </ItemGroup>
</Project>
EOF

cp "$source_file" "$demo/Program.cs"

dotnet run --project "$demo/Demo.csproj" -v quiet --nologo
