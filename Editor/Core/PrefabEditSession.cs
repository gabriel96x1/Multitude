using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multitude
{
    /// <summary>Lifecycle states of an open prefab document.</summary>
    public enum PrefabSessionState
    {
        Empty,
        Loading,
        Clean,
        Dirty,
        Conflict,
        Applying,
        Disposed
    }

    /// <summary>Result of trying to persist a session into its prefab asset.</summary>
    public enum PrefabApplyOutcome
    {
        NothingToDo,
        Applied,
        Conflict,
        Failed
    }

    /// <summary>
    /// One open prefab document: an isolated preview scene that owns the prefab contents,
    /// the saved-state baseline used for dirty and conflict detection, and Apply/Revert.
    /// </summary>
    public sealed class PrefabEditSession : IDisposable
    {
        readonly List<GameObject> m_LoadedRoots = new List<GameObject>();

        string m_AssetPath;
        Scene m_PreviewScene;
        GameObject m_Root;
        PrefabAssetSnapshot m_Baseline;
        string m_BaselineFingerprint;
        PrefabSessionState m_State = PrefabSessionState.Empty;
        bool m_UndoHooked;
        bool m_Disposed;

        public string AssetPath => m_AssetPath;

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(m_AssetPath))
                {
                    return "Multitude";
                }

                return System.IO.Path.GetFileNameWithoutExtension(m_AssetPath);
            }
        }

        public PrefabSessionState State => m_State;

        public PrefabAssetSnapshot Baseline => m_Baseline;

        public Scene PreviewScene => m_PreviewScene;

        public GameObject Root => m_Root;

        public bool IsDisposed => m_Disposed;

        /// <summary>Raised when the session state changes (clean, dirty, conflict, disposed...).</summary>
        public event Action<PrefabEditSession> StateChanged;

        /// <summary>Raised when the session content may have changed and views should refresh.</summary>
        public event Action<PrefabEditSession> ContentChanged;

        PrefabEditSession(string assetPath)
        {
            m_AssetPath = assetPath;
        }

        /// <summary>Validates the asset and loads it into its own preview scene.</summary>
        public static bool TryOpen(string assetPath, out PrefabEditSession session, out string error)
        {
            session = null;

            var validation = PrefabAssetValidator.Validate(assetPath);
            if (!validation.IsValid)
            {
                error = validation.Message;
                return false;
            }

            var candidate = new PrefabEditSession(assetPath);
            if (!candidate.Load(out error))
            {
                candidate.Dispose();
                return false;
            }

            session = candidate;
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// True when the in-memory prefab content no longer matches the state captured at load time.
        /// This reads the last computed state; call <see cref="RefreshState"/> to recompute it.
        /// </summary>
        public bool IsDirty => m_State == PrefabSessionState.Dirty || m_State == PrefabSessionState.Conflict;

        /// <summary>Serializes the hierarchy and compares it against the baseline captured at load time.</summary>
        public bool ComputeDirty()
        {
            if (m_Disposed || m_Root == null)
            {
                return false;
            }

            return !string.Equals(PrefabContentFingerprint.Compute(m_Root), m_BaselineFingerprint, StringComparison.Ordinal);
        }

        /// <summary>Recomputes the dirty flag and updates the state when it changed.</summary>
        public bool RefreshState()
        {
            if (m_Disposed || m_Root == null)
            {
                return false;
            }

            if (m_State != PrefabSessionState.Clean && m_State != PrefabSessionState.Dirty)
            {
                return false;
            }

            var dirty = ComputeDirty();
            var next = dirty ? PrefabSessionState.Dirty : PrefabSessionState.Clean;
            if (next == m_State)
            {
                return false;
            }

            SetState(next);
            return true;
        }

        /// <summary>Forces the dirty state after an edit made through a path that cannot be observed.</summary>
        public void MarkDirty()
        {
            if (m_Disposed || m_State == PrefabSessionState.Disposed)
            {
                return;
            }

            if (m_State == PrefabSessionState.Clean || m_State == PrefabSessionState.Conflict)
            {
                SetState(PrefabSessionState.Dirty);
            }
        }

        /// <summary>True when the prefab file on disk differs from the snapshot taken when the session opened.</summary>
        public bool HasExternalChanges()
        {
            if (m_Disposed)
            {
                return false;
            }

            var current = PrefabAssetSnapshot.Capture(m_AssetPath);
            return !current.Matches(m_Baseline);
        }

        /// <summary>
        /// Writes the preview content back into the prefab asset.
        /// Never overwrites a file that changed on disk unless the caller explicitly allows it.
        /// </summary>
        public PrefabApplyOutcome Apply(bool overwriteExternalChanges, out string error)
        {
            error = string.Empty;

            if (m_Disposed || m_Root == null)
            {
                error = "The session is not open any more.";
                return PrefabApplyOutcome.Failed;
            }

            if (m_State == PrefabSessionState.Applying)
            {
                error = "The session is already saving.";
                return PrefabApplyOutcome.Failed;
            }

            if (!ComputeDirty())
            {
                SetState(PrefabSessionState.Clean);
                return PrefabApplyOutcome.NothingToDo;
            }

            if (HasExternalChanges() && !overwriteExternalChanges)
            {
                SetState(PrefabSessionState.Conflict);
                error = $"'{m_AssetPath}' changed on disk after it was opened. Multitude did not overwrite it.";
                return PrefabApplyOutcome.Conflict;
            }

            SetState(PrefabSessionState.Applying);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(m_Root, m_AssetPath);
            }
            catch (Exception exception)
            {
                error = $"Saving '{m_AssetPath}' failed: {exception.Message}";
                SetState(PrefabSessionState.Dirty);
                return PrefabApplyOutcome.Failed;
            }

            // Never report success before the asset can be loaded back from disk.
            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(m_AssetPath);
            if (reloaded == null)
            {
                error = $"'{m_AssetPath}' was written but cannot be loaded back from disk. Check the Console and the file on disk.";
                SetState(PrefabSessionState.Dirty);
                return PrefabApplyOutcome.Failed;
            }

            m_Baseline = PrefabAssetSnapshot.Capture(m_AssetPath);
            m_BaselineFingerprint = PrefabContentFingerprint.Compute(m_Root);
            SetState(PrefabSessionState.Clean);
            ContentChanged?.Invoke(this);
            return PrefabApplyOutcome.Applied;
        }

        /// <summary>Discards the preview content and reloads the prefab from disk.</summary>
        public bool Revert(out string error)
        {
            error = string.Empty;

            if (m_Disposed)
            {
                error = "The session is not open any more.";
                return false;
            }

            UnloadPreviewContent();

            if (!Load(out error))
            {
                SetState(PrefabSessionState.Empty);
                return false;
            }

            ContentChanged?.Invoke(this);
            return true;
        }

        /// <summary>Closes the preview scene and releases every resource exactly once.</summary>
        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            UnhookUndo();

            try
            {
                UnloadPreviewContent();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Multitude: closing the preview of '{m_AssetPath}' reported {exception.GetType().Name}: {exception.Message}");
            }

            m_BaselineFingerprint = null;
            SetState(PrefabSessionState.Disposed);
            StateChanged = null;
            ContentChanged = null;
        }

        bool Load(out string error)
        {
            error = string.Empty;
            SetState(PrefabSessionState.Loading);

            try
            {
                m_PreviewScene = EditorSceneManager.NewPreviewScene();
                PrefabUtility.LoadPrefabContentsIntoPreviewScene(m_AssetPath, m_PreviewScene);

                m_LoadedRoots.Clear();
                m_LoadedRoots.AddRange(m_PreviewScene.GetRootGameObjects());
                m_Root = PickPrefabRoot(m_LoadedRoots);

                if (m_Root == null)
                {
                    error = $"'{m_AssetPath}' loaded no objects into its preview scene.";
                    SetState(PrefabSessionState.Empty);
                    return false;
                }

                m_Baseline = PrefabAssetSnapshot.Capture(m_AssetPath);
                m_BaselineFingerprint = PrefabContentFingerprint.Compute(m_Root);
                HookUndo();
                SetState(PrefabSessionState.Clean);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Opening '{m_AssetPath}' failed: {exception.Message}";
                SetState(PrefabSessionState.Empty);
                return false;
            }
        }

        void UnloadPreviewContent()
        {
            m_LoadedRoots.Clear();
            m_Root = null;

            if (m_PreviewScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(m_PreviewScene);
            }

            m_PreviewScene = default;
        }

        static GameObject PickPrefabRoot(List<GameObject> roots)
        {
            if (roots == null || roots.Count == 0)
            {
                return null;
            }

            GameObject best = null;
            foreach (var candidate in roots)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (best == null || candidate.transform.childCount > best.transform.childCount)
                {
                    best = candidate;
                }
            }

            return best;
        }

        void HookUndo()
        {
            if (m_UndoHooked)
            {
                return;
            }

            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            m_UndoHooked = true;
        }

        void UnhookUndo()
        {
            if (!m_UndoHooked)
            {
                return;
            }

            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            m_UndoHooked = false;
        }

        void OnUndoRedoPerformed()
        {
            if (m_Disposed || m_Root == null)
            {
                return;
            }

            RefreshState();
            ContentChanged?.Invoke(this);
        }

        void SetState(PrefabSessionState state)
        {
            if (m_State == state)
            {
                return;
            }

            m_State = state;
            StateChanged?.Invoke(this);
        }
    }
}
