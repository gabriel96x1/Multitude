using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Multitude
{
    /// <summary>
    /// One window per open prefab. The window owns its session, its preview view, its hierarchy and
    /// its inspector; closing it releases every one of those resources.
    /// </summary>
    public sealed class PrefabEditorWindow : EditorWindow
    {
        const float kSplitterSize = 5f;
        const float kMinHierarchyWidth = 130f;
        const float kMinPreviewHeight = 110f;
        const float kMinInspectorHeight = 90f;
        const float kDefaultHierarchyWidth = 205f;
        const float kDefaultInspectorHeight = 190f;
        const float kWindowWidth = 780f;
        const float kWindowHeight = 580f;

        static readonly Dictionary<string, PrefabEditorWindow> s_OpenWindows = new Dictionary<string, PrefabEditorWindow>();
        static readonly Dictionary<string, PrefabEditSession> s_PendingSessions = new Dictionary<string, PrefabEditSession>();
        static readonly Dictionary<string, Rect> s_PendingPositions = new Dictionary<string, Rect>();
        static bool s_SuppressClosePrompt;

        [SerializeField] string m_AssetPath;
        [SerializeField] float m_HierarchyWidth = kDefaultHierarchyWidth;
        [SerializeField] float m_InspectorHeight = kDefaultInspectorHeight;

        PrefabEditSession m_Session;
        PrefabPreviewView m_Preview;
        PrefabHierarchyView m_Hierarchy;
        PrefabObjectInspector m_Inspector;
        Vector2 m_HierarchyScroll;
        Vector2 m_InspectorScroll;
        string m_StatusMessage;
        MessageType m_StatusType;
        double m_LastDirtyCheck;
        double m_LastPlaybackTick;
        bool m_ResizingHierarchy;
        bool m_ResizingInspector;

        [InitializeOnLoadMethod]
        static void InstallHooks()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.wantsToQuit -= OnWantsToQuit;
            EditorApplication.wantsToQuit += OnWantsToQuit;
            MultitudePreviewHost.DestroyOrphans();
        }

        static void OnBeforeAssemblyReload()
        {
            s_SuppressClosePrompt = true;
        }

        static void OnAfterAssemblyReload()
        {
            s_SuppressClosePrompt = false;
            s_PendingSessions.Clear();
            s_PendingPositions.Clear();
        }

        static bool OnWantsToQuit()
        {
            s_SuppressClosePrompt = true;
            var keys = new List<string>(s_OpenWindows.Keys);
            foreach (var key in keys)
            {
                if (!s_OpenWindows.TryGetValue(key, out var window) || window == null || window.m_Session == null)
                {
                    continue;
                }

                window.m_Session.RefreshState();
                if (!window.m_Session.IsDirty)
                {
                    continue;
                }

                var choice = EditorUtility.DisplayDialogComplex(
                    "Multitude: unsaved prefab changes",
                    $"'{window.m_Session.DisplayName}' has changes that were neither applied nor discarded.",
                    "Apply and quit",
                    "Cancel",
                    "Discard and quit");

                if (choice == 1)
                {
                    s_SuppressClosePrompt = false;
                    return false;
                }

                if (choice == 0)
                {
                    window.m_Session.Apply(false, out var error);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogError($"Multitude: {error}");
                    }
                }
            }

            return true;
        }

        /// <summary>Opens (or focuses) the window for one prefab asset.</summary>
        public static bool TryOpen(string assetPath, out string error)
        {
            error = string.Empty;

            var validation = PrefabAssetValidator.Validate(assetPath);
            if (!validation.IsValid)
            {
                error = validation.Message;
                return false;
            }

            if (s_OpenWindows.TryGetValue(assetPath, out var existing) && existing != null)
            {
                existing.Focus();
                return true;
            }

            if (!PrefabEditSession.TryOpen(assetPath, out var session, out error))
            {
                return false;
            }

            CreateWindow(assetPath, session);
            return true;
        }

        /// <summary>Number of open Multitude windows; used by the commands to report what happened.</summary>
        public static int OpenWindowCount => s_OpenWindows.Count;

        static PrefabEditorWindow CreateWindow(string assetPath, PrefabEditSession session)
        {
            var window = CreateInstance<PrefabEditorWindow>();
            window.m_AssetPath = assetPath;
            window.titleContent = new GUIContent(GetWindowTitle(assetPath, false));
            window.minSize = new Vector2(480f, 360f);
            var offset = s_OpenWindows.Count * 26f;
            window.position = new Rect(140f + offset, 160f + offset, kWindowWidth, kWindowHeight);
            window.AdoptSession(session);
            window.Show();
            s_OpenWindows[assetPath] = window;
            return window;
        }

        static PrefabEditorWindow CreateWindowForPendingSession(string assetPath)
        {
            if (!s_PendingSessions.TryGetValue(assetPath, out var session) || session == null)
            {
                return null;
            }

            s_PendingSessions.Remove(assetPath);

            var window = CreateInstance<PrefabEditorWindow>();
            window.m_AssetPath = assetPath;
            window.titleContent = new GUIContent(GetWindowTitle(assetPath, session.IsDirty));
            window.minSize = new Vector2(480f, 360f);
            if (s_PendingPositions.TryGetValue(assetPath, out var position))
            {
                s_PendingPositions.Remove(assetPath);
                window.position = position;
            }
            else
            {
                window.position = new Rect(160f, 180f, kWindowWidth, kWindowHeight);
            }

            window.AdoptSession(session);
            window.Show();
            s_OpenWindows[assetPath] = window;
            return window;
        }

        static string GetWindowTitle(string assetPath, bool dirty)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath ?? string.Empty);
            if (string.IsNullOrEmpty(name))
            {
                name = "Multitude";
            }

            return dirty ? name + " *" : name;
        }

        void AdoptSession(PrefabEditSession session)
        {
            m_Session = session;
            m_AssetPath = session.AssetPath;
            m_StatusMessage = null;
            BuildViews();
            m_Session.StateChanged += OnSessionStateChanged;
            m_Session.ContentChanged += OnSessionContentChanged;
            UpdateTitle();
        }

        void BuildViews()
        {
            ReleaseViews();

            m_Hierarchy = new PrefabHierarchyView();
            m_Hierarchy.SelectionChanged += OnHierarchySelectionChanged;
            m_Hierarchy.FrameRequested += OnHierarchyFrameRequested;

            m_Inspector = new PrefabObjectInspector();

            if (m_Session != null && !m_Session.IsDisposed)
            {
                var repaint = new Action(Repaint);
                m_Preview = new PrefabPreviewView(m_Session, repaint);
            }

            RestoreSelection();
        }

        void ReleaseViews()
        {
            if (m_Hierarchy != null)
            {
                m_Hierarchy.SelectionChanged -= OnHierarchySelectionChanged;
                m_Hierarchy.FrameRequested -= OnHierarchyFrameRequested;
                m_Hierarchy = null;
            }

            m_Preview?.Dispose();
            m_Preview = null;

            m_Inspector?.Dispose();
            m_Inspector = null;
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            minSize = new Vector2(480f, 360f);
            EditorApplication.update -= OnEditorTick;
            EditorApplication.update += OnEditorTick;
            m_LastPlaybackTick = 0d;

            if (m_Session == null && !string.IsNullOrEmpty(m_AssetPath))
            {
                RestoreSession();
            }
        }

        void RestoreSession()
        {
            if (s_PendingSessions.TryGetValue(m_AssetPath, out var pending) && pending != null)
            {
                s_PendingSessions.Remove(m_AssetPath);
                AdoptSession(pending);
                return;
            }

            if (s_OpenWindows.TryGetValue(m_AssetPath, out var other) && other != null && other != this)
            {
                s_OpenWindows.Remove(m_AssetPath);
            }

            if (PrefabEditSession.TryOpen(m_AssetPath, out var session, out var error))
            {
                AdoptSession(session);
            }
            else
            {
                SetStatus(error, MessageType.Error);
            }
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorTick;
        }

        /// <summary>Drives the particle preview at a steady rate while the effects are playing.</summary>
        void OnEditorTick()
        {
            if (m_Preview == null || s_SuppressClosePrompt || m_Session == null)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (m_LastPlaybackTick <= 0d)
            {
                m_LastPlaybackTick = now;
                return;
            }

            if (now - m_LastPlaybackTick < 1d / 30d)
            {
                return;
            }

            var delta = now - m_LastPlaybackTick;
            m_LastPlaybackTick = now;

            if (delta > 0.5d)
            {
                delta = 0.5d;
            }

            m_Preview.Tick(delta);
        }

        void OnInspectorUpdate()
        {
            if (m_Session == null || s_SuppressClosePrompt)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now - m_LastDirtyCheck < 0.5d)
            {
                return;
            }

            m_LastDirtyCheck = now;
            if (m_Session.RefreshState())
            {
                Repaint();
            }
        }

        void OnDestroy()
        {
            if (!string.IsNullOrEmpty(m_AssetPath)
                && s_OpenWindows.TryGetValue(m_AssetPath, out var registered)
                && registered == this)
            {
                s_OpenWindows.Remove(m_AssetPath);
            }

            if (m_Session == null)
            {
                ReleaseViews();
                return;
            }

            var closeSilently = s_SuppressClosePrompt
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || BuildPipeline.isBuildingPlayer;

            if (closeSilently)
            {
                CloseSession();
                return;
            }

            m_Session.RefreshState();
            if (!m_Session.IsDirty)
            {
                CloseSession();
                return;
            }

            var choice = EditorUtility.DisplayDialogComplex(
                "Multitude: unsaved prefab changes",
                $"'{m_Session.DisplayName}' has changes that were neither applied nor discarded.",
                "Apply and close",
                "Cancel",
                "Discard and close");

            switch (choice)
            {
                case 0:
                {
                    var outcome = m_Session.Apply(false, out var error);
                    if (outcome == PrefabApplyOutcome.Applied || outcome == PrefabApplyOutcome.NothingToDo)
                    {
                        CloseSession();
                        return;
                    }

                    Debug.LogError($"Multitude: could not apply '{m_Session.AssetPath}': {error} The window was reopened so the changes are not lost.");
                    ReopenLater(m_Session);
                    return;
                }

                case 1:
                    ReopenLater(m_Session);
                    return;

                default:
                    CloseSession();
                    return;
            }
        }

        void ReopenLater(PrefabEditSession session)
        {
            if (string.IsNullOrEmpty(m_AssetPath))
            {
                CloseSession();
                return;
            }

            s_PendingSessions[m_AssetPath] = session;
            s_PendingPositions[m_AssetPath] = position;
            m_Session = null;
            ReleaseViews();

            var reopenedPath = m_AssetPath;
            EditorApplication.delayCall += () =>
            {
                if (s_OpenWindows.ContainsKey(reopenedPath))
                {
                    return;
                }

                CreateWindowForPendingSession(reopenedPath);
            };
        }

        void CloseSession()
        {
            ReleaseViews();

            if (m_Session != null)
            {
                m_Session.StateChanged -= OnSessionStateChanged;
                m_Session.ContentChanged -= OnSessionContentChanged;
                m_Session.Dispose();
                m_Session = null;
            }
        }

        /// <summary>Reconnects the inspector to whatever the hierarchy already had selected.</summary>
        void RestoreSelection()
        {
            if (m_Inspector == null || m_Session == null)
            {
                return;
            }

            var selected = m_Hierarchy != null ? m_Hierarchy.Selected : null;
            m_Inspector.SetSession(m_Session, selected);

            if (m_Preview != null)
            {
                m_Preview.Highlighted = selected;
            }
        }

        void OnHierarchySelectionChanged(GameObject target)
        {
            if (m_Inspector != null && m_Session != null)
            {
                m_Inspector.SetSession(m_Session, target);
            }

            if (m_Preview != null)
            {
                m_Preview.Highlighted = target;
            }

            m_InspectorScroll = Vector2.zero;
            Repaint();
        }

        void OnHierarchyFrameRequested(GameObject target)
        {
            m_Preview?.FrameObject(target);
        }

        void OnSessionStateChanged(PrefabEditSession session)
        {
            m_Preview?.InvalidateContent();
            UpdateTitle();
            Repaint();
        }

        void OnSessionContentChanged(PrefabEditSession session)
        {
            m_Hierarchy?.SyncRoot(session);
            m_Preview?.InvalidateContent();
            Repaint();
        }

        void UpdateTitle()
        {
            var dirty = m_Session != null && m_Session.IsDirty;
            titleContent = new GUIContent(GetWindowTitle(m_AssetPath, dirty));
        }

        void SetStatus(string message, MessageType type)
        {
            m_StatusMessage = message;
            m_StatusType = type;
        }

        void OnGUI()
        {
            if (m_Session == null)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(
                    m_StatusMessage ?? "This Multitude window has no prefab. Open one from the Project window (right click a prefab > Multitude).",
                    string.IsNullOrEmpty(m_StatusMessage) ? MessageType.Info : m_StatusType);

                if (!string.IsNullOrEmpty(m_AssetPath) && GUILayout.Button("Try to reload '" + System.IO.Path.GetFileNameWithoutExtension(m_AssetPath) + "'"))
                {
                    RestoreSession();
                }

                return;
            }

            DrawToolbar();

            if (!string.IsNullOrEmpty(m_StatusMessage))
            {
                EditorGUILayout.HelpBox(m_StatusMessage, m_StatusType);
            }

            var lastRect = GUILayoutUtility.GetLastRect();
            var top = Mathf.Max(22f, lastRect.yMax);
            DrawBody(top);
        }

        /// <summary>
        /// Body layout with two draggable separators: one for the hierarchy width, one for the
        /// inspector height. Both sizes are serialized, so every window keeps its own proportions.
        /// </summary>
        void DrawBody(float top)
        {
            var bodyHeight = Mathf.Max(80f, position.height - top);

            var hierarchyWidth = Mathf.Clamp(
                m_HierarchyWidth,
                kMinHierarchyWidth,
                Mathf.Max(kMinHierarchyWidth, position.width * 0.6f));

            var verticalSplitter = new Rect(hierarchyWidth, top, kSplitterSize, bodyHeight);
            var rightX = verticalSplitter.xMax;
            var rightWidth = Mathf.Max(80f, position.width - rightX);

            var inspectorHeight = Mathf.Clamp(
                m_InspectorHeight,
                kMinInspectorHeight,
                Mathf.Max(kMinInspectorHeight, bodyHeight - kMinPreviewHeight - kSplitterSize));

            var previewHeight = Mathf.Max(kMinPreviewHeight, bodyHeight - inspectorHeight - kSplitterSize);
            var previewRect = new Rect(rightX, top, rightWidth, previewHeight);
            var horizontalSplitter = new Rect(rightX, previewRect.yMax, rightWidth, kSplitterSize);
            var inspectorRect = new Rect(
                rightX,
                horizontalSplitter.yMax,
                rightWidth,
                Mathf.Max(0f, position.height - horizontalSplitter.yMax));

            GUILayout.BeginArea(new Rect(0f, top, hierarchyWidth, bodyHeight));
            DrawHierarchyContent();
            GUILayout.EndArea();

            DrawVerticalSplitter(verticalSplitter);

            if (m_Preview != null)
            {
                m_Preview.Draw(previewRect);
            }
            else
            {
                EditorGUI.HelpBox(previewRect, "The preview is not available.", MessageType.Warning);
            }

            DrawHorizontalSplitter(horizontalSplitter);

            GUILayout.BeginArea(inspectorRect);
            DrawInspectorContent();
            GUILayout.EndArea();
        }

        void DrawVerticalSplitter(Rect rect)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.28f));

            var current = Event.current;
            if (current == null)
            {
                return;
            }

            switch (current.type)
            {
                case EventType.MouseDown:
                    if (current.button == 0 && rect.Contains(current.mousePosition))
                    {
                        if (current.clickCount == 2)
                        {
                            m_HierarchyWidth = kDefaultHierarchyWidth;
                            current.Use();
                            Repaint();
                            return;
                        }

                        m_ResizingHierarchy = true;
                        current.Use();
                    }

                    return;

                case EventType.MouseDrag:
                    if (m_ResizingHierarchy)
                    {
                        m_HierarchyWidth = Mathf.Clamp(
                            m_HierarchyWidth + current.delta.x,
                            kMinHierarchyWidth,
                            Mathf.Max(kMinHierarchyWidth, position.width * 0.6f));
                        current.Use();
                        Repaint();
                    }

                    return;

                case EventType.MouseUp:
                    m_ResizingHierarchy = false;
                    return;
            }
        }

        void DrawHorizontalSplitter(Rect rect)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, rect.width, 1f), new Color(0f, 0f, 0f, 0.28f));

            var current = Event.current;
            if (current == null)
            {
                return;
            }

            switch (current.type)
            {
                case EventType.MouseDown:
                    if (current.button == 0 && rect.Contains(current.mousePosition))
                    {
                        if (current.clickCount == 2)
                        {
                            m_InspectorHeight = kDefaultInspectorHeight;
                            current.Use();
                            Repaint();
                            return;
                        }

                        m_ResizingInspector = true;
                        current.Use();
                    }

                    return;

                case EventType.MouseDrag:
                    if (m_ResizingInspector)
                    {
                        m_InspectorHeight = Mathf.Clamp(
                            m_InspectorHeight - current.delta.y,
                            kMinInspectorHeight,
                            Mathf.Max(kMinInspectorHeight, position.height - kMinPreviewHeight - 80f));
                        current.Use();
                        Repaint();
                    }

                    return;

                case EventType.MouseUp:
                    m_ResizingInspector = false;
                    return;
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(m_Session.State == PrefabSessionState.Applying))
                {
                    if (GUILayout.Button("Apply", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                    {
                        RequestApply();
                    }

                    if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                    {
                        RequestRevert();
                    }
                }

                if (GUILayout.Button("Frame", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                {
                    m_Preview?.FrameAll();
                }

                var playback = m_Preview != null ? m_Preview.Playback : null;
                var hasPlayback = playback != null && playback.HasParticleSystems;

                using (new EditorGUI.DisabledScope(!hasPlayback))
                {
                    GUILayout.Space(6f);
                    var label = playback != null && playback.IsPlaying ? "Pause" : "Play";
                    if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(48f)) && hasPlayback)
                    {
                        if (playback.IsPlaying)
                        {
                            playback.Pause();
                        }
                        else
                        {
                            playback.Resume();
                        }

                        m_LastPlaybackTick = EditorApplication.timeSinceStartup;
                    }

                    if (GUILayout.Button("Restart", EditorStyles.toolbarButton, GUILayout.Width(56f)) && hasPlayback)
                    {
                        playback.Restart();
                        playback.Resume();
                        m_LastPlaybackTick = EditorApplication.timeSinceStartup;
                    }

                    var loop = GUILayout.Toggle(hasPlayback && playback.Loop, "Loop", EditorStyles.toolbarButton, GUILayout.Width(44f));
                    if (hasPlayback)
                    {
                        playback.Loop = loop;
                    }

                    var speed = playback != null ? playback.Speed : 1f;
                    var newSpeed = GUILayout.HorizontalSlider(speed, VfxPreviewPlayback.MinSpeed, VfxPreviewPlayback.MaxSpeed, GUILayout.Width(70f));
                    if (hasPlayback && !Mathf.Approximately(newSpeed, speed))
                    {
                        playback.Speed = newSpeed;
                    }

                    GUILayout.Label(speed.ToString("0.0") + "x", EditorStyles.miniLabel, GUILayout.Width(30f));
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(DescribeState(), EditorStyles.miniLabel, GUILayout.Width(110f));
                GUILayout.Label(m_Session.AssetPath, EditorStyles.miniLabel);
            }
        }

        string DescribeState()
        {
            switch (m_Session.State)
            {
                case PrefabSessionState.Loading:
                    return "Loading";
                case PrefabSessionState.Clean:
                    return "Saved";
                case PrefabSessionState.Dirty:
                    return "Unapplied changes";
                case PrefabSessionState.Conflict:
                    return "Changed on disk";
                case PrefabSessionState.Applying:
                    return "Saving";
                default:
                    return "Closed";
            }
        }

        void DrawHierarchyContent()
        {
            EditorGUILayout.LabelField("Hierarchy", EditorStyles.boldLabel);
            m_HierarchyScroll = EditorGUILayout.BeginScrollView(m_HierarchyScroll, false, false);
            m_Hierarchy?.Draw(m_Session, Mathf.Max(60f, m_HierarchyWidth - 24f));
            EditorGUILayout.EndScrollView();
        }

        void DrawInspectorContent()
        {
            EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
            m_InspectorScroll = EditorGUILayout.BeginScrollView(m_InspectorScroll, false, false);
            m_Inspector?.Draw();
            EditorGUILayout.EndScrollView();
        }

        void RequestApply()
        {
            if (m_Session == null)
            {
                return;
            }

            m_Session.RefreshState();
            var outcome = m_Session.Apply(false, out var error);
            if (outcome == PrefabApplyOutcome.Conflict)
            {
                var choice = EditorUtility.DisplayDialogComplex(
                    "Multitude: the prefab changed on disk",
                    error + "\n\nChoose what to do with your changes.",
                    "Overwrite the file",
                    "Keep editing",
                    "Reload from disk");

                if (choice == 0)
                {
                    outcome = m_Session.Apply(true, out error);
                }
                else if (choice == 2)
                {
                    RequestRevert(true);
                    return;
                }
                else
                {
                    SetStatus("Nothing was written. Your changes are still in this window.", MessageType.Info);
                    return;
                }
            }

            switch (outcome)
            {
                case PrefabApplyOutcome.Applied:
                    SetStatus($"Applied to '{m_Session.AssetPath}'.", MessageType.Info);
                    UpdateTitle();
                    break;
                case PrefabApplyOutcome.NothingToDo:
                    SetStatus("There were no changes to apply.", MessageType.Info);
                    break;
                case PrefabApplyOutcome.Failed:
                    SetStatus(error, MessageType.Error);
                    break;
            }
        }

        void RequestRevert(bool skipConfirmation = false)
        {
            if (m_Session == null)
            {
                return;
            }

            m_Session.RefreshState();
            if (!skipConfirmation && m_Session.IsDirty)
            {
                var discard = EditorUtility.DisplayDialog(
                    "Multitude: discard changes",
                    $"Discard every change made to '{m_Session.DisplayName}' and reload it from disk?",
                    "Discard and reload",
                    "Cancel");

                if (!discard)
                {
                    return;
                }
            }

            if (!m_Session.Revert(out var error))
            {
                SetStatus(error, MessageType.Error);
                return;
            }

            BuildViews();
            SetStatus("Reloaded from disk.", MessageType.Info);
            UpdateTitle();
            Repaint();
        }
    }
}
