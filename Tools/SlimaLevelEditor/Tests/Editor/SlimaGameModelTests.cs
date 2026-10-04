using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlimaPrototype.Tests
{
    public sealed class SlimaGameModelTests
    {
        [Test]
        public void JsonRoundTrip_PreservesLevel()
        {
            SlimaLevelData source = SlimaLevelData.CreatePrototype();
            SlimaLevelData result = SlimaLevelJson.FromJson(SlimaLevelJson.ToJson(source));
            Assert.AreEqual(16, result.width);
            Assert.AreEqual(source.entities.Count, result.entities.Count);
            Assert.AreEqual(SlimaTerrainType.Acid, result.GetTerrain(new Vector2Int(7, 8)));
        }

        [Test]
        public void Move_IsGridBasedAndConsumesGel()
        {
            SlimaGameModel model = ModelFor(EmptyLevel());
            Assert.IsTrue(model.Move(Vector2Int.right));
            Assert.AreEqual(new Vector2Int(2, 1), model.ActiveSlime.cell);
            Assert.AreEqual(99, model.ActiveSlime.gel);
        }

        [Test]
        public void Split_CopiesRecipeAndConservesGel()
        {
            SlimaLevelData level = EmptyLevel();
            level.entities.Add(Entity("split", SlimaEntityType.Splitter, 2, 1));
            SlimaGameModel model = ModelFor(level);
            Assert.IsTrue(model.Move(Vector2Int.right));
            Assert.AreEqual(2, model.Slimes.Count);
            Assert.AreEqual(99, model.Slimes.Sum(item => item.gel));
            Assert.AreEqual(model.Slimes[0].recipe, model.Slimes[1].recipe);
        }

        [Test]
        public void PressurePlate_OpensLinkedDoor()
        {
            SlimaLevelData level = EmptyLevel();
            level.entities.Add(Entity("plate", SlimaEntityType.PressurePlate, 2, 1, "gate", 10));
            SlimaEntityData door = Entity("door", SlimaEntityType.Door, 3, 1, "gate");
            level.entities.Add(door);
            SlimaGameModel model = ModelFor(level);
            Assert.IsTrue(model.Move(Vector2Int.right));
            Assert.IsTrue(model.IsDoorOpen(door));
            Assert.IsTrue(model.Move(Vector2Int.right));
        }

        [Test]
        public void Undo_RestoresPositionAndGel()
        {
            SlimaGameModel model = ModelFor(EmptyLevel());
            model.Move(Vector2Int.right);
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(new Vector2Int(1, 1), model.ActiveSlime.cell);
            Assert.AreEqual(100, model.ActiveSlime.gel);
        }

        [Test]
        public void MatchingFilteredLight_CompletesLevel()
        {
            SlimaLevelData level = EmptyLevel();
            level.rules.winCondition = SlimaWinCondition.ActivateAllReceivers;
            level.entities.RemoveAll(item => item.type == SlimaEntityType.Spawn);
            level.entities.RemoveAll(item => item.type == SlimaEntityType.LightEmitter || item.type == SlimaEntityType.LightReceiver);
            level.entities.Add(Entity("spawn", SlimaEntityType.Spawn, 3, 6, recipe: SlimaRecipe.Red));
            level.entities.Add(new SlimaEntityData { instanceId = "emitter", type = SlimaEntityType.LightEmitter, x = 1, z = 6, direction = 1 });
            level.entities.Add(Entity("receiver", SlimaEntityType.LightReceiver, 6, 6, recipe: SlimaRecipe.Red));
            SlimaGameModel model = ModelFor(level);
            Assert.IsTrue(model.Won);
            Assert.IsTrue(model.IsReceiverActive("receiver"));
        }

        [Test]
        public void ReachingExit_CompletesLevel()
        {
            SlimaLevelData level = EmptyLevel();
            level.entities.Add(Entity("exit", SlimaEntityType.Exit, 2, 1));
            SlimaGameModel model = ModelFor(level);
            Assert.IsFalse(model.Won);
            Assert.IsTrue(model.Move(Vector2Int.right));
            Assert.IsTrue(model.ExitReached);
            Assert.IsTrue(model.Won);
        }

        [Test]
        public void MatchingColor_PreventsEnvironmentDamage()
        {
            SlimaLevelData level = EmptyLevel();
            SlimaEntityData spawn = level.entities.First(item => item.type == SlimaEntityType.Spawn);
            spawn.recipe = SlimaRecipe.Yellow;
            level.SetTerrain(new Vector2Int(2, 1), SlimaTerrainType.Acid);
            SlimaGameModel model = ModelFor(level);
            model.Move(Vector2Int.right);
            Assert.AreEqual(99, model.ActiveSlime.gel);
        }

        [Test]
        public void PrototypeScene_HasLevelReference()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototype/SlimaPrototype.unity");
            SlimaGameBootstrap bootstrap = Object.FindObjectOfType<SlimaGameBootstrap>();
            Assert.IsNotNull(bootstrap);
            Assert.IsNotNull(bootstrap.LevelJson);
            Assert.IsNotNull(bootstrap.GlobalConfigJson);
            Assert.AreEqual("Prototype_01", bootstrap.LevelJson.name);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Game/Data/Levels/Prototype_01.json"));
        }

        [Test]
        public void GlobalConfig_ControlsItemValues()
        {
            SlimaLevelData level = EmptyLevel();
            level.entities.Add(Entity("potion", SlimaEntityType.GelPotion, 2, 1));
            SlimaGlobalGameplayConfig config = SlimaGlobalGameplayConfig.CreateDefault();
            config.gel.initialGel = 50;
            config.items.potionRestore = 9;
            SlimaGameModel model = new SlimaGameModel();
            model.Initialize(level, config);
            model.Move(Vector2Int.right);
            Assert.AreEqual(58, model.ActiveSlime.gel);
        }

        private static SlimaGameModel ModelFor(SlimaLevelData level)
        {
            var model = new SlimaGameModel();
            model.Initialize(level);
            return model;
        }

        private static SlimaLevelData EmptyLevel()
        {
            var level = new SlimaLevelData { width = 8, height = 8 };
            level.entities.Add(Entity("spawn", SlimaEntityType.Spawn, 1, 1));
            level.entities.Add(Entity("emitter", SlimaEntityType.LightEmitter, 1, 6));
            level.entities.Add(Entity("receiver", SlimaEntityType.LightReceiver, 6, 6, recipe: SlimaRecipe.Red));
            return level;
        }

        private static SlimaEntityData Entity(string id, SlimaEntityType type, int x, int z, string link = "", int value = 0, SlimaRecipe recipe = SlimaRecipe.None)
        {
            return new SlimaEntityData { instanceId = id, type = type, x = x, z = z, linkId = link, value = value, recipe = recipe };
        }
    }
}
