// ci/PackageExporter.cs
// Build tooling for this repository. It is copied into the throwaway CI project as
// Assets/CiTools/Editor/PackageExporter.cs and is NEVER shipped inside the exported package.
//
// Invoked by .github/workflows/release-unitypackage.yml with:
//   Unity -batchmode -quit -nographics -projectPath <project> \
//         -executeMethod Multitude.Ci.PackageExporter.Export
//
// It exports Assets/Multitude (the package as a consumer would see it) to
// <project>/build/Multitude-<version>.unitypackage and exits with a non zero code on failure,
// so the workflow can tell a broken export from a successful one.

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Multitude.Ci
{
    public static class PackageExporter
    {
        /// <summary>Folder of the package inside the throwaway project.</summary>
        const string SourceRoot = "Assets/Multitude";

        /// <summary>Output folder, relative to the project root (not to the process cwd).</summary>
        const string OutputFolder = "build";

        /// <summary>Assets that must be inside the export for it to be usable.</summary>
        static readonly string[] RequiredAssets =
        {
            "Editor/Multitude.Editor.asmdef",
            "package.json",
        };

        /// <summary>Assets that must never be inside the export.</summary>
        static readonly string[] ForbiddenAssets =
        {
            "Tests",
            "Documentation~",
        };
        public static void Export()
        {
            try
            {
                var version = ReadVersion();
                var output = Path.Combine(ProjectRoot, OutputFolder, "Multitude-" + version + ".unitypackage");
                Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ProjectRoot);

                Validate();

                AssetDatabase.ExportPackage(new[] { SourceRoot }, output, ExportPackageOptions.Recurse);

                var size = new FileInfo(output).Length;
                if (size <= 0)
                {
                    Fail("export produced an empty archive: " + output);
                    return;
                }

                Log("exported " + output + " (" + size + " bytes) from " + SourceRoot);
                Exit(0);
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
            }
        }

        /// <summary>
        /// The project root of the project Unity has open, so the output path never depends on
        /// the process working directory.
        /// </summary>
        static string ProjectRoot
        {
            get
            {
                var parent = Directory.GetParent(Application.dataPath);
                if (parent == null)
                {
                    throw new InvalidOperationException("cannot resolve the project root from " + Application.dataPath);
                }

                return parent.FullName;
            }
        }

        static void Validate()
        {
            if (!AssetDatabase.IsValidFolder(SourceRoot))
            {
                throw new DirectoryNotFoundException("missing package folder: " + SourceRoot);
            }

            foreach (var asset in RequiredAssets)
            {
                var path = Path.Combine(SourcePath, asset);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("missing file inside the package: " + path);
                }
            }

            foreach (var forbidden in ForbiddenAssets)
            {
                var path = Path.Combine(SourcePath, forbidden);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                // An empty folder left behind by an orphan .meta exports nothing, so it is only a
                // warning. A folder with content would ship what must not ship: that is an error.
                if (Directory.GetFileSystemEntries(path).Length == 0)
                {
                    Log("ignoring empty leftover folder " + path);
                    continue;
                }

                throw new InvalidOperationException("forbidden folder inside the package: " + path);
            }
        }

        /// <summary>Absolute path of the package sources, independent of the process cwd.</summary>
        static string SourcePath
        {
            get { return Path.Combine(ProjectRoot, SourceRoot.Replace('/', Path.DirectorySeparatorChar)); }
        }

        /// <summary>Reads the version from the package manifest instead of hardcoding it in CI.</summary>
        static string ReadVersion()
        {
            var manifestPath = Path.Combine(SourcePath, "package.json");
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("missing package manifest: " + manifestPath);
            }

            var manifest = JsonUtility.FromJson<PackageManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || string.IsNullOrEmpty(manifest.version))
            {
                throw new InvalidOperationException("package.json has no version field: " + manifestPath);
            }

            return manifest.version;
        }

        [Serializable]
        sealed class PackageManifest
        {
            // ReSharper disable once InconsistentNaming — the field name must match package.json
            public string version;
        }

        static void Log(string message)
        {
            Debug.Log("[multitude-ci] " + message);
            Console.WriteLine("[multitude-ci] " + message);
        }

        static void Fail(string message)
        {
            Debug.LogError("[multitude-ci] " + message);
            Console.Error.WriteLine("[multitude-ci] " + message);
            Exit(1);
        }

        static void Exit(int code)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(code);
                return;
            }

            throw new Exception("[multitude-ci] export finished with code " + code);
        }
    }
}
