#!/usr/bin/env bash
# Publica una versión de Vigía para que Puente la instale.
#   1. compila Vigía (autocontenida) y Puente
#   2. firma el .zip y Puente.exe con la clave de %USERPROFILE%\.vigia\release-key.pem (nunca va al repo)
#   3. crea la versión vX.Y.Z en JcperezDev/vigia-releases (público, solo instaladores) y sube los archivos
#   4. etiqueta el código en JcperezDev/vigia (privado)
# Uso (git-bash):  tools/release.sh "Qué trae esta versión"
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd -W)"
NOTES="${1:?Escribe las notas de la versión entre comillas}"
PUB_REPO="JcperezDev/vigia-releases"
KEY="$USERPROFILE/.vigia/release-key.pem"
DOTNET="$USERPROFILE/.dotnet/dotnet.exe"
export DOTNET_ROOT="$USERPROFILE/.dotnet" DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

V=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' app/Vigia/Vigia.csproj)
[ -f "$KEY" ] || { echo "Falta la clave de firma: $KEY"; exit 1; }
[ -z "$(git status --porcelain)" ] || { echo "Hay cambios sin commit; haz commit antes de publicar."; exit 1; }

TOKEN=$(printf 'protocol=https\nhost=github.com\n\n' | git credential fill 2>/dev/null | sed -n 's/^password=//p')
[ -n "$TOKEN" ] || { echo "git no tiene sesión de GitHub."; exit 1; }
DISCARD="$ROOT/out/.discard"  # mingw curl falla (23) al escribir en /dev/null
api() { curl -sS -H "Authorization: token $TOKEN" -H "Accept: application/vnd.github+json" "$@"; }

mkdir -p out
if [ "$(api -o "$DISCARD" -w '%{http_code}' "https://api.github.com/repos/$PUB_REPO/releases/tags/v$V")" = 200 ]; then
  echo "v$V ya está publicada. Sube <Version> en app/Vigia/Vigia.csproj y app/Vigia/app.manifest."; exit 1
fi

OUT="$ROOT/out/$V"
rm -rf "out/$V"; mkdir -p "out/$V"
echo "== Vigía $V"
"$DOTNET" publish app/Vigia/Vigia.csproj -c Release -r win-x64 --self-contained true -o "$OUT/Vigia" -v quiet -nologo
echo "== Puente"
"$DOTNET" publish app/Puente/Puente.csproj -c Release -o "$OUT/puente" -v quiet -nologo
cp "out/$V/puente/Puente.exe" "out/$V/Puente.exe"

ZIP="Vigia-$V-win-x64.zip"
( cd "out/$V/Vigia" && /c/Windows/System32/tar.exe -a -c -f "../$ZIP" * )

P="$OUT/puente/Puente.exe"
for f in "$ZIP" Puente.exe; do
  "$P" --sign "$OUT/$f" "$KEY"
  "$P" --verify "$OUT/$f" || { echo "La firma de $f no se pudo comprobar."; exit 1; }
done
ls -la "out/$V" | grep -E "zip|exe|sig"

echo "== Publicando v$V"
node -e "require('fs').writeFileSync(process.argv[1], JSON.stringify({tag_name:'v'+process.argv[2], target_commitish:'main', name:'Vigía '+process.argv[2], body:process.argv[3]}))" "$OUT/release.json" "$V" "$NOTES"
api -o "$OUT/release-out.json" -H "Content-Type: application/json; charset=utf-8" --data-binary "@$OUT/release.json" "https://api.github.com/repos/$PUB_REPO/releases"
RID=$(node -e "const r=require(process.argv[1]); if(!r.id){console.error(JSON.stringify(r)); process.exit(1)} console.log(r.id)" "$OUT/release-out.json")
for f in "$ZIP" "$ZIP.sig" Puente.exe Puente.exe.sig; do
  code=$(api -o "$DISCARD" -w '%{http_code}' -H "Content-Type: application/octet-stream" --data-binary "@$OUT/$f" \
    "https://uploads.github.com/repos/$PUB_REPO/releases/$RID/assets?name=$f")
  echo "  $f: $code"; [ "$code" = 201 ] || exit 1
done

git rev-parse -q --verify "refs/tags/v$V" >/dev/null || git tag "v$V"
git push -q origin main "v$V"
echo "Listo: https://github.com/$PUB_REPO/releases/tag/v$V"
