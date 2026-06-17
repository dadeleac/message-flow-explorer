#!/usr/bin/env bash
#
# Publica Message Flow Explorer como ejecutables AUTÓNOMOS (self-contained) para
# varias plataformas. El frontend React se compila y se embebe en el binario.
# El entregable es una CARPETA por plataforma (empaquetada en .zip): el cliente
# la descomprime y ejecuta ./mfe — no necesita instalar el runtime de .NET.
#
# Importante: NO usamos single-file porque MSBuildWorkspace (Roslyn) arranca un
# proceso auxiliar "BuildHost" (carpetas BuildHost-netcore / BuildHost-net472)
# que debe acompañar al ejecutable. Sin él se pierde el análisis de alta
# fidelidad y se cae al modo sintáctico. La carpeta self-contained sí lo incluye.
#
# Uso:
#   ./scripts/publish.sh                # todas las plataformas
#   ./scripts/publish.sh osx-arm64      # solo una
#
# Salida: dist-release/<rid>/  (con mfe[.exe]) y message-flow-explorer-<rid>.zip
#
# Nota: para el análisis MSBuild la máquina destino necesita el .NET SDK instalado
# (lo tiene cualquier dev .NET). Sin SDK, la herramienta cae al análisis sintáctico.

set -euo pipefail

cd "$(dirname "$0")/.."   # raíz del repo

CLI_PROJ="src/MessageFlowExplorer.Cli/MessageFlowExplorer.Cli.csproj"
OUT_ROOT="dist-release"

# Plataformas por defecto; se pueden pasar como argumentos.
RIDS=("$@")
if [ ${#RIDS[@]} -eq 0 ]; then
  RIDS=(osx-arm64 osx-x64 win-x64 linux-x64)
fi

echo "==> Compilando el frontend (web/dist)…"
npm ci --prefix web
npm run build --prefix web

rm -rf "$OUT_ROOT"
mkdir -p "$OUT_ROOT"

for RID in "${RIDS[@]}"; do
  echo ""
  echo "==> Publicando self-contained para $RID…"
  OUT_DIR="$OUT_ROOT/$RID"

  dotnet publish "$CLI_PROJ" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    -p:DebugType=none \
    --output "$OUT_DIR"

  # Empaquetar en .zip por plataforma.
  ZIP_NAME="message-flow-explorer-$RID.zip"
  echo "==> Empaquetando $ZIP_NAME"
  ( cd "$OUT_DIR" && zip -qr "../$ZIP_NAME" . )
done

echo ""
echo "✅ Listo. Entregables en $OUT_ROOT/"
ls -1 "$OUT_ROOT"/*.zip
