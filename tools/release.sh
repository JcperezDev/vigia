#!/usr/bin/env bash
# Publica una versión de Vigía para que Puente la instale. Nada queda publicado a medias:
#   1. compila Vigía (autocontenida) y Puente
#   2. pruebas: selftest de Vigía (en una carpeta de datos aparte), zip-test y acl-test de Puente, traducciones
#   3. arma el .zip
#   4-5. firma el .zip, Puente.exe y el manifiesto (versión, tamaño y SHA-256 de cada pieza)
#   6. comprueba en local cada firma y cada huella con el Puente recién compilado (la clave pública de dentro)
#   7. crea la versión como BORRADOR (nadie la ve ni la instala)
#   8. sube todos los archivos
#   9. comprueba que están todos, con su nombre y tamaño, y descarga el manifiesto para compararlo byte a byte
#  10. sube la etiqueta y solo entonces publica (el borrador pasa a versión; GitHub la marca como la última)
# Si algo falla, el borrador se queda como borrador: vuelve a ejecutar el script y sigue (los archivos que ya estaban
# bien no se vuelven a subir; uno distinto se borra del borrador y se sube de nuevo). Nunca toca versiones publicadas.
#
# La clave de firma: %USERPROFILE%\.vigia\release-key.dpapi (cifrada con tu cuenta de Windows, ver docs/SIGNING.md);
# si solo existe el .pem se usa con aviso. Nunca se copia, no se imprime y no va al repo ni a out/. Si otra cuenta o
# grupo puede abrirla (permisos), no se firma.
# Firma fuera de este entorno (recomendado, ver docs/SIGNING.md):
#   VIGIA_RELEASE_SIGN=fuera   hace 1-4 (compila, prueba, arma y hace el manifiesto) y para, sin tocar la clave
#   (en la cuenta de firma)    Puente.exe --sign-release out\<versión> <clave>
#   VIGIA_RELEASE_SIGNED=1     no compila ni firma: comprueba lo firmado fuera (6) y publica (7-10)
#
# Uso (git-bash):  tools/release.sh "Qué trae esta versión"
#   VIGIA_RELEASE_DRY=1     hace 1-6 y para: no toca la red ni git
#   VIGIA_RELEASE_API=http://127.0.0.1:PUERTO   servidor de pruebas (tools/release-mock.js): sin token, sin git push
#   VIGIA_RELEASE_SKIP_BUILD=1   (solo con el servidor de pruebas) usa out/<versión> ya compilado
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd -W)"
NOTES="${1:?Escribe las notas de la versión entre comillas}"
PUB_REPO="${VIGIA_RELEASE_REPO:-JcperezDev/vigia}"
# las instalaciones hasta 0.20.17 buscan actualizaciones en vigia-releases; cuando casi nadie las use, se quita
MIRROR_REPO="${VIGIA_RELEASE_MIRROR-JcperezDev/vigia-releases}"
DOTNET="$USERPROFILE/.dotnet/dotnet.exe"
export DOTNET_ROOT="$USERPROFILE/.dotnet" DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1
API="${VIGIA_RELEASE_API:-https://api.github.com}"
UPLOADS="${VIGIA_RELEASE_API:-https://uploads.github.com}"
MOCK=0
case "$API" in
  https://api.github.com) ;;
  http://127.0.0.1:*|http://localhost:*) MOCK=1 ;;
  *) echo "VIGIA_RELEASE_API solo puede ser GitHub o un servidor de pruebas en 127.0.0.1."; exit 1 ;;
esac
DRY="${VIGIA_RELEASE_DRY:-0}"
SKIP_BUILD=0; [ "$MOCK" = 1 ] && [ "${VIGIA_RELEASE_SKIP_BUILD:-0}" = 1 ] && SKIP_BUILD=1
[ "${VIGIA_RELEASE_SIGNED:-0}" = 1 ] && SKIP_BUILD=1   # lo firmado fuera no se vuelve a compilar: cambiaría los bytes
# contra GitHub de verdad los repos son fijos: una variable olvidada no puede publicar en otro sitio
if [ "$MOCK" = 0 ] && { [ "$PUB_REPO" != JcperezDev/vigia ] || [ "$MIRROR_REPO" != JcperezDev/vigia-releases ]; }; then
  echo "VIGIA_RELEASE_REPO y VIGIA_RELEASE_MIRROR solo se pueden cambiar con el servidor de pruebas."; exit 1
fi

SIGN="${VIGIA_RELEASE_SIGN:-aqui}"; SIGNED="${VIGIA_RELEASE_SIGNED:-0}"
case "$SIGN" in aqui|fuera) ;; *) echo "VIGIA_RELEASE_SIGN solo puede ser aqui o fuera."; exit 1 ;; esac
[ "$SIGNED" = 1 ] && [ "$SIGN" = fuera ] && { echo "VIGIA_RELEASE_SIGN=fuera y VIGIA_RELEASE_SIGNED=1 son pasos distintos: uno cada vez."; exit 1; }
KEY=""
if [ "$SIGN" = aqui ] && [ "$SIGNED" != 1 ]; then
  KEY="$USERPROFILE/.vigia/release-key.dpapi"
  [ -f "$KEY" ] || KEY="$USERPROFILE/.vigia/release-key.pem"
  if [ "$MOCK" = 1 ]; then KEY="${VIGIA_RELEASE_TEST_KEY:?con el servidor de pruebas hace falta una clave de prueba}"; fi
  [ -f "$KEY" ] || { echo "Falta la clave de firma (release-key.dpapi o .pem en %USERPROFILE%\\.vigia)."; exit 1; }
  case "$KEY" in *.pem) [ "$MOCK" = 1 ] || echo "Aviso: la clave de firma está sin cifrar (.pem). Ver docs/SIGNING.md para cifrarla con tu cuenta." ;; esac
  case "$(cd "$(dirname "$KEY")" && pwd -W)/" in "$ROOT"/*) echo "La clave de firma no puede estar dentro del repositorio."; exit 1 ;; esac
fi

V=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' app/Vigia/Vigia.csproj)
MV=$(grep -o 'assemblyIdentity version="[0-9.]*"' app/Vigia/app.manifest | grep -o '[0-9][0-9.]*')
[ "$MV" = "$V.0" ] || { echo "app/Vigia/app.manifest dice $MV y Vigia.csproj $V: pon la misma versión."; exit 1; }
# versión mínima segura que anuncia el manifiesto (vacío = la que trae Puente compilada). Nunca más nueva que V.
MIN_VERSION="${VIGIA_MIN_VERSION:-}"
if [ "$MOCK" = 0 ] && [ "$DRY" != 1 ]; then
  [ -z "$(git status --porcelain)" ] || { echo "Hay cambios sin commit; haz commit antes de publicar."; exit 1; }
fi

OUT="$ROOT/out/$V"
mkdir -p out
DISCARD="$ROOT/out/.discard"  # mingw curl falla (23) al escribir en /dev/null
P="$OUT/puente/Puente.exe"
if [ "$SKIP_BUILD" = 1 ] && [ "$MOCK" = 1 ]; then
  # solo con el servidor de pruebas (tools/release-test.js): artefactos de prueba en %TEMP% y Puente de prueba
  OUT="${VIGIA_RELEASE_OUT:-$OUT}"; P="${VIGIA_RELEASE_PUENTE:-$P}"
fi
ZIP="Vigia-$V-win-x64.zip"
ASSETS=("$ZIP" "$ZIP.sig" Puente.exe Puente.exe.sig Vigia-Setup.exe Vigia-Setup.exe.sig vigia-manifest.json vigia-manifest.json.sig SHA256SUMS.txt)
if [ "$SIGNED" = 1 ]; then
  [ -x "$P" ] || { echo "Falta $P: primero VIGIA_RELEASE_SIGN=fuera tools/release.sh."; exit 1; }
  ( cd "$OUT" && sha256sum "${ASSETS[@]:0:8}" > SHA256SUMS.txt ) 2>/dev/null || { echo "Faltan archivos firmados en $OUT."; exit 1; }
fi

if [ "$SKIP_BUILD" = 0 ]; then
# ---------------------------------------------------------------- 1. compilar
rm -rf "out/$V"; mkdir -p "out/$V"
echo "== 1. compilar Vigía $V y Puente"
"$DOTNET" publish app/Vigia/Vigia.csproj -c Release -r win-x64 --self-contained true -o "$OUT/Vigia" -v quiet -nologo
"$DOTNET" publish app/Puente/Puente.csproj -c Release -o "$OUT/puente" -v quiet -nologo
cp "$P" "$OUT/Puente.exe"

# ---------------------------------------------------------------- 2. pruebas
echo "== 2. pruebas"
ST="$OUT/selftest"; mkdir -p "$ST/data"
VIGIA_DATA="$ST/data" "$OUT/Vigia/Vigia.exe" --selftest "$ST/out.txt" \
  || { grep FAIL "$ST/out.txt" | head -20; echo "El selftest de Vigía falló: no se publica."; exit 1; }
echo "  selftest: $(grep -c '^OK' "$ST/out.txt") OK, 0 FAIL"
"$P" --zip-test >"$OUT/zip-test.txt" || { cat "$OUT/zip-test.txt"; echo "zip-test falló."; exit 1; }
"$P" --acl-test >"$OUT/acl-test.txt" || { cat "$OUT/acl-test.txt"; echo "acl-test falló."; exit 1; }
node tools/i18n-check.js >"$OUT/i18n.txt" || { tail -20 "$OUT/i18n.txt"; echo "Las traducciones tienen errores."; exit 1; }
"$OUT/Vigia/Vigia.exe" --install-check || { echo "La versión compilada no pasa --install-check."; exit 1; }
rm -rf "$ST"

# ---------------------------------------------------------------- 3. paquete
echo "== 3. paquete"
( cd "out/$V/Vigia" && /c/Windows/System32/tar.exe -a -c -f "../$ZIP" * )
# el mismo Puente con otro nombre: es el que se baja desde la web. Su contador en GitHub = descargas nuevas;
# Puente.exe lo usan las actualizaciones, así no se mezclan (ver tools/stats.js)
cp "$OUT/Puente.exe" "$OUT/Vigia-Setup.exe"

# ---------------------------------------------------------------- 4-5. huellas y firmas
echo "== 4-5. manifiesto y firmas"
"$P" --manifest "$OUT" "$V" "$MIN_VERSION" >/dev/null
if [ "$SIGN" = fuera ]; then
  ( cd "$OUT" && sha256sum "$ZIP" Puente.exe Vigia-Setup.exe vigia-manifest.json )
  echo "Listo para firmar fuera (la clave no se tocó). En la cuenta de firma:"
  echo "  Puente.exe --sign-release \"$(cd "$OUT" && pwd -W)\" <clave>"
  echo "y luego aquí: VIGIA_RELEASE_SIGNED=1 tools/release.sh \"...\""
  exit 0
fi
"$P" --key-check "$KEY" >/dev/null || { "$P" --key-check "$KEY"; echo "No se firma con una clave que otras cuentas pueden abrir. Ver docs/SIGNING.md."; exit 1; }
for f in "$ZIP" Puente.exe Vigia-Setup.exe vigia-manifest.json; do "$P" --sign "$OUT/$f" "$KEY"; done
( cd "$OUT" && sha256sum "${ASSETS[@]:0:8}" > SHA256SUMS.txt )
fi

# ---------------------------------------------------------------- 6. comprobar en local
echo "== 6. comprobar firmas y huellas"
for f in "$ZIP" Puente.exe Vigia-Setup.exe; do
  "$P" --verify "$OUT/$f" || { echo "La firma de $f no se pudo comprobar."; exit 1; }
done
"$P" --verify-manifest "$OUT/vigia-manifest.json" || { echo "El manifiesto no cuadra con los archivos."; exit 1; }
( cd "$OUT" && sha256sum -c --quiet SHA256SUMS.txt ) || { echo "SHA256SUMS no cuadra."; exit 1; }
for f in "${ASSETS[@]}"; do [ -s "$OUT/$f" ] || { echo "Falta $f."; exit 1; }; done
if [ "$DRY" = 1 ]; then echo "Prueba en seco: todo listo en out/$V, no se tocó la red ni git."; exit 0; fi

# ---------------------------------------------------------------- red
if [ "$MOCK" = 1 ]; then
  TOKEN="prueba"
else
  TOKEN=$(printf 'protocol=https\nhost=github.com\n\n' | git credential fill 2>/dev/null | sed -n 's/^password=//p')
  [ -n "$TOKEN" ] || { echo "git no tiene sesión de GitHub."; exit 1; }
fi
api() { curl -sS --retry 3 --retry-delay 2 -H "Authorization: token $TOKEN" -H "Accept: application/vnd.github+json" "$@"; }
js() { node -e "$1" "${@:2}"; }

# el borrador de esta versión (si un intento anterior lo dejó) o uno nuevo. Una versión ya publicada no se toca.
draft_for() {  # $1 = repo -> imprime el id del borrador
  local repo="$1" code id
  code=$(api -o "$OUT/tag.json" -w '%{http_code}' "$API/repos/$repo/releases/tags/v$V")
  if [ "$code" = 200 ]; then echo "v$V ya está publicada en $repo. Sube <Version> y vuelve a intentarlo." >&2; return 1; fi
  api -o "$OUT/list.json" "$API/repos/$repo/releases?per_page=100"
  id=$(js "const l=require(process.argv[1]); const d=Array.isArray(l)&&l.find(r=>r.draft&&r.tag_name==='v'+process.argv[2]); console.log(d?d.id:'')" "$OUT/list.json" "$V")
  if [ -n "$id" ]; then echo "  el borrador v$V ya existía en $repo: sigo con él" >&2; echo "$id"; return 0; fi
  js "require('fs').writeFileSync(process.argv[1], JSON.stringify({tag_name:'v'+process.argv[2], target_commitish:process.argv[4], name:'Vigía '+process.argv[2], body:process.argv[3], draft:true}))" \
    "$OUT/release.json" "$V" "$NOTES" "$(git rev-parse HEAD)"
  api -o "$OUT/release-out.json" -H "Content-Type: application/json; charset=utf-8" --data-binary "@$OUT/release.json" "$API/repos/$repo/releases"
  js "const r=require(process.argv[1]); if(!r.id||r.draft!==true){console.error(JSON.stringify(r).slice(0,400)); process.exit(1)} console.log(r.id)" "$OUT/release-out.json"
}

same_remote() {  # $1 = url de la API del archivo subido, $2 = archivo local: ¿son los mismos bytes?
  rm -f "$OUT/remote.bin"
  api -L -o "$OUT/remote.bin" -H "Accept: application/octet-stream" "$1" && cmp -s "$OUT/remote.bin" "$2"
  local r=$?; rm -f "$OUT/remote.bin"; return $r
}

upload_all() {  # $1 = repo, $2 = id
  local repo="$1" rid="$2" f size have aid url code
  api -o "$OUT/assets.json" "$API/repos/$repo/releases/$rid/assets?per_page=100"
  for f in "${ASSETS[@]}"; do
    size=$(stat -c %s "$OUT/$f")
    read -r have aid url < <(js "const l=require(process.argv[1]); const a=l.find(x=>x.name===process.argv[2]); console.log(a?a.size:'-', a?a.id:'-', a?a.url:'-')" "$OUT/assets.json" "$f")
    # de una vez anterior: se reaprovecha solo si son los mismos bytes (mismo tamaño no basta: se pudo recompilar)
    if [ "$have" = "$size" ] && same_remote "$url" "$OUT/$f"; then echo "  $f: ya estaba (mismos bytes)"; continue; fi
    if [ "$aid" != "-" ]; then api -o "$DISCARD" -X DELETE "$API/repos/$repo/releases/assets/$aid"; fi
    code=$(api -o "$DISCARD" -w '%{http_code}' -H "Content-Type: application/octet-stream" --data-binary "@$OUT/$f" \
      "$UPLOADS/repos/$repo/releases/$rid/assets?name=$f") || code=000
    echo "  $f: $code"; [ "$code" = 201 ] || { echo "No se pudo subir $f. El borrador sigue sin publicar; vuelve a ejecutar el script."; return 1; }
  done
}

check_all() {  # $1 = repo, $2 = id: estén todos, con su tamaño, y cada uno byte a byte
  local repo="$1" rid="$2" url sizes="" f
  api -o "$OUT/assets.json" "$API/repos/$repo/releases/$rid/assets?per_page=100"
  for f in "${ASSETS[@]}"; do sizes+="$f=$(stat -c %s "$OUT/$f");"; done
  js "const l=require(process.argv[1]); const want=process.argv[2].split(';').filter(Boolean).map(s=>s.split('='));
      const bad=want.filter(([n,s])=>!l.some(a=>a.name===n&&String(a.size)===s&&(a.state||'uploaded')==='uploaded'));
      const extra=l.filter(a=>!want.some(([n])=>n===a.name)).map(a=>a.name);
      if(bad.length||extra.length){console.error('  faltan o no cuadran: '+bad.map(x=>x[0]).join(', ')+(extra.length?' · sobran: '+extra.join(', '):'')); process.exit(1)}" \
    "$OUT/assets.json" "$sizes" || return 1
  for f in "${ASSETS[@]}"; do
    url=$(js "const l=require(process.argv[1]); console.log(l.find(a=>a.name===process.argv[2]).url)" "$OUT/assets.json" "$f")
    same_remote "$url" "$OUT/$f" || { echo "  $f subido no es el mismo que se firmó aquí"; return 1; }
  done
  echo "  $repo: ${#ASSETS[@]} archivos, cada uno byte a byte igual al firmado"
}

publish_draft() {  # $1 = repo, $2 = id
  api -o "$OUT/pub.json" -X PATCH -H "Content-Type: application/json" --data-binary '{"draft":false}' "$API/repos/$1/releases/$2"
  js "const r=require(process.argv[1]); if(r.draft!==false||r.tag_name!=='v'+process.argv[2]){console.error(JSON.stringify(r).slice(0,400)); process.exit(1)}" "$OUT/pub.json" "$V"
}

REPOS=("$PUB_REPO"); [ -z "$MIRROR_REPO" ] || REPOS+=("$MIRROR_REPO")
declare -A RID
for repo in "${REPOS[@]}"; do
  echo "== 7. borrador en $repo"
  RID[$repo]=$(draft_for "$repo")
  echo "== 8. subir a $repo"
  upload_all "$repo" "${RID[$repo]}"
  echo "== 9. comprobar $repo"
  check_all "$repo" "${RID[$repo]}" || { echo "El borrador de $repo no está completo: no se publica. Vuelve a ejecutar el script."; exit 1; }
done

echo "== 10. etiqueta y publicar"
if [ "$MOCK" = 0 ]; then
  # la etiqueta apunta al commit exacto que se compiló
  git rev-parse -q --verify "refs/tags/v$V" >/dev/null || git tag "v$V"
  [ "$(git rev-parse "v$V^{commit}")" = "$(git rev-parse HEAD)" ] || { echo "La etiqueta v$V apunta a otro commit."; exit 1; }
  git push -q origin main "v$V"
fi
for repo in "${REPOS[@]}"; do publish_draft "$repo" "${RID[$repo]}"; echo "  publicada en $repo"; done
echo "Listo: https://github.com/$PUB_REPO/releases/tag/v$V"
