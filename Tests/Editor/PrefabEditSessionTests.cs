using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multitude.Tests
{
    public sealed class PrefabEditSessionTests
    {
        [SetUp]
        public void SetUp()
        {
            TempPrefabFactory.EnsureFolder();
        }

        [TearDown]
        public void TearDown()
        {
            TempPrefabFactory.DeleteFolder();
        }

        [Test]
        public void TryOpen_InvalidPath_FailsWithMessage()
        {
            var opened = PrefabEditSession.TryOpen("Assets/MultitudeTestsTemp/Missing.prefab", out var session, out var error);

            Assert.IsFalse(opened);
            Assert.IsNull(session);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void TryOpen_RealPrefab_StartsCleanAndKeepsOpenScenesUntouched()
        {
            var path = TempPrefabFactory.CreatePrefab("SessionClean");
            var scenesBefore = SceneManager.sceneCount;

            var opened = PrefabEditSession.TryOpen(path, out var session, out var error);

            try
            {
                Assert.IsTrue(opened, error);
                Assert.AreEqual(PrefabSessionState.Clean, session.State);
                Assert.IsFalse(session.IsDirty);
                Assert.IsNotNull(session.Root);
                Assert.IsTrue(session.PreviewScene.IsValid());
                Assert.AreEqual(scenesBefore, SceneManager.sceneCount);
                Assert.AreEqual("SessionClean", session.DisplayName);
            }
            finally
            {
                session?.Dispose();
            }
        }

        [Test]
        public void EditingContent_MakesSessionDirty()
        {
            var path = TempPrefabFactory.CreatePrefab("SessionDirty");
            PrefabEditSession.TryOpen(path, out var session, out var error);

            try
            {
                Assert.IsTrue(session != null, error);
                var child = TempPrefabFactory.FindChild(session.Root, "Child");
                Assert.IsNotNull(child);

                child.localPosition = new Vector3(0f, 3f, 0f);
                Assert.IsTrue(session.ComputeDirty());
                Assert.IsTrue(session.RefreshState());
                Assert.AreEqual(PrefabSessionState.Dirty, session.State);
                Assert.IsTrue(session.IsDirty);
            }
            finally
            {
                session?.Dispose();
            }
        }

        [Test]
        public void EditingBackToTheOriginalValue_BecomesCleanAgain()
        {
            var path = TempPrefabFactory.CreatePrefab("SessionUndoBack");
            PrefabEditSession.TryOpen(path, out var session, out var error);

            try
            {
                Assert.IsTrue(session != null, error);
                var child = TempPrefabFactory.FindChild(session.Root, "Child");
                var original = child.localPosition;

                child.localPosition = new Vector3(2f, 2f, 2f);
                session.RefreshState();
                Assert.AreEqual(PrefabSessionState.Dirty, session.State);

                child.localPosition = original;
                session.RefreshState();
                Assert.AreEqual(PrefabSessionState.Clean, session.State);
                Assert.IsFalse(session.IsDirty);
            }
            finally
            {
                session?.Dispose();
            }
        }

        [Test]
        public void Fingerprint_OfMissingRoot_IsEmpty()
        {
            Assert.IsEmpty(PrefabContentFingerprint.Compute(null));
        }

        [Test]
        public void Revert_RestoresContentAndCleanState()
        {
            var path = TempPrefabFactory.CreatePrefab("SessionRevert");
            PrefabEditSession.TryOpen(path, out var session, out var error);

            try
            {
                Assert.IsTrue(session != null, error);
                var child = TempPrefabFactory.FindChild(session.Root, "Child");
                child.localPosition = new Vector3(0f, 9f, 0f);
                session.RefreshState();

                Assert.IsTrue(session.Revert(out var revertError), revertError);

                Assert.AreEqual(PrefabSessionState.Clean, session.State);
                Assert.IsFalse(session.IsDirty);
                var restored = TempPrefabFactory.FindChild(session.Root, "Child");
                Assert.IsNotNull(restored);
                Assert.AreEqual(1f, restored.localPosition.y, 0.001f);
            }
            finally
            {
                session?.Dispose();
            }
        }

        [Test]
        public void Apply_WritesToDiskAndResetsBaseline()
        {
            var path = TempPrefabFactory.CreatePrefab("SessionApply");
            var hashBefore = PrefabAssetSnapshot.Capture(path).ContentHash;
            PrefabEditSession.TryOpen(path, out var session, out var error);

            try
            {
                Assert.IsTrue(session != null, error);
                var child = TempPrefabFactory.FindChild(session.Root, "Child");
                child.localPosition = new Vector3(0f, 4f, 0f);
                session.RefreshState();

                var outcome = session.Apply(false, out var applyError);

                Assert.AreEqual(PrefabApplyOutcome.Applied, outcome, applyError);
                Assert.AreEqual(PrefabSessionState.Clean, session.State);
                Assert.IsFalse(session.IsDirty);
                var hashAfter = PrefabAssetSnapshot.Capture(path).ContentHash;
                Assert.AreNotEqual(hashBefore, hashAfter);
            }
            finally
            {
                session?.Dispose();
            }

            // Reopening reads the persisted value back from disk.
            PrefabEditSession.TryOpen(path, out var reopened, out var reopenError);
            try
            {
                Assert.IsTrue(reopened != null, reopenError);
                var child = TempPrefabFactory.FindChild(reopened.Root, "Child");
                Assert.IsNotNull(child);
                Assert.AreEqual(4f, child.localPosition.y, 0.001f);
            }
            finally
            {
                reopened?.Dispose();
            }
        }

        [Test]
        public void Apply_AfterExternalChange_ReportsConflictAndDoesNotWrite()
        {
            var path = TempPrefabFactory.CreatePrefab("SessionConflict");
            PrefabEditSession.TryOpen(path, out var session, out var error);

            try
            {
                Assert.IsTrue(session != null, error);
                TempPrefabFactory.FindChild(session.Root, "Child").localPosition = new Vector3(0f, 5f, 0f);
                session.RefreshState();

                TempPrefabFactory.MoveRootOnDisk(path, 21f);
                var externallyWrittenHash = PrefabAssetSnapshot.Capture(path).ContentHash;

                var outcome = session.Apply(false, out var applyError);

                Assert.AreEqual(PrefabApplyOutcome.Conflict, outcome);
                Assert.AreEqual(PrefabSessionState.Conflict, session.State);
                Assert.IsNotEmpty(applyError);
                Assert.AreEqual(externallyWrittenHash, PrefabAssetSnapshot.Capture(path).ContentHash);

                var overwritten = session.Apply(true, out var overwriteError);
                Assert.AreEqual(PrefabApplyOutcome.Applied, overwritten, overwriteError);
                Assert.AreNotEqual(externallyWrittenHash, PrefabAssetSnapshot.Capture(path).ContentHash);
            }
            finally
            {
                session?.Dispose();
            }
        }

        [Test]
        public void Apply_WithoutChanges_DoesNothing()
        {
            var path = TempPrefabFactory.CreatePrefab("SessionNoChanges");
            var hashBefore = PrefabAssetSnapshot.Capture(path).ContentHash;
            PrefabEditSession.TryOpen(path, out var session, out var error);

            try
            {
                Assert.IsTrue(session != null, error);
                var outcome = session.Apply(false, out var applyError);

                Assert.AreEqual(PrefabApplyOutcome.NothingToDo, outcome, applyError);
                Assert.AreEqual(hashBefore, PrefabAssetSnapshot.Capture(path).ContentHash);
            }
            finally
            {
                session?.Dispose();
            }
        }

        [Test]
        public void TwoSessions_AreIndependent()
        {
            var firstPath = TempPrefabFactory.CreatePrefab("SessionFirst", 1f);
            var secondPath = TempPrefabFactory.CreatePrefab("SessionSecond", 2f);
            var secondHashBefore = PrefabAssetSnapshot.Capture(secondPath).ContentHash;

            PrefabEditSession.TryOpen(firstPath, out var first, out var firstError);
            PrefabEditSession.TryOpen(secondPath, out var second, out var secondError);

            try
            {
                Assert.IsTrue(first != null, firstError);
                Assert.IsTrue(second != null, secondError);
                Assert.AreNotSame(first.Root, second.Root);
                Assert.AreNotEqual(first.PreviewScene.handle, second.PreviewScene.handle);

                TempPrefabFactory.FindChild(first.Root, "Child").localPosition = new Vector3(0f, 7f, 0f);
                first.RefreshState();

                Assert.AreEqual(PrefabSessionState.Dirty, first.State);
                Assert.AreEqual(PrefabSessionState.Clean, second.State);
                Assert.AreEqual(secondHashBefore, PrefabAssetSnapshot.Capture(secondPath).ContentHash);

                first.Dispose();
                Assert.IsTrue(first.IsDisposed);
                Assert.IsNotNull(second.Root);
                Assert.IsFalse(second.IsDisposed);
                Assert.AreEqual(PrefabSessionState.Clean, second.State);
            }
            finally
            {
                first?.Dispose();
                second?.Dispose();
            }
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var path = TempPrefabFactory.CreatePrefab("SessionDispose");
            PrefabEditSession.TryOpen(path, out var session, out var error);
            Assert.IsTrue(session != null, error);

            session.Dispose();
            Assert.DoesNotThrow(() => session.Dispose());
            Assert.IsTrue(session.IsDisposed);
            Assert.AreEqual(PrefabSessionState.Disposed, session.State);
        }
    }
}
