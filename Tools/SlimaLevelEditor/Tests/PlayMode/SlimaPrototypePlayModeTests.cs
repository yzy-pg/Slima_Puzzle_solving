using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SlimaPrototype.Tests
{
    public sealed class SlimaPrototypePlayModeTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_BootstrapsRuntimeModel()
        {
            SceneManager.LoadScene("SlimaPrototype");
            yield return null;
            SlimaGameBootstrap bootstrap = Object.FindObjectOfType<SlimaGameBootstrap>();
            Assert.IsNotNull(bootstrap);
            Assert.IsNotNull(bootstrap.Model);
            Assert.AreEqual(16, bootstrap.Model.Level.width);
            Assert.AreEqual(1, bootstrap.Model.Slimes.Count);
        }
    }
}
