using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Multitude
{
    /// <summary>Reason a prefab asset cannot be opened by a Multitude session.</summary>
    public enum PrefabAssetProblem
    {
        None,
        EmptyPath,
        OutsideProject,
        NotAPrefab,
        Missing,
        ModelPrefab,
        Unsupported
    }

    /// <summary>Outcome of validating an asset path before a session is created.</summary>
    public readonly struct PrefabAssetValidation
    {
        public readonly PrefabAssetProblem Problem;
        public readonly string Message;

        public PrefabAssetValidation(PrefabAssetProblem problem, string message)
        {
            Problem = problem;
            Message = message;
        }

        public bool IsValid => Problem == PrefabAssetProblem.None;

        public static PrefabAssetValidation Valid => new PrefabAssetValidation(PrefabAssetProblem.None, string.Empty);
    }

    /// <summary>
    /// Snapshot of a prefab file on disk. Used both as the opening baseline and to detect
    /// edits made outside Multitude while a session is open.
    /// </summary>
    public readonly struct PrefabAssetSnapshot
    {
        public readonly string AssetPath;
        public readonly bool Exists;
        public readonly string ContentHash;
        public readonly long SizeInBytes;

        PrefabAssetSnapshot(string assetPath, bool exists, string contentHash, long sizeInBytes)
        {
            AssetPath = assetPath ?? string.Empty;
            Exists = exists;
            ContentHash = contentHash ?? string.Empty;
            SizeInBytes = sizeInBytes;
        }

        /// <summary>Absolute project root folder (the parent of the Assets folder).</summary>
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>Reads the prefab file and hashes its bytes. Never throws for a missing file.</summary>
        public static PrefabAssetSnapshot Capture(string assetPath)
        {
            var absolutePath = ToAbsolutePath(assetPath);
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
            {
                return new PrefabAssetSnapshot(assetPath, false, string.Empty, 0);
            }

            var bytes = File.ReadAllBytes(absolutePath);
            return new PrefabAssetSnapshot(assetPath, true, ComputeHash(bytes), bytes.LongLength);
        }

        /// <summary>True when both snapshots describe the same on-disk content.</summary>
        public bool Matches(PrefabAssetSnapshot other)
        {
            return Exists == other.Exists && string.Equals(ContentHash, other.ContentHash, StringComparison.Ordinal);
        }

        public static string ComputeHash(byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString();
            }
        }

        /// <summary>Maps an "Assets/..." path to an absolute path, or null when the path is not project-relative.</summary>
        public static string ToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return null;
            }

            return Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    /// <summary>Checks that a path points to a prefab asset Multitude can safely edit and save.</summary>
    public static class PrefabAssetValidator
    {
        public static PrefabAssetValidation Validate(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return new PrefabAssetValidation(PrefabAssetProblem.EmptyPath, "No prefab asset was selected.");
            }

            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return new PrefabAssetValidation(
                    PrefabAssetProblem.OutsideProject,
                    $"'{assetPath}' is not inside the project's Assets folder, so Multitude cannot edit it.");
            }

            if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return new PrefabAssetValidation(
                    PrefabAssetProblem.NotAPrefab,
                    $"'{assetPath}' is not a prefab. Multitude opens .prefab assets only.");
            }

            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;
            if (asset == null)
            {
                return new PrefabAssetValidation(
                    PrefabAssetProblem.Missing,
                    $"The prefab '{assetPath}' does not exist any more. Refresh the project window and try again.");
            }

            var assetType = PrefabUtility.GetPrefabAssetType(asset);
            switch (assetType)
            {
                case PrefabAssetType.Regular:
                case PrefabAssetType.Variant:
                    return PrefabAssetValidation.Valid;
                case PrefabAssetType.Model:
                    return new PrefabAssetValidation(
                        PrefabAssetProblem.ModelPrefab,
                        $"'{assetPath}' is a model prefab (imported from a model file). Its content is read-only, so Multitude does not open it. " +
                        "Create a prefab variant of it and open that instead.");
                case PrefabAssetType.MissingAsset:
                    return new PrefabAssetValidation(
                        PrefabAssetProblem.Missing,
                        $"The prefab '{assetPath}' is missing: the project no longer holds the asset behind it.");
                default:
                    return new PrefabAssetValidation(
                        PrefabAssetProblem.Unsupported,
                        $"'{assetPath}' is not an editable prefab asset ({assetType}).");
            }
        }

        /// <summary>Cheap check used by menu validation, where creating a session would be too expensive.</summary>
        public static bool LooksOpenable(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            return assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }
    }
}
