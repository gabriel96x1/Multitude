using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Multitude.Tests
{
    /// <summary>Creates throwaway prefab assets for the EditMode tests and deletes them afterwards.</summary>
    public static class TempPrefabFactory
    {
        public const string Folder = "Assets/MultitudeTestsTemp";

        public static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "MultitudeTestsTemp");
            }
        }

        public static void DeleteFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        /// <summary>Creates a prefab with one root and one child cube, saved at x = <paramref name="rootX"/>.</summary>
        public static string CreatePrefab(string fileName, float rootX = 0f)
        {
            EnsureFolder();

            var root = new GameObject(fileName);
            root.transform.position = new Vector3(rootX, 0f, 0f);

            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.name = "Child";
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = new Vector3(0f, 1f, 0f);

            var path = Folder + "/" + fileName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            return path;
        }

        /// <summary>Rewrites the prefab on disk with a moved root, simulating an edit made outside Multitude.</summary>
        public static void MoveRootOnDisk(string assetPath, float rootX)
        {
            var contents = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                contents.transform.position = new Vector3(rootX, 0f, 0f);
                PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        public static Transform FindChild(GameObject root, string childName)
        {
            var children = root.GetComponentsInChildren<Transform>(true);
            foreach (var child in children)
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
