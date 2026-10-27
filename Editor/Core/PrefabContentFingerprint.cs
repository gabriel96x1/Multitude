using System.Text;
using UnityEditor;
using UnityEngine;

namespace Multitude
{
    /// <summary>
    /// Stable fingerprint of a prefab hierarchy: every transform value, the activation state, the
    /// layer and tag of each object plus the serialized state of each component.
    ///
    /// Multitude compares fingerprints instead of trusting a "something changed" flag, so the dirty
    /// state also goes back to clean when a change is undone by hand.
    /// </summary>
    public static class PrefabContentFingerprint
    {
        public static string Compute(GameObject root)
        {
            if (root == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(4096);
            AppendTransform(builder, root.transform, 0);
            return PrefabAssetSnapshot.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        static void AppendTransform(StringBuilder builder, Transform node, int depth)
        {
            builder.Append(depth).Append('|').Append(node.name).Append('|');
            builder.Append(node.localPosition.ToString("R")).Append('|');
            builder.Append(node.localRotation.ToString("R")).Append('|');
            builder.Append(node.localScale.ToString("R")).Append('|');
            builder.Append(node.gameObject.activeSelf).Append('|');
            builder.Append(node.gameObject.layer).Append('|');
            builder.Append(node.gameObject.tag).Append('\n');

            var components = node.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null)
                {
                    builder.Append("  <missing script>\n");
                    continue;
                }

                builder.Append("  ").Append(component.GetType().FullName).Append(':');
                try
                {
                    builder.Append(EditorJsonUtility.ToJson(component, true));
                }
                catch (System.Exception exception)
                {
                    builder.Append("<unreadable:").Append(exception.GetType().Name).Append('>');
                }

                builder.Append('\n');
            }

            for (var index = 0; index < node.childCount; index++)
            {
                AppendTransform(builder, node.GetChild(index), depth + 1);
            }
        }
    }
}
