using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multitude.Tests
{
    public sealed class MultitudePreviewHostTests
    {
        [Test]
        public void Layer_IsAUserLayer()
        {
            Assert.GreaterOrEqual(MultitudePreviewHost.Layer, 8);
            Assert.LessOrEqual(MultitudePreviewHost.Layer, 31);
        }

        [Test]
        public void CreateHiddenObject_DoesNotDirtyTheOpenScene()
        {
            var dirtyBefore = SceneManager.GetActiveScene().isDirty;
            var created = MultitudePreviewHost.CreateHiddenObject("Multitude Test Hidden Object");

            try
            {
                Assert.IsNotNull(created);
                Assert.AreEqual(HideFlags.HideAndDontSave, created.hideFlags);
                Assert.AreEqual(dirtyBefore, SceneManager.GetActiveScene().isDirty);
            }
            finally
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }
        }

        [Test]
        public void PrepareForPreview_MovesEveryChildToThePreviewLayer()
        {
            var root = MultitudePreviewHost.CreateHiddenObject("Multitude Test Root");
            var child = new GameObject("Multitude Test Child");
            try
            {
                child.transform.SetParent(root.transform, false);

                MultitudePreviewHost.PrepareForPreview(root);

                Assert.AreEqual(MultitudePreviewHost.Layer, root.layer);
                Assert.AreEqual(MultitudePreviewHost.Layer, child.layer);
                Assert.AreEqual(HideFlags.HideAndDontSave, child.hideFlags);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void IsAlive_IsFalseForDestroyedObjects()
        {
            var created = MultitudePreviewHost.CreateHiddenObject("Multitude Test Alive");
            Assert.IsTrue(MultitudePreviewHost.IsAlive(created));

            Object.DestroyImmediate(created);

            Assert.IsFalse(MultitudePreviewHost.IsAlive(created));
            Assert.IsFalse(MultitudePreviewHost.IsAlive(null));
        }
    }
}
