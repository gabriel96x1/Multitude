using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Multitude
{
    /// <summary>
    /// Drives the particle systems of a preview by hand.
    ///
    /// A preview lives outside play mode, where nothing advances the simulation on its own: the
    /// controller fast-forwards every system with ParticleSystem.Simulate on each tick, so effects
    /// play, loop and restart inside the window without entering play mode and without touching the
    /// user's scenes.
    /// </summary>
    public sealed class VfxPreviewPlayback
    {
        public const float MinSpeed = 0.1f;
        public const float MaxSpeed = 2f;

        const float FirstStepSeconds = 0.03f;

        readonly List<ParticleSystem> m_Systems = new List<ParticleSystem>();

        float m_Speed = 1f;
        double m_Elapsed;
        double m_LongestDuration = 1d;
        bool m_Playing;

        public bool IsPlaying => m_Playing;

        public bool HasParticleSystems => m_Systems.Count > 0;

        public bool Loop { get; set; } = true;

        public int SystemCount => m_Systems.Count;

        public float Speed
        {
            get => m_Speed;
            set => m_Speed = Mathf.Clamp(value, MinSpeed, MaxSpeed);
        }

        public double Elapsed => m_Elapsed;

        /// <summary>Registers every system inside the given copy and restarts the playback.</summary>
        public void Bind(GameObject previewCopy)
        {
            m_Systems.Clear();
            m_LongestDuration = 1d;

            if (previewCopy != null)
            {
                foreach (var system in previewCopy.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (system == null)
                    {
                        continue;
                    }

                    m_Systems.Add(system);
                    var duration = system.main.duration + system.main.startLifetime.constantMax;
                    if (duration > m_LongestDuration)
                    {
                        m_LongestDuration = duration;
                    }
                }
            }

            Restart();
        }

        /// <summary>Clears every system and starts the effects from the first frame.</summary>
        public void Restart()
        {
            m_Elapsed = 0d;

            foreach (var system in m_Systems)
            {
                if (system == null)
                {
                    continue;
                }

                system.Clear(true);
                system.Play(true);
                // A tiny first step is what actually makes the bursts of an impact effect exist:
                // Simulate(0) alone leaves the systems with zero particles in edit mode.
                system.Simulate(FirstStepSeconds, false, true);
            }

            m_Playing = m_Systems.Count > 0;
        }

        public void Pause()
        {
            m_Playing = false;
        }

        public void Resume()
        {
            m_Playing = m_Systems.Count > 0;
        }

        /// <summary>
        /// Advances the effects by the given real time. Returns true when a repaint is needed.
        /// </summary>
        public bool Advance(double realSeconds)
        {
            if (!m_Playing || m_Systems.Count == 0 || realSeconds <= 0d)
            {
                return false;
            }

            var step = (float)(realSeconds * m_Speed);
            var advanced = false;

            foreach (var system in m_Systems)
            {
                if (system == null)
                {
                    continue;
                }

                system.Simulate(step, false, false);
                advanced = true;
            }

            m_Elapsed += realSeconds * m_Speed;

            if (Loop && m_Elapsed > m_LongestDuration)
            {
                Restart();
            }
            else if (!Loop && m_Elapsed > m_LongestDuration)
            {
                m_Playing = false;
            }

            return advanced;
        }

        /// <summary>Total duration of the longest registered effect, in seconds.</summary>
        public double LongestDuration => m_LongestDuration;
    }
}
