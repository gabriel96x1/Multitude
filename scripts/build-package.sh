#!/usr/bin/env bash
#
# Builds the package locally: runs the EditMode suite and produces installable artifacts.
#
#   build/Multitude-<version>.unitypackage   drag it into the Unity Project window
#   build/com.gabs.multitude-<version>.tgz   Package Manager > + > Add package from tarball
#
# The repository root IS the UPM package, and Unity can only export what belongs to a project it has
# open, so this script keeps a throwaway project in .build/, copies the package sources into
# Assets/Multitude, runs the tests and exports the folder with scripts/PackageExporter.cs.
#
# Usage:
#   scripts/build-package.sh                 # tests + .unitypackage + .tgz
#   scripts/build-package.sh --skip-tests    # export only (still checks the archive)
#   scripts/build-package.sh --no-tgz        # .unitypackage only
#   scripts/build-package.sh --clean         # wipe .build/ first (slow: reimports everything)
#
# Env:
#   UNITY_PATH   full path to the Unity executable (default: newest editor matching package.json)

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$SCRIPT_DIR/.." && pwd)
BUILD_DIR="$ROOT/build"
BUILD_ROOT="$ROOT/.build"
MIN_TESTS=32

RUN_TESTS=1
BUILD_TGZ=1
CLEAN=0

for arg in "$@"; do
  case "$arg" in
    --skip-tests) RUN_TESTS=0 ;;
    --no-tgz)     BUILD_TGZ=0 ;;
    --clean)      CLEAN=1 ;;
    -h|--help)    sed -n '3,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown argument: $arg (try --help)" >&2; exit 2 ;;
  esac
done

step() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
info() { printf '    %s\n' "$*"; }
die()  { printf '\n\033[31merror:\033[0m %s\n' "$*" >&2; exit 1; }

cd "$ROOT"

# ---------------------------------------------------------------- version + editor
step "Reading package.json"
[ -f package.json ] || die "run this from the package repository (package.json not found)"
VERSION=$(python3 -c "import json;print(json.load(open('package.json'))['version'])")
UNITY_MIN=$(python3 -c "import json;print(json.load(open('package.json'))['unity'])")
info "version $VERSION, unity >= $UNITY_MIN"

step "Locating the Unity editor"
if [ -n "${UNITY_PATH:-}" ]; then
  UNITY="$UNITY_PATH"
else
  CANDIDATES=$(ls -1d "/Applications/Unity/Hub/Editor/$UNITY_MIN"*/Unity.app/Contents/MacOS/Unity 2>/dev/null || true)
  [ -n "$CANDIDATES" ] || die "no Unity $UNITY_MIN* editor found in /Applications/Unity/Hub/Editor; set UNITY_PATH"
  # sort (not sort -V: BSD sort has no version compare) is enough for the x.y.zfN naming
  UNITY=$(printf '%s\n' "$CANDIDATES" | sort | tail -1)
fi
[ -x "$UNITY" ] || die "not executable: $UNITY"
EDITOR_VERSION=$(basename "$(cd "$(dirname "$UNITY")/../../.." && pwd)")
info "editor $EDITOR_VERSION at $UNITY"

# The project is keyed by editor version: a Library/ built by another patch produces import errors
# that look like test failures.
PROJECT="$BUILD_ROOT/project-$EDITOR_VERSION"

if [ "$CLEAN" = "1" ] && [ -d "$BUILD_ROOT" ]; then
  step "Cleaning $BUILD_ROOT"
  rm -rf "$BUILD_ROOT"
fi
mkdir -p "$BUILD_ROOT" "$BUILD_DIR"

# ---------------------------------------------------------------- throwaway project
if [ ! -d "$PROJECT/ProjectSettings" ]; then
  step "Creating the throwaway project"
  rm -rf "$PROJECT"
  # run from .build: launching the editor with the cwd inside the repo makes it import the repo
  ( cd "$BUILD_ROOT" && "$UNITY" -batchmode -quit -nographics -createProject "$PROJECT" \
      -logFile "$BUILD_ROOT/create.log" ) || { tail -40 "$BUILD_ROOT/create.log"; die "could not create the project"; }
  info "created $PROJECT"
else
  step "Reusing the throwaway project"
  info "$PROJECT (use --clean to reimport from scratch)"
fi

# ---------------------------------------------------------------- copy the package
step "Staging the package into Assets/Multitude"
rm -rf "$PROJECT/Assets/Multitude" "$PROJECT/Assets/Multitude.meta"
mkdir -p "$PROJECT/Assets/Multitude" "$PROJECT/Assets/CiTools/Editor"
# shipped folders only: no docs/, no Documentation~ (Unity ignores ~ folders and would drop it anyway)
for item in Editor package.json README.md CHANGELOG.md LICENSE licenses; do
  if [ -e "$item" ]; then
    cp -R "$item" "$PROJECT/Assets/Multitude/"
    [ -e "$item.meta" ] && cp "$item.meta" "$PROJECT/Assets/Multitude/"
  fi
done
cp scripts/PackageExporter.cs "$PROJECT/Assets/CiTools/Editor/"

# the tests travel too, but are removed again before exporting
rm -rf "$PROJECT/Assets/Multitude/Tests" "$PROJECT/Assets/Multitude/Tests.meta"
mkdir -p "$PROJECT/Assets/Multitude/Tests"
cp -R Tests/. "$PROJECT/Assets/Multitude/Tests/"
[ -e Tests.meta ] && cp Tests.meta "$PROJECT/Assets/Multitude/"
info "$(find "$PROJECT/Assets/Multitude" -name '*.cs' | wc -l | tr -d ' ') C# files staged"

# UNITY_INCLUDE_TESTS assemblies only compile when the test framework is present, and -createProject
# does not ship it in Unity 6 (verified on 6000.6.2f1).
python3 - "$PROJECT/Packages/manifest.json" <<'PY'
import json, sys
path = sys.argv[1]
with open(path) as handle:
    data = json.load(handle)
deps = data.setdefault("dependencies", {})
if "com.unity.test-framework" not in deps:
    deps["com.unity.test-framework"] = "1.8.0"
    with open(path, "w") as handle:
        json.dump(data, handle, indent=2)
    print("    injected com.unity.test-framework 1.8.0")
else:
    print("    com.unity.test-framework already present")
PY

# ---------------------------------------------------------------- tests
RESULTS="$BUILD_ROOT/results.xml"
if [ "$RUN_TESTS" = "1" ]; then
  step "Running the EditMode suite"
  rm -f "$RESULTS"
  set +e
  ( cd "$PROJECT" && "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
      -runTests -testPlatform EditMode -testResults "$RESULTS" -logFile "$BUILD_ROOT/tests.log" )
  CODE=$?
  set -e
  # Unity: 0 = all passed, 2 = tests failed, 3 = the run never started (licence, import, crash)
  case "$CODE" in
    0) ;;
    2) grep -E "^(FAILED|[0-9]+\) )" "$BUILD_ROOT/tests.log" | head -20 || true
       die "EditMode tests failed (see $BUILD_ROOT/tests.log)" ;;
    *) tail -40 "$BUILD_ROOT/tests.log" || true
       die "Unity could not run the tests (exit $CODE): licence, import error or crash" ;;
  esac
  COUNT=$(grep -o 'testcasecount="[0-9]*"' "$RESULTS" 2>/dev/null | head -1 | grep -o '[0-9]*' || echo 0)
  [ "$COUNT" -ge "$MIN_TESTS" ] || die "expected at least $MIN_TESTS tests, Unity reported $COUNT — the test assembly did not compile"
  info "$COUNT tests passed"
else
  info "tests skipped (--skip-tests)"
fi

# ---------------------------------------------------------------- export
step "Exporting the .unitypackage"
# never ship the tests, and drop the orphan .meta too: Unity recreates an empty Tests/ from it
rm -rf "$PROJECT/Assets/Multitude/Tests" "$PROJECT/Assets/Multitude/Tests.meta"
rm -rf "$PROJECT/build"
set +e
( cd "$PROJECT" && "$UNITY" -batchmode -quit -nographics -projectPath "$PROJECT" \
    -executeMethod Multitude.Build.PackageExporter.Export -logFile "$BUILD_ROOT/export.log" )
CODE=$?
set -e
if [ "$CODE" != "0" ] || ! grep -q "\[multitude-build\] exported" "$BUILD_ROOT/export.log"; then
  grep -E "\[multitude-build\]|error CS" "$BUILD_ROOT/export.log" | head -20 || tail -40 "$BUILD_ROOT/export.log"
  die "export failed (see $BUILD_ROOT/export.log)"
fi
grep "\[multitude-build\] exported" "$BUILD_ROOT/export.log" | head -1
rm -f "$BUILD_DIR"/*.unitypackage
cp "$PROJECT"/build/*.unitypackage "$BUILD_DIR/"

# ---------------------------------------------------------------- verify the archive
step "Verifying the archive"
PACKAGE="$BUILD_DIR/Multitude-$VERSION.unitypackage"
[ -s "$PACKAGE" ] || die "missing or empty: $PACKAGE"
SIZE=$(wc -c < "$PACKAGE" | tr -d ' ')
[ "$SIZE" -gt 20000 ] || die "archive is suspiciously small ($SIZE bytes)"

# a .unitypackage is a gzipped tar of <guid>/{pathname,asset,asset.meta,...}
tar -tzf "$PACKAGE" | grep -E '/pathname$' > "$BUILD_ROOT/entries.txt" || die "not a unitypackage archive"
while IFS= read -r entry; do
  tar -xzOf "$PACKAGE" "$entry"
  echo
done < "$BUILD_ROOT/entries.txt" > "$BUILD_ROOT/paths.txt"

grep -qx 'Assets/Multitude/Editor/Multitude.Editor.asmdef' "$BUILD_ROOT/paths.txt" \
  || die "the editor asmdef is not in the archive"
grep -qx 'Assets/Multitude/package.json' "$BUILD_ROOT/paths.txt" \
  || die "package.json is not in the archive"
if grep -q '/Tests' "$BUILD_ROOT/paths.txt"; then die "the archive contains Tests"; fi
if grep -q 'Documentation~' "$BUILD_ROOT/paths.txt"; then die "the archive contains Documentation~"; fi
if grep -q 'CiTools' "$BUILD_ROOT/paths.txt"; then die "the archive contains the build tooling"; fi

ENTRIES=$(wc -l < "$BUILD_ROOT/paths.txt" | tr -d ' ')
[ "$ENTRIES" -ge 20 ] || die "the archive looks truncated ($ENTRIES entries)"
info "$ENTRIES entries, $SIZE bytes, no Tests / Documentation~ / CiTools"

# ---------------------------------------------------------------- UPM tarball
if [ "$BUILD_TGZ" = "1" ]; then
  step "Building the UPM tarball"
  command -v npm >/dev/null 2>&1 || die "npm is required for the .tgz (use --no-tgz to skip it)"
  STAGING="$BUILD_ROOT/staging"
  rm -rf "$STAGING"
  mkdir -p "$STAGING/com.gabs.multitude"
  git archive --format=tar HEAD -o "$BUILD_ROOT/head.tar"
  tar -xf "$BUILD_ROOT/head.tar" -C "$STAGING/com.gabs.multitude"
  # build tooling and the landing page never belong inside the package
  rm -rf "$STAGING/com.gabs.multitude/.github" \
         "$STAGING/com.gabs.multitude/scripts" \
         "$STAGING/com.gabs.multitude/docs" \
         "$STAGING/com.gabs.multitude/docs.meta"
  ( cd "$STAGING" && npm pack ./com.gabs.multitude --pack-destination "$BUILD_DIR" > /dev/null )

  tar -tzf "$BUILD_DIR/com.gabs.multitude-$VERSION.tgz" > "$BUILD_ROOT/tgz.txt"
  grep -qx 'package/package.json' "$BUILD_ROOT/tgz.txt" || die "the tarball has no package.json"
  grep -q '^package/Editor/Multitude.Editor.asmdef$' "$BUILD_ROOT/tgz.txt" || die "the tarball has no editor asmdef"
  info "$(wc -l < "$BUILD_ROOT/tgz.txt" | tr -d ' ') entries"
fi

# ---------------------------------------------------------------- summary
step "Done"
ls -lh "$BUILD_DIR" | grep -vE '^total' | awk '{printf "    %-45s %s\n", $9, $5}'
cat <<EOF

    Install the .unitypackage: drag it into the Unity Project window (lands in Assets/Multitude/).
    Upload both files by hand:  gh release create v$VERSION build/* --title "Multitude $VERSION"
    Logs: $BUILD_ROOT/
EOF
