#!/usr/bin/env bash
# Packs the SDK and the templates, checks what the packages hold, creates a project from the worker
# template and builds it against the packed SDK.
set -euo pipefail
export LC_ALL=C
root=$(cd "$(dirname "$0")/.." && pwd)
work=$(mktemp -d)
# A local settings file beside the example must never be packed; one that is already there is left alone.
canary="$root/examples/Worker/appsettings.Development.json"
if [ -e "$canary" ]; then canary=; else echo '{}' > "$canary"; fi
trap 'rm -rf "$work" ${canary:+"$canary"}' EXIT
dotnet pack "$root/src/Utxorpc.Sdk/Utxorpc.Sdk.csproj" --configuration Release --output "$work/feed"
dotnet pack "$root/templates/Utxorpc.Templates.csproj" --configuration Release -warnaserror --output "$work/feed"
for package in "$work"/feed/*.nupkg; do
  unzip -Z1 "$package" | grep -qx icon.png
  unzip -p "$package" README.md | grep -q 'public/logo-dark.svg'
done
diff <(unzip -Z1 "$work"/feed/Utxorpc.Templates.*.nupkg | grep -v -E '^(_rels/|package/|\[Content_Types\]\.xml$|.*\.nuspec$)' | sort) - <<'LIST'
README.md
content/utxorpc-worker/.template.config/template.json
content/utxorpc-worker/Program.cs
content/utxorpc-worker/UtxorpcWorker.csproj
content/utxorpc-worker/Worker.cs
content/utxorpc-worker/appsettings.json
icon.png
LIST
dotnet new install "$work"/feed/Utxorpc.Templates.*.nupkg --debug:custom-hive "$work/hive"
dotnet new utxorpc-worker --name Acme.Indexer --output "$work/Acme.Indexer" --url http://node.example:50051 --debug:custom-hive "$work/hive"
grep -q '"Url": "http://node.example:50051"' "$work/Acme.Indexer/appsettings.json"
grep -q '"ResumeOnFailure": true' "$work/Acme.Indexer/appsettings.json"
grep -q 'namespace Acme.Indexer;' "$work/Acme.Indexer/Worker.cs"
grep -q '<PackageReference Include="Utxorpc.Sdk" Version=' "$work/Acme.Indexer/Acme.Indexer.csproj"
# Utxorpc.Sdk comes only from the packages packed above, also once the same version is on nuget.org.
# A private package folder keeps it out of the machine's package cache.
cat > "$work/Acme.Indexer/nuget.config" <<CONFIG
<configuration>
  <packageSources>
    <clear />
    <add key="packed" value="$work/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="packed"><package pattern="Utxorpc.Sdk" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
CONFIG
NUGET_PACKAGES="$work/packages" dotnet build "$work/Acme.Indexer" -warnaserror
