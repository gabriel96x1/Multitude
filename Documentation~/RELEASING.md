# Releasing Multitude

Two GitHub Actions workflows publish the package. Nothing here runs on a developer machine.

| Workflow | Trigger | Result |
|---|---|---|
| `.github/workflows/test-package.yml` | every PR into `master` | 32 EditMode tests as the `EditMode (Unity 6000.6)` check |
| `.github/workflows/release-unitypackage.yml` | a PR **merged** into `master`, or manual dispatch | GitHub Release `v<version>` with `Multitude-<version>.unitypackage` + `com.gabs.multitude-<version>.tgz` |
| `.github/workflows/mirror-unity-image.yml` | manual dispatch only | copies the editor image into GHCR, removing Docker Hub from the release path |

## One-time setup

### 1. `UNITY_LICENSE` (required)

CI needs a licensed editor to compile and export. Generate the licence file once, on a machine with an
activated Unity 6000.6.2f1:

```bash
docker run --rm -v "$PWD":/project unityci/editor:ubuntu-6000.6.2f1-base-3 \
  unity-editor -batchmode -nographics -createManualActivationFile -logFile /dev/stdout
```

Upload the resulting `Unity_v6000.x.alf` at <https://license.unity3d.com/manual>, download the `.ulf` and
store it as the repository secret `UNITY_LICENSE`:

```bash
base64 -w0 Unity_v6000.6.x.ulf | pbcopy   # paste as the secret value
```

Both workflows accept the `.ulf` XML or its base64: if the value starts with `<?xml` it is written as is,
otherwise it is decoded from base64 into `/root/.local/share/unity3d/Unity/Unity_lic.ulf`.

Personal licences expire. When a run fails with `No valid Unity Editor license found`, regenerate the
`.ulf` and update the secret; nothing else in the workflow changes.

### 2. `DOCKERHUB_USERNAME` + `DOCKERHUB_TOKEN` (optional)

GitHub-hosted runners share IPs and Docker Hub allows 100 anonymous pulls per 6 h per IP. The failure is
`toomanyrequests` while pulling the container, before any step runs, so it cannot be retried from inside
the job.

Both workflows deliberately ship **without** a `container.credentials` block: GitHub validates the workflow
template before any step runs and rejects an empty value, so a missing secret would fail the job with
`Unexpected value ''` instead of falling back to an anonymous pull. Re-add the block only when the two
secrets exist (the snippet is in a comment right above `options` in both workflows).

Alternative, no quota at all: run *Mirror Unity editor image* once, make the resulting
`ghcr.io/gabriel96x1/unity-editor:6000.6.2f1` package public, then point `image:` at it in both workflows.

### 3. Branch protection on `master`

Mark `EditMode (Unity 6000.6)` as a required status check. Without it the gate is decorative.

## Cutting a release

1. Bump `version` in `package.json` and add the matching `## [<version>]` section to `CHANGELOG.md`.
2. Open a PR into `master`, let the EditMode check pass, merge it.
3. The release workflow runs by itself. It fails on purpose if:
   - `CHANGELOG.md` has no section for that version,
   - the tag `v<version>` already exists (bump the version instead of re-releasing),
   - the pinned editor does not satisfy the `unity` field in `package.json`,
   - the export is suspiciously small or contains `Tests/`, `Documentation~/` or `CiTools/`.
4. Confirm the Release carries both files:

```bash
gh release view v0.1.0 --json assets --jq '.assets[].name'
```

To rebuild a version that already exists, run the workflow manually with `force: true`; the release assets
are replaced in place, the tag is not moved.

## Editor version

The editor is pinned in both workflows (`env.UNITY_VERSION`, `env.UNITY_IMAGE`) and must stay in the same
`6000.6.x` line as `package.json`'s `unity` field — `prepare` compares the two and fails otherwise. When
bumping the patch:

1. Check the image tag exists: `docker manifest inspect unityci/editor:ubuntu-6000.6.2f1-base-3`.
   The `-base-` variant is the editor with no scripting backend; do not switch to `-il2cpp-`, the runner
   cannot hold that image plus a project `Library/`.
2. Update `UNITY_VERSION`, `UNITY_IMAGE` and the image in both `container.image` blocks.
3. Clear the `library-*` Actions cache: a `Library/` built by another patch produces import errors that
   look like test failures.

## Why a throwaway project

The repository root *is* the package, but Unity only runs test assemblies and exports assets that belong to
the project it has open. CI therefore creates a project with `-createProject`, copies `Editor/`, `Tests/`
and the manifests into `Assets/Multitude`, adds `com.unity.test-framework` to the manifest
(`-createProject` does not ship it in 6000.6), runs the suite, and exports the folder with
`ci/PackageExporter.cs` after deleting `Tests/` — and its `.meta`, because Unity recreates an empty
`Tests/` from an orphan `.meta`.

## Costs

The repository is public, so standard runners, minutes and artifacts are free. The real cost of a release is
~20 minutes of wall-clock CI (image pull + import + tests + export) and keeping the licence secret alive.
