# Building the package

Everything happens locally. There is no CI: `scripts/build-package.sh` runs the test suite and produces
the two installable files, and the release is uploaded to GitHub by hand.

```bash
./scripts/build-package.sh
```

| Output | Install it with |
|---|---|
| `build/Multitude-<version>.unitypackage` | drag it into the Unity **Project** window (or double click it with the project open). Lands in `Assets/Multitude/` |
| `build/com.gabs.multitude-<version>.tgz` | `Window > Package Manager > + > Add package from tarball...` |

Do not install both at once: the same types would be compiled twice.

## Options

| Flag | Effect |
|---|---|
| `--skip-tests` | export only (the archive is still verified). Warm run: ~5 s |
| `--no-tgz` | skip `npm pack`, `.unitypackage` only |
| `--clean` | wipe `.build/` and reimport from scratch (needed after switching editor patch) |

`UNITY_PATH=/path/to/Unity` overrides the editor. Otherwise the newest
`/Applications/Unity/Hub/Editor/<unity field from package.json>*/Unity.app` is used.

## What the script does

1. Reads `version` and `unity` from `package.json` and picks a matching editor. The editor version is
   part of the throwaway project path, because a `Library/` built by another patch produces import
   errors that look like test failures.
2. Creates (or reuses) `.build/project-<editor version>/`.
3. Copies the package into `Assets/Multitude/`: `Editor/`, `package.json`, `README.md`,
   `CHANGELOG.md`, `LICENSE`, `licenses/` **and** `Tests/`, plus `scripts/PackageExporter.cs` as
   `Assets/CiTools/Editor/`. Root `.meta` files travel too so the GUIDs stay stable.
4. Injects `com.unity.test-framework` into the project manifest. `-createProject` does not ship it in
   Unity 6, and without it the `UNITY_INCLUDE_TESTS` test assembly never compiles — the suite would
   report "0 tests" and look green.
5. Runs the EditMode suite and fails if fewer than 32 tests ran or Unity exits with 2 (failed) or 3
   (never started).
6. Deletes `Tests/` **and** `Tests.meta` (Unity recreates an empty `Tests/` from an orphan `.meta`),
   then exports `Assets/Multitude` with `AssetDatabase.ExportPackage`.
7. Verifies the archive: it must contain the editor asmdef and `package.json`, and must not contain
   `Tests/`, `Documentation~/` or `CiTools/`.
8. Builds the UPM tarball with `git archive` + `npm pack`, minus `.github/`, `scripts/` and `docs/`.

Logs live in `.build/` (`create.log`, `tests.log`, `export.log`, `results.xml`). Both `.build/` and
`build/` are gitignored.

## What ships, and what cannot

The `.unitypackage` contains `Editor/`, `package.json`, `README.md`, `CHANGELOG.md`, `LICENSE` and
`licenses/`.

- `Tests/` is **not** in the `.unitypackage` (it needs the test framework and `testables` wired up);
  it does travel in the `.tgz` and in git URL installs, where it can actually run.
- `Documentation~/` can never travel inside a `.unitypackage`: Unity ignores folders ending in `~`, so
  the export drops them silently. Inside the `.tgz` it is fine.
- `docs/` (the landing page) and `scripts/` (build tooling) are not part of the package.

## Uploading a release by hand

Bump `version` in `package.json`, add the matching `## [<version>]` section to `CHANGELOG.md`, build,
then:

```bash
gh release create v0.1.0 build/* --title "Multitude 0.1.0" --notes "First release"
```

Or drag both files into *Releases → Draft a new release* on GitHub. Tag names follow `v<version>`, and
the git URL install can pin them (`#v0.1.0`).

## Requirements

- macOS with a Unity Hub editor matching the `unity` field of `package.json`, activated (Unity Personal
  is enough — nothing here leaves the machine).
- `python3` (reads `package.json`, patches the manifest) and `npm` (only for the `.tgz`).
