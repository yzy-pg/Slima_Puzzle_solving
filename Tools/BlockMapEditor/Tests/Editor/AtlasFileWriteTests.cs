using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using NUnit.Framework;

namespace BlockMap.Tests
{
    public sealed class AtlasFileWriteTests
    {
        string _directory;
        string _path;
        static readonly byte[] Original = { 1, 2, 3, 4 };
        static readonly byte[] Changed = { 9, 8, 7, 6, 5 };

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "SlimaAtlasWrite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "atlas.png");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }

        static bool Write(string path, byte[] bytes)
        {
            var method = typeof(BlockMapLevelBuilder).GetMethod("WriteAtlasIfChanged", BindingFlags.Static | BindingFlags.NonPublic);
            try { return (bool)method.Invoke(null, new object[] { path, bytes }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [Test]
        public void CreatesNewAtlasAndLeavesNoTemporaryFiles()
        {
            Assert.IsTrue(Write(_path, Original));
            CollectionAssert.AreEqual(Original, File.ReadAllBytes(_path));
            Assert.AreEqual(1, Directory.GetFiles(_directory).Length);
        }

        [Test]
        public void IdenticalAtlasIsNotRewrittenEvenWhileMapped()
        {
            File.WriteAllBytes(_path, Original);
            DateTime timestamp = File.GetLastWriteTimeUtc(_path);
            using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var mapping = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false))
            using (var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
            {
                Assert.IsFalse(Write(_path, Original));
                Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(_path));
                Assert.AreEqual(1, view.ReadByte(0));
            }
        }

        [Test]
        [Platform("Win")]
        public void ChangedMappedAtlasReplacesFileWithoutWin32Error1224OrChangingMeta()
        {
            File.WriteAllBytes(_path, Original);
            File.WriteAllText(_path + ".meta", "guid: 1234567890abcdef1234567890abcdef\n");
            string meta = File.ReadAllText(_path + ".meta");
            using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var mapping = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false))
            using (var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
            {
                var error = Assert.Throws<IOException>(() => File.WriteAllBytes(_path, Changed));
                Assert.AreEqual(1224, error.HResult & 0xFFFF, "Reproduce the actual mapped-file truncation failure.");
                Assert.IsTrue(Write(_path, Changed));
                CollectionAssert.AreEqual(Changed, File.ReadAllBytes(_path));
                Assert.AreEqual(1, view.ReadByte(0), "Existing mapped readers must remain valid.");
                Assert.AreEqual(meta, File.ReadAllText(_path + ".meta"));
            }
            Assert.AreEqual(2, Directory.GetFiles(_directory).Length);
        }

        [Test]
        [Platform("Win")]
        public void FailedReplacementKeepsOriginalAtlasAndCleansTemporaryFile()
        {
            File.WriteAllBytes(_path, Original);
            using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(() => Write(_path, Changed));
                CollectionAssert.AreEqual(Original, File.ReadAllBytes(_path));
                Assert.AreEqual(1, Directory.GetFiles(_directory).Length);
            }
        }
    }
}
