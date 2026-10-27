using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Multitude
{
    /// <summary>
    /// Tree of the session's prefab only. Selection stays inside Multitude: it never writes to
    /// Unity's global Selection, so two open windows keep independent selections.
    /// </summary>
    public sealed class PrefabHierarchyView
    {
        const float kRowHeight = 17f;
        const float kIndentWidth = 13f;

        readonly HashSet<Transform> m_Collapsed = new HashSet<Transform>();

        GUIStyle m_RowLabel;

        public GameObject Selected { get; private set; }

        public event Action<GameObject> SelectionChanged;

        public event Action<GameObject> FrameRequested;

        /// <summary>Clears the selection when the session that owns it is gone.</summary>
        public void SyncRoot(PrefabEditSession session)
        {
            if (session == null || session.Root == null || Selected == null)
            {
                Selected = null;
                return;
            }

            if (!Selected.transform.IsChildOf(session.Root.transform) && Selected != session.Root)
            {
                Selected = null;
            }
        }

        public void Select(GameObject target)
        {
            if (Selected == target)
            {
                return;
            }

            Selected = target;
            SelectionChanged?.Invoke(Selected);
        }

        public void Draw(PrefabEditSession session, float width)
        {
            if (m_RowLabel == null)
            {
                m_RowLabel = new GUIStyle(EditorStyles.label)
                {
                    clipping = TextClipping.Clip,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (session == null || session.Root == null)
            {
                EditorGUILayout.HelpBox("No prefab is loaded in this window.", MessageType.Info);
                return;
            }

            DrawNode(session.Root.transform, 0, width);
        }

        void DrawNode(Transform node, int depth, float width)
        {
            if (node == null)
            {
                return;
            }

            DrawRow(node, depth, width);

            if (!m_Collapsed.Contains(node))
            {
                for (var index = 0; index < node.childCount; index++)
                {
                    DrawNode(node.GetChild(index), depth + 1, width);
                }
            }
        }

        void DrawRow(Transform node, int depth, float width)
        {
            var row = GUILayoutUtility.GetRect(width, kRowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(kRowHeight));
            var current = Event.current;
            var gameObject = node.gameObject;

            if (current != null && row.Contains(current.mousePosition))
            {
                if (current.type == EventType.MouseDown && current.button == 0)
                {
                    Select(gameObject);
                    if (current.clickCount == 2)
                    {
                        FrameRequested?.Invoke(gameObject);
                    }

                    current.Use();
                }
                else if (current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.045f));
                }
            }

            if (current != null && current.type == EventType.Repaint)
            {
                if (Selected == gameObject)
                {
                    EditorGUI.DrawRect(row, new Color(0.24f, 0.49f, 0.9f, 0.35f));
                }
                else if (Selected != null && Selected.transform == node.parent)
                {
                    EditorGUI.DrawRect(row, new Color(0.24f, 0.49f, 0.9f, 0.12f));
                }
            }

            var indent = row.x + 2f + depth * kIndentWidth;
            var hasChildren = node.childCount > 0;
            var isExpanded = !m_Collapsed.Contains(node);

            if (hasChildren)
            {
                var foldoutRect = new Rect(indent, row.y, kIndentWidth, row.height);
                var next = EditorGUI.Foldout(foldoutRect, isExpanded, GUIContent.none, true);
                if (next != isExpanded)
                {
                    if (next)
                    {
                        m_Collapsed.Remove(node);
                    }
                    else
                    {
                        m_Collapsed.Add(node);
                    }
                }
            }

            var labelRect = new Rect(indent + kIndentWidth + 2f, row.y, Mathf.Max(10f, row.width - indent - kIndentWidth), row.height);
            var style = m_RowLabel;
            var previousColor = style.normal.textColor;
            style.normal.textColor = gameObject.activeSelf ? EditorStyles.label.normal.textColor : new Color(0.6f, 0.6f, 0.6f, 1f);
            GUI.Label(labelRect, gameObject.name, style);
            style.normal.textColor = previousColor;
        }
    }
}
