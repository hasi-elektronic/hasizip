#!/usr/bin/env bash
# Neue HasiZip-Version veröffentlichen: hasizip/scripts/release.sh 1.1.0
# Setzt die Version in App, CLI und Installer, committet, taggt hasizip-vX.Y.Z und pusht.
# Der Tag startet .github/workflows/hasizip-release.yml (Setup.exe + CLI als GitHub Release).
set -euo pipefail
VERSION="${1:?Version fehlt, z. B. 1.1.0}"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Version muss X.Y.Z sein"; exit 1; }
cd "$(dirname "$0")/.."

sed -i.bak -E "s|<Version>[^<]+</Version>|<Version>$VERSION</Version>|" src/HasiZip.App/HasiZip.App.csproj src/HasiZip.Cli/HasiZip.Cli.csproj
sed -i.bak -E "s|#define MyAppVersion \"[^\"]+\"|#define MyAppVersion \"$VERSION\"|" installer/HasiZip.iss
sed -i.bak -E "s|assemblyIdentity version=\"[^\"]+\"|assemblyIdentity version=\"$VERSION.0\"|" src/HasiZip.App/app.manifest
rm -f src/HasiZip.App/*.bak src/HasiZip.Cli/*.bak installer/*.bak

dotnet test tests/HasiZip.Core.Tests --nologo
git add src/HasiZip.App/HasiZip.App.csproj src/HasiZip.Cli/HasiZip.Cli.csproj installer/HasiZip.iss src/HasiZip.App/app.manifest
git commit -m "chore(hasizip): version $VERSION"
git tag "hasizip-v$VERSION"
git push && git push origin "hasizip-v$VERSION"
echo "Release-Build läuft: GitHub → Actions → HasiZip Release"
