using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Multitude
{
    /// <summary>
    /// Component inspector for the object selected in the hierarchy. Uses the same editor path as
    /// the built-in inspector (Editor.CreateEditor), so undo and serialized editing keep working.
    /// </summary>
    public sealed class PrefabObjectInspector : IDisposable
    {
        readonly List<Editor> m_Editors = new List<Editor>();
        readonly List<bool> m_Expanded = new List<bool>();

        GameObject m_Target;
        PrefabEditSession m_Session;
        PrefabSessionState m_LastState = PrefabSessionState.Empty;
        bool m_Disposed;

        public GameObject Target => m_Target;

        public void SetSession(PrefabEditSession session, GameObject target)
        {
            if (m_Session != session)
            {
                m_Session = session;
                m_LastState = PrefabSessionState.Empty;
            }

            SetTarget(target);
        }

        public void SetTarget(GameObject target)
        {
            if (m_Target == target)
            {
                return;
            }

            m_Target = target;
            RebuildEditors();
        }

        public void Draw()
        {
            if (m_Target == null)
            {
                EditorGUILayout.HelpBox("Select an object in the hierarchy to inspect and edit it.", MessageType.Info);
                return;
            }

            if (m_Session != null)
            {
                m_LastState = m_Session.State;
                using (new EditorGUI.DisabledScope(m_LastState == PrefabSessionState.Applying || m_LastState == PrefabSessionState.Disposed))
                {
                    DrawTarget();
                }
            }
            else
            {
                DrawTarget();
            }
        }

        void DrawTarget()
        {
            var changed = false;
            EditorGUI.BeginChangeCheck();

            var headerStyle = new GUIStyle(EditorStyles.boldLabel);
            EditorGUILayout.LabelField(m_Target.name, headerStyle);
            EditorGUILayout.Space(2f);

            if ((m_Editors.Count == 0 || m_Editors[0] == null) && Event.current != null && Event.current.type == EventType.Layout)
            {
                RebuildEditors();
            }

            if (m_Editors.Count > 0 && m_Editors[0] != null)
            {
                DrawEditorSafely(m_Editors[0], "GameObject");
            }

            for (var index = 1; index < m_Editors.Count; index++)
            {
                var editor = m_Editors[index];
                var component = editor != null ? editor.target as Component : null;
                if (editor == null || editor.target == null)
                {
                    EditorGUILayout.LabelField("(missing component)", EditorStyles.miniLabel);
                    continue;
                }

                var componentName = component != null ? component.GetType().Name : editor.target.name;
                var missing = component == null && !(editor.target is GameObject);
                var label = missing ? "(Missing) " + componentName : componentName;

                EditorGUILayout.Space(3f);
                var expanded = EditorGUILayout.Foldout(m_Expanded[index], label, true, EditorStyles.foldoutHeader);
                m_Expanded[index] = expanded;
                if (!expanded)
                {
                    continue;
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    var rule = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
                    EditorGUI.DrawRect(rule, new Color(0f, 0f, 0f, 0.25f));
                    EditorGUILayout.Space(2f);
                    DrawEditorSafely(editor, label);
                    EditorGUILayout.Space(2f);
                }
            }

            changed |= EditorGUI.EndChangeCheck();

            if (changed && m_Session != null)
            {
                m_Session.RefreshState();
                m_Session.MarkDirty();
            }
        }

        void DrawEditorSafely(Editor editor, string label)
        {
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            var previousFieldWidth = EditorGUIUtility.fieldWidth;
            var previousWideMode = EditorGUIUtility.wideMode;

            try
            {
                var viewWidth = EditorGUIUtility.currentViewWidth;
                EditorGUIUtility.labelWidth = Mathf.Clamp(viewWidth * 0.3f, 90f, 170f);
                EditorGUIUtility.fieldWidth = 0f;
                EditorGUIUtility.wideMode = true;

                editor.OnInspectorGUI();
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(
                    $"The inspector for '{label}' could not be drawn by Multitude ({exception.GetType().Name}: {exception.Message}). " +
                    "This usually means the custom editor expects the normal Prefab Stage; the object stays editable through the fields that did draw.",
                    MessageType.Warning);
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
                EditorGUIUtility.fieldWidth = previousFieldWidth;
                EditorGUIUtility.wideMode = previousWideMode;
            }
        }

        void RebuildEditors()
        {
            ClearEditors();

            if (m_Target == null)
            {
                return;
            }

            var root = UnityEditor.Editor.CreateEditor(m_Target);
            if (root != null)
            {
                m_Editors.Add(root);
                m_Expanded.Add(true);
            }

            var components = m_Target.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var editor = UnityEditor.Editor.CreateEditor(component);
                if (editor == null)
                {
                    continue;
                }

                m_Editors.Add(editor);
                m_Expanded.Add(true);
            }
        }

        void ClearEditors()
        {
            foreach (var editor in m_Editors)
            {
                if (editor != null)
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                }
            }

            m_Editors.Clear();
            m_Expanded.Clear();
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            ClearEditors();
            m_Target = null;
            m_Session = null;
        }
    }
}
