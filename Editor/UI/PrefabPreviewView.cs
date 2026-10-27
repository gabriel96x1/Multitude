using System;
using UnityEditor;
using UnityEngine;

namespace Multitude
{
    /// <summary>
    /// Renders one session's prefab inside an editor window and handles orbit, pan and zoom.
    ///
    /// The session keeps the prefab content in its own preview scene, which Unity never renders here.
    /// The picture comes from a throwaway copy of that content: it is hidden, put on a private layer
    /// and taken out of every camera with Renderer.forceRenderingOff, then switched on only for the
    /// frame this view renders into its own render texture. Nothing of Multitude reaches the user's
    /// scene, scene view, game view or prefab stage, and nothing of the user's scenes reaches the
    /// preview. Particle systems are simulated by hand so effects play in edit mode.
    /// </summary>
    public sealed class PrefabPreviewView : IDisposable
    {
        const float kMinPitch = -85f;
        const float kMaxPitch = 85f;
        const float kOrbitSpeed = 0.35f;
        const float kZoomSpeed = 0.06f;
        const double kContentCheckInterval = 0.35d;
        const float kVfxWarmUp = 0.35f;
        const float kMaxAutoRadius = 2.5f;

        readonly PrefabEditSession m_Session;
        readonly Action m_RequestRepaint;
        readonly VfxPreviewPlayback m_Playback = new VfxPreviewPlayback();

        RenderTexture m_Target;
        Camera m_Camera;
        Light m_Light;
        GameObject m_CameraObject;
        GameObject m_LightObject;
        GameObject m_Clone;
        string m_CloneFingerprint;
        double m_LastContentCheck;

        Vector3 m_Pivot;
        float m_Yaw = 135f;
        float m_Pitch = 18f;
        float m_Distance = 6f;
        float m_SceneRadius = 1f;
        bool m_FramePending = true;
        bool m_Orbiting;
        bool m_Panning;
        bool m_Disposed;
        bool m_RepaintScheduled;

        public PrefabPreviewView(PrefabEditSession session, Action requestRepaint)
        {
            m_Session = session;
            m_RequestRepaint = requestRepaint;
            EnsureEnvironment();
        }

        /// <summary>Object drawn with a highlight box in the preview. Set by the window from its hierarchy selection.</summary>
        public GameObject Highlighted { get; set; }

        public VfxPreviewPlayback Playback => m_Playback;

        public bool HasRig => m_Camera != null;

        /// <summary>Advances the particle effects. Returns true when the window should repaint.</summary>
        public bool Tick(double deltaSeconds)
        {
            if (m_Disposed)
            {
                return false;
            }

            if (m_Playback.Advance(deltaSeconds))
            {
                RequestRepaint();
                return true;
            }

            return false;
        }

        /// <summary>Frames the prefab, warming up the effects first when it is a pure VFX prefab.</summary>
        public void FrameAll()
        {
            m_FramePending = false;

            if (m_Session == null || m_Session.Root == null)
            {
                return;
            }

            WarmUpEffects();

            if (!TryGetBounds(m_Session.Root, out var bounds))
            {
                bounds = new Bounds(m_Session.Root.transform.position, Vector3.one);
            }

            FrameBounds(bounds, 1.25f);
        }

        /// <summary>Frames a single object (used by double click in the hierarchy).</summary>
        public void FrameObject(GameObject target)
        {
            if (target == null || !TryGetBounds(target, out var bounds))
            {
                return;
            }

            FrameBounds(bounds, 1.6f);
        }

        /// <summary>Forces the rendered copy to be rebuilt on the next draw.</summary>
        public void InvalidateContent()
        {
            m_CloneFingerprint = null;
            m_LastContentCheck = 0d;
        }

        public void Draw(Rect rect)
        {
            if (m_Disposed || rect.width < 16f || rect.height < 16f)
            {
                return;
            }

            EnsureEnvironment();

            if (!HasRig)
            {
                EditorGUI.HelpBox(rect, "The preview is not available. Close the window or press Revert to reload the prefab.", MessageType.Warning);
                return;
            }

            EnsureClone();

            if (m_Clone == null)
            {
                EditorGUI.HelpBox(rect, "This prefab has nothing to draw.", MessageType.Info);
                return;
            }

            if (m_FramePending)
            {
                FrameAll();
            }

            EnsureTarget(Mathf.Max(16, (int)rect.width), Mathf.Max(16, (int)rect.height));
            ApplyInput(rect);
            UpdateCameraTransform();

            if (Event.current != null && Event.current.type == EventType.Repaint && m_Target != null)
            {
                MultitudePreviewHost.SetRenderersEnabled(m_Clone, true);
                try
                {
                    m_Camera.Render();
                }
                finally
                {
                    MultitudePreviewHost.SetRenderersEnabled(m_Clone, false);
                }

                GUI.DrawTexture(rect, m_Target, ScaleMode.StretchToFill, false);
                DrawHighlight(rect);
            }
        }

        /// <summary>World bounds used for framing, taken from the renderers (or colliders) of the given object.</summary>
        public static bool TryGetBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            if (root == null)
            {
                return false;
            }

            if (TryAccumulate(root.GetComponentsInChildren<MeshRenderer>(true), ref bounds, out var any))
            {
                return true;
            }

            if (TryAccumulate(root.GetComponentsInChildren<SkinnedMeshRenderer>(true), ref bounds, out any))
            {
                return true;
            }

            var particleRenderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
            any = false;
            foreach (var renderer in particleRenderers)
            {
                if (renderer == null || renderer.bounds.size.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                bounds = any ? Encapsulate(bounds, renderer.bounds) : renderer.bounds;
                any = true;
            }

            if (any)
            {
                return true;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            any = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.bounds.size.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                bounds = any ? Encapsulate(bounds, renderer.bounds) : renderer.bounds;
                any = true;
            }

            if (any)
            {
                return true;
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                bounds = any ? Encapsulate(bounds, collider.bounds) : collider.bounds;
                any = true;
            }

            return any;
        }

        static bool TryAccumulate<T>(T[] renderers, ref Bounds bounds, out bool any) where T : Renderer
        {
            any = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || renderer.bounds.size.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                bounds = any ? Encapsulate(bounds, renderer.bounds) : renderer.bounds;
                any = true;
            }

            return any;
        }

        static Bounds Encapsulate(Bounds bounds, Bounds other)
        {
            bounds.Encapsulate(other);
            return bounds;
        }

        /// <summary>Releases the render texture, the camera, the light and the rendered copy.</summary>
        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;

            if (m_Camera != null)
            {
                m_Camera.targetTexture = null;
            }

            DestroyObject(m_Clone);
            m_Clone = null;
            DestroyObject(m_CameraObject);
            DestroyObject(m_LightObject);
            m_CameraObject = null;
            m_LightObject = null;
            m_Camera = null;
            m_Light = null;

            if (m_Target != null)
            {
                m_Target.Release();
                UnityEngine.Object.DestroyImmediate(m_Target);
                m_Target = null;
            }
        }

        void EnsureEnvironment()
        {
            if (m_Disposed)
            {
                return;
            }

            if (m_Camera != null && !MultitudePreviewHost.IsAlive(m_CameraObject))
            {
                m_Camera = null;
                m_Light = null;
                m_CameraObject = null;
                m_LightObject = null;
                m_Clone = null;
                m_CloneFingerprint = null;
            }

            if (m_Camera != null)
            {
                return;
            }

            var layer = MultitudePreviewHost.Layer;

            m_CameraObject = MultitudePreviewHost.CreateHiddenObject(MultitudePreviewHost.CameraName);
            m_Camera = m_CameraObject.AddComponent<Camera>();
            m_Camera.cameraType = CameraType.Preview;
            m_Camera.enabled = false;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0.17f, 0.17f, 0.19f, 1f);
            m_Camera.fieldOfView = 35f;
            m_Camera.nearClipPlane = 0.02f;
            m_Camera.farClipPlane = 5000f;
            m_Camera.allowHDR = false;
            m_Camera.allowMSAA = false;
            m_Camera.cullingMask = 1 << layer;

            m_LightObject = MultitudePreviewHost.CreateHiddenObject(MultitudePreviewHost.LightName);
            m_Light = m_LightObject.AddComponent<Light>();
            m_Light.type = LightType.Directional;
            m_Light.intensity = 1.2f;
            m_Light.cullingMask = 1 << layer;
            m_LightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        }

        void EnsureClone()
        {
            if (m_Session == null || m_Session.Root == null || m_Session.IsDisposed)
            {
                return;
            }

            if (m_Clone != null && !MultitudePreviewHost.IsAlive(m_Clone))
            {
                m_Clone = null;
                m_CloneFingerprint = null;
            }

            var now = EditorApplication.timeSinceStartup;
            var forced = string.IsNullOrEmpty(m_CloneFingerprint);
            if (!forced && now - m_LastContentCheck < kContentCheckInterval)
            {
                ScheduleRepaintWhileDirty();
                return;
            }

            m_LastContentCheck = now;
            var fingerprint = PrefabContentFingerprint.Compute(m_Session.Root);
            if (m_Clone != null && string.Equals(fingerprint, m_CloneFingerprint, StringComparison.Ordinal))
            {
                ScheduleRepaintWhileDirty();
                return;
            }

            RebuildClone(fingerprint);
            RequestRepaint();
        }

        void RebuildClone(string fingerprint)
        {
            DestroyObject(m_Clone);
            m_Clone = null;

            var holder = MultitudePreviewHost.CreateHiddenObject(MultitudePreviewHost.HolderName);
            try
            {
                m_Clone = UnityEngine.Object.Instantiate(m_Session.Root, holder.transform);
                m_Clone.transform.SetParent(null, true);
                m_Clone.name = MultitudePreviewHost.CopyName + " " + m_Session.DisplayName;
                MultitudePreviewHost.PrepareForPreview(m_Clone);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Multitude: the preview copy of '{m_Session.AssetPath}' failed ({exception.GetType().Name}: {exception.Message}).");
                DestroyObject(m_Clone);
                m_Clone = null;
            }
            finally
            {
                DestroyObject(holder);
            }

            m_CloneFingerprint = fingerprint;
            m_Playback.Bind(m_Clone);
        }

        /// <summary>Runs the effects forward so a pure VFX prefab has something to frame.</summary>
        void WarmUpEffects()
        {
            if (!m_Playback.HasParticleSystems)
            {
                return;
            }

            m_Playback.Restart();
            m_Playback.Advance(kVfxWarmUp);
        }

        void FrameBounds(Bounds bounds, float margin)
        {
            m_Pivot = bounds.center;
            m_SceneRadius = Mathf.Clamp(bounds.extents.magnitude, 0.25f, kMaxAutoRadius);
            var fieldOfView = m_Camera != null ? m_Camera.fieldOfView : 35f;
            m_Distance = m_SceneRadius / Mathf.Sin(fieldOfView * 0.5f * Mathf.Deg2Rad) * margin;
            m_Yaw = 135f;
            m_Pitch = 18f;
        }

        void EnsureTarget(int width, int height)
        {
            if (m_Target != null && (m_Target.width != width || m_Target.height != height))
            {
                m_Camera.targetTexture = null;
                m_Target.Release();
                UnityEngine.Object.DestroyImmediate(m_Target);
                m_Target = null;
            }

            if (m_Target == null)
            {
                m_Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "Multitude Preview Target",
                    antiAliasing = 1
                };
                m_Target.Create();
            }

            if (m_Camera.targetTexture != m_Target)
            {
                m_Camera.targetTexture = m_Target;
            }
        }

        void ApplyInput(Rect rect)
        {
            var current = Event.current;
            if (current == null)
            {
                return;
            }

            var inside = rect.Contains(current.mousePosition);
            switch (current.type)
            {
                case EventType.MouseDown:
                    if (!inside)
                    {
                        return;
                    }

                    if (current.button == 0 && !current.alt)
                    {
                        m_Orbiting = true;
                        current.Use();
                    }
                    else if (current.button == 1 || (current.button == 0 && current.alt) || current.button == 2)
                    {
                        m_Panning = true;
                        current.Use();
                    }

                    return;

                case EventType.MouseUp:
                    m_Orbiting = false;
                    m_Panning = false;
                    return;

                case EventType.ScrollWheel:
                    if (!inside)
                    {
                        return;
                    }

                    m_Distance = Mathf.Clamp(
                        m_Distance * (1f + current.delta.y * kZoomSpeed),
                        m_SceneRadius * 0.1f,
                        m_SceneRadius * 80f);
                    current.Use();
                    RequestRepaint();
                    return;

                case EventType.MouseDrag:
                    if (m_Orbiting)
                    {
                        m_Yaw += current.delta.x * kOrbitSpeed;
                        m_Pitch = Mathf.Clamp(m_Pitch - current.delta.y * kOrbitSpeed, kMinPitch, kMaxPitch);
                        current.Use();
                        RequestRepaint();
                        return;
                    }

                    if (m_Panning)
                    {
                        var rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
                        var scale = m_Distance * 0.0016f;
                        m_Pivot -= (rotation * Vector3.right) * (current.delta.x * scale);
                        m_Pivot -= (rotation * Vector3.up) * (current.delta.y * scale);
                        current.Use();
                        RequestRepaint();
                    }

                    return;

                case EventType.KeyDown:
                    if (current.keyCode == KeyCode.F && inside && !current.control && !current.command)
                    {
                        FrameAll();
                        current.Use();
                        RequestRepaint();
                    }

                    return;
            }
        }

        void UpdateCameraTransform()
        {
            if (m_Camera == null)
            {
                return;
            }

            var rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
            m_Camera.transform.rotation = rotation;
            m_Camera.transform.position = m_Pivot - rotation * Vector3.forward * m_Distance;
            m_Camera.nearClipPlane = Mathf.Max(0.01f, m_Distance * 0.002f);
            m_Camera.farClipPlane = Mathf.Max(100f, m_Distance * 20f);
        }

        void DrawHighlight(Rect rect)
        {
            if (Highlighted == null || m_Camera == null || !TryGetBounds(Highlighted, out var bounds))
            {
                return;
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var any = false;

            for (var x = 0; x < 2; x++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var z = 0; z < 2; z++)
                    {
                        var corner = bounds.center + new Vector3(
                            bounds.extents.x * (x * 2 - 1),
                            bounds.extents.y * (y * 2 - 1),
                            bounds.extents.z * (z * 2 - 1));
                        var viewport = m_Camera.WorldToViewportPoint(corner);
                        if (viewport.z <= 0f)
                        {
                            continue;
                        }

                        min = Vector2.Min(min, new Vector2(viewport.x, viewport.y));
                        max = Vector2.Max(max, new Vector2(viewport.x, viewport.y));
                        any = true;
                    }
                }
            }

            if (!any || max.x < 0f || min.x > 1f || max.y < 0f || min.y > 1f)
            {
                return;
            }

            var screenMin = new Vector2(
                rect.x + Mathf.Clamp01(min.x) * rect.width,
                rect.y + Mathf.Clamp01(1f - max.y) * rect.height);
            var screenMax = new Vector2(
                rect.x + Mathf.Clamp01(max.x) * rect.width,
                rect.y + Mathf.Clamp01(1f - min.y) * rect.height);

            var outline = new Rect(
                screenMin.x,
                screenMin.y,
                Mathf.Max(4f, screenMax.x - screenMin.x),
                Mathf.Max(4f, screenMax.y - screenMin.y));

            var color = new Color(1f, 0.62f, 0.12f, 0.9f);
            EditorGUI.DrawRect(new Rect(outline.x, outline.y, outline.width, 1f), color);
            EditorGUI.DrawRect(new Rect(outline.x, outline.yMax - 1f, outline.width, 1f), color);
            EditorGUI.DrawRect(new Rect(outline.x, outline.y, 1f, outline.height), color);
            EditorGUI.DrawRect(new Rect(outline.xMax - 1f, outline.y, 1f, outline.height), color);
        }

        void RequestRepaint()
        {
            m_RequestRepaint?.Invoke();
        }

        /// <summary>
        /// While the session has unapplied changes the copy is watched: a repaint is scheduled so a
        /// deferred rebuild happens without the user having to move the mouse.
        /// </summary>
        void ScheduleRepaintWhileDirty()
        {
            if (m_RepaintScheduled || m_Disposed || m_Session == null || !m_Session.IsDirty)
            {
                return;
            }

            m_RepaintScheduled = true;
            EditorApplication.delayCall += () =>
            {
                m_RepaintScheduled = false;
                if (!m_Disposed)
                {
                    RequestRepaint();
                }
            };
        }

        static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
