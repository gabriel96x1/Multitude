using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Multitude.Tests
{
    public sealed class VfxPreviewPlaybackTests
    {
        GameObject m_Root;

        [TearDown]
        public void TearDown()
        {
            if (m_Root != null)
            {
                Object.DestroyImmediate(m_Root);
                m_Root = null;
            }
        }

        GameObject CreateParticleObject()
        {
            var root = EditorUtility.CreateGameObjectWithHideFlags("Multitude Playback Test", HideFlags.HideAndDontSave);
            m_Root = root;
            var system = root.AddComponent<ParticleSystem>();

            var main = system.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = 1f;
            main.startSpeed = 0f;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 20f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;

            return root;
        }

        [Test]
        public void Bind_RegistersTheSystemsOfTheCopy()
        {
            var root = CreateParticleObject();
            var playback = new VfxPreviewPlayback();

            playback.Bind(root);

            Assert.IsTrue(playback.HasParticleSystems);
            Assert.AreEqual(1, playback.SystemCount);
            Assert.IsTrue(playback.IsPlaying);
        }

        [Test]
        public void Advance_SimulatesTheSystemsWithoutPlayMode()
        {
            var root = CreateParticleObject();
            var system = root.GetComponent<ParticleSystem>();
            var playback = new VfxPreviewPlayback();
            playback.Bind(root);

            var advanced = playback.Advance(0.5d);

            Assert.IsTrue(advanced);
            Assert.Greater(system.particleCount, 0);
            Assert.Greater(playback.Elapsed, 0d);
        }

        [Test]
        public void Pause_StopsAdvancing()
        {
            var root = CreateParticleObject();
            var playback = new VfxPreviewPlayback();
            playback.Bind(root);
            playback.Advance(0.2d);

            playback.Pause();

            Assert.IsFalse(playback.IsPlaying);
            Assert.IsFalse(playback.Advance(0.5d));
        }

        [Test]
        public void Restart_ClearsTheParticlesAndStartsAgain()
        {
            var root = CreateParticleObject();
            var system = root.GetComponent<ParticleSystem>();
            var playback = new VfxPreviewPlayback();
            playback.Bind(root);
            playback.Advance(0.4d);
            Assert.Greater(system.particleCount, 0);

            playback.Restart();

            Assert.AreEqual(0d, playback.Elapsed, 0.0001d);
            Assert.AreEqual(0, system.particleCount);
            Assert.IsTrue(playback.IsPlaying);
        }

        [Test]
        public void Speed_IsClamped()
        {
            var playback = new VfxPreviewPlayback();

            playback.Speed = 99f;
            Assert.AreEqual(VfxPreviewPlayback.MaxSpeed, playback.Speed);

            playback.Speed = -5f;
            Assert.AreEqual(VfxPreviewPlayback.MinSpeed, playback.Speed);
        }

        [Test]
        public void Bind_WithoutParticleSystems_DoesNotPlay()
        {
            var root = EditorUtility.CreateGameObjectWithHideFlags("Multitude Empty Test", HideFlags.HideAndDontSave);
            m_Root = root;
            var playback = new VfxPreviewPlayback();

            playback.Bind(root);

            Assert.IsFalse(playback.HasParticleSystems);
            Assert.IsFalse(playback.IsPlaying);
        }
    }
}
