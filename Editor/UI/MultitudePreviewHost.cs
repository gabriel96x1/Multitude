using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Multitude
{
    /// <summary>
    /// Creates the hidden objects Multitude renders with and keeps them out of the user's way.
    ///
    /// Unity renders any object that lives in a loaded scene, even with HideFlags.HideAndDontSave, so
    /// every preview object is created with three guards: hidden flags, a layer only the preview camera
    /// sees, and Renderer.forceRenderingOff, which takes it out of the scene view and the game view
    /// while Multitude is not drawing. Only while the preview renders its own frame are the renderers
    /// switched back on, synchronously.
    /// </summary>
    public static class MultitudePreviewHost
    {
        public const string NamePrefix = "__Multitude";
        public const string CameraName = NamePrefix + "PreviewCamera";
        public const string LightName = NamePrefix + "PreviewLight";
        public const string CopyName = NamePrefix + "PreviewCopy";
        public const string HolderName = NamePrefix + "PreviewHolder";

        static int s_Layer = -1;

        /// <summary>Layer shared by every preview object; picked from the unused user layers.</summary>
        public static int Layer
        {
            get
            {
                if (s_Layer < 0)
                {
                    s_Layer = FindFreeLayer();
                }

                return s_Layer;
            }
        }

        /// <summary>Creates an object that is hidden from the start, so the user's scene stays clean.</summary>
        public static GameObject CreateHiddenObject(string name)
        {
            var created = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            created.layer = Layer;
            SceneVisibilityManager.instance.Hide(created, true);
            return created;
        }

        /// <summary>
        /// Hides a freshly created copy and moves it to the preview layer. Every renderer starts with
        /// rendering switched off; the preview turns it on only for its own frame.
        /// </summary>
        public static void PrepareForPreview(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var layer = Layer;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                var target = child.gameObject;
                target.layer = layer;
                target.hideFlags = HideFlags.HideAndDontSave;
                SceneVisibilityManager.instance.Hide(target, true);
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.forceRenderingOff = true;
            }
        }

        /// <summary>Turns rendering of the preview objects on or off for every camera.</summary>
        public static void SetRenderersEnabled(GameObject root, bool enabled)
        {
            if (root == null)
            {
                return;
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.forceRenderingOff = !enabled;
            }
        }

        /// <summary>True when the object still exists.</summary>
        public static bool IsAlive(GameObject target)
        {
            return target != null;
        }

        /// <summary>
        /// Destroys preview objects left behind by a window that no longer exists (a script reload
        /// drops every C# reference while the objects survive in the editor's hidden world).
        /// </summary>
        public static int DestroyOrphans(HashSet<GameObject> keep = null)
        {
            var destroyed = 0;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null || (keep != null && keep.Contains(candidate)))
                {
                    continue;
                }

                var name = candidate.name ?? string.Empty;
                var isOurs = name.StartsWith(NamePrefix, StringComparison.Ordinal);
                if (!isOurs)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(candidate);
                destroyed++;
            }

            return destroyed;
        }

        [InitializeOnLoadMethod]
        static void ResetLayerCache()
        {
            s_Layer = -1;
        }

        static int FindFreeLayer()
        {
            for (var candidate = 31; candidate >= 8; candidate--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(candidate)))
                {
                    return candidate;
                }
            }

            // Every user layer has a name: fall back to the last one.
            return 31;
        }
    }
}
