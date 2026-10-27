using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Multitude.Tests
{
    public sealed class PrefabAssetSnapshotTests
    {
        GameObject m_Source;

        [SetUp]
        public void SetUp()
        {
            TempPrefabFactory.EnsureFolder();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Source != null)
            {
                Object.DestroyImmediate(m_Source);
                m_Source = null;
            }

            TempPrefabFactory.DeleteFolder();
        }

        [Test]
        public void Validate_EmptyPath_IsRejected()
        {
            var validation = PrefabAssetValidator.Validate(string.Empty);

            Assert.IsFalse(validation.IsValid);
            Assert.AreEqual(PrefabAssetProblem.EmptyPath, validation.Problem);
            Assert.IsNotEmpty(validation.Message);
        }

        [Test]
        public void Validate_PathOutsideAssets_IsRejected()
        {
            var validation = PrefabAssetValidator.Validate("Library/Some.prefab");

            Assert.IsFalse(validation.IsValid);
            Assert.AreEqual(PrefabAssetProblem.OutsideProject, validation.Problem);
        }

        [Test]
        public void Validate_NonPrefabAsset_IsRejected()
        {
            var validation = PrefabAssetValidator.Validate("Assets/NotAPrefab.asset");

            Assert.IsFalse(validation.IsValid);
            Assert.AreEqual(PrefabAssetProblem.NotAPrefab, validation.Problem);
        }

        [Test]
        public void Validate_MissingPrefab_IsRejected()
        {
            var validation = PrefabAssetValidator.Validate("Assets/MultitudeTestsTemp/DoesNotExist.prefab");

            Assert.IsFalse(validation.IsValid);
            Assert.AreEqual(PrefabAssetProblem.Missing, validation.Problem);
            Assert.IsNotEmpty(validation.Message);
        }

        [Test]
        public void Validate_RealPrefab_IsAccepted()
        {
            var path = TempPrefabFactory.CreatePrefab("SnapshotValid");

            var validation = PrefabAssetValidator.Validate(path);

            Assert.IsTrue(validation.IsValid, validation.Message);
        }

        [Test]
        public void Capture_RepeatedReadsOfSameFile_Match()
        {
            var path = TempPrefabFactory.CreatePrefab("SnapshotStable");

            var first = PrefabAssetSnapshot.Capture(path);
            var second = PrefabAssetSnapshot.Capture(path);

            Assert.IsTrue(first.Exists);
            Assert.IsNotEmpty(first.ContentHash);
            Assert.IsTrue(first.Matches(second));
        }

        [Test]
        public void Capture_AfterExternalWrite_DoesNotMatch()
        {
            var path = TempPrefabFactory.CreatePrefab("SnapshotChanged");
            var before = PrefabAssetSnapshot.Capture(path);

            TempPrefabFactory.MoveRootOnDisk(path, 12f);
            var after = PrefabAssetSnapshot.Capture(path);

            Assert.IsFalse(before.Matches(after));
            Assert.IsTrue(after.SizeInBytes > 0);
        }

        [Test]
        public void Capture_MissingFile_ReportsMissing()
        {
            var snapshot = PrefabAssetSnapshot.Capture("Assets/MultitudeTestsTemp/NeverCreated.prefab");

            Assert.IsFalse(snapshot.Exists);
            Assert.IsEmpty(snapshot.ContentHash);
        }

        [Test]
        public void ComputeHash_IsDeterministic()
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5 };

            Assert.AreEqual(PrefabAssetSnapshot.ComputeHash(bytes), PrefabAssetSnapshot.ComputeHash((byte[])bytes.Clone()));
        }

        [Test]
        public void ToAbsolutePath_ProjectRelativePath_ResolvesUnderProjectRoot()
        {
            var absolute = PrefabAssetSnapshot.ToAbsolutePath("Assets/Thing.prefab");

            Assert.IsNotNull(absolute);
            StringAssert.StartsWith(PrefabAssetSnapshot.ProjectRoot, absolute);
        }

        [Test]
        public void ToAbsolutePath_PathOutsideAssets_IsNull()
        {
            Assert.IsNull(PrefabAssetSnapshot.ToAbsolutePath("Packages/com.gabs.multitude/package.json"));
        }
    }
}
