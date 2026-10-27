using UnityEditor;
using UnityEngine;

namespace Multitude
{
    /// <summary>Entry points: Project window context menu and the Tools menu.</summary>
    public static class OpenPrefabInMultitude
    {
        public const string ContextMenuPath = "Assets/Multitude/Open Prefab in Multitude";
        public const string ToolsMenuPath = "Tools/Multitude/Open Selected Prefabs";

        [MenuItem(ContextMenuPath, false, 32)]
        static void OpenContextSelection()
        {
            OpenPaths(GetSelectedPrefabPaths());
        }

        [MenuItem(ContextMenuPath, true)]
        static bool OpenContextSelectionValidate()
        {
            var paths = GetSelectedPrefabPaths();
            return paths.Length == 1;
        }

        [MenuItem(ToolsMenuPath, false, 200)]
        static void OpenToolsSelection()
        {
            var paths = GetSelectedPrefabPaths();
            if (paths.Length == 0)
            {
                Debug.LogWarning("Multitude: select one or more prefab assets in the Project window first.");
                return;
            }

            OpenPaths(paths);
        }

        [MenuItem(ToolsMenuPath, true)]
        static bool OpenToolsSelectionValidate()
        {
            return GetSelectedPrefabPaths().Length > 0;
        }

        /// <summary>Opens every given prefab, reporting the ones that could not be opened.</summary>
        public static int OpenPaths(string[] assetPaths)
        {
            var opened = 0;
            if (assetPaths == null)
            {
                return opened;
            }

            foreach (var assetPath in assetPaths)
            {
                if (PrefabEditorWindow.TryOpen(assetPath, out var error))
                {
                    opened++;
                    continue;
                }

                Debug.LogError($"Multitude: {error}");
            }

            return opened;
        }

        static string[] GetSelectedPrefabPaths()
        {
            var selection = Selection.assetGUIDs;
            if (selection == null || selection.Length == 0)
            {
                return new string[0];
            }

            var paths = new System.Collections.Generic.List<string>(selection.Length);
            foreach (var guid in selection)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (PrefabAssetValidator.LooksOpenable(path))
                {
                    paths.Add(path);
                }
            }

            return paths.ToArray();
        }
    }
}
