using System;
using System.IO;
using BlockMap;
using BlockMap.Gameplay;
using UnityEngine;

namespace SlimaPuzzle
{
    [Serializable]
    public sealed class SlimaSave
    {
        public int version = 1;
        public string scenePath;
        public string savedUtc;
        public bool hasProgress;
        public GameplayProgress progress;
    }

    public static class SlimaSaveStore
    {
        public const int SlotCount = 3;

        public static string SlotPath(int slot, string directory = null)
        {
            if (slot < 0 || slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            return Path.Combine(directory ?? Path.Combine(Application.persistentDataPath, "Saves"),
                $"slot-{slot + 1}.json");
        }

        public static SlimaSave Read(int slot, string directory = null)
        {
            string path = SlotPath(slot, directory);
            if (!File.Exists(path)) return null;
            SlimaSave save = JsonUtility.FromJson<SlimaSave>(File.ReadAllText(path));
            if (save == null || save.version != 1 || string.IsNullOrEmpty(save.scenePath) ||
                !DateTime.TryParse(save.savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out _))
                throw new InvalidDataException("存档格式无法读取。");
            if (!save.hasProgress) save.progress = null;
            GameplayProgress progress = save.progress;
            if (save.hasProgress)
            {
                if (progress == null || string.IsNullOrEmpty(progress.worldJson) || progress.actors == null ||
                    progress.actors.Length < 1 || progress.actors.Length > 2 ||
                    progress.activeActor < 0 || progress.activeActor >= progress.actors.Length)
                    throw new InvalidDataException("存档中的关卡进度不完整。");
                var world = JsonUtility.FromJson<BlockWorld.SaveData>(progress.worldJson);
                if (world == null || world.blocks == null || world.blocks.Count == 0)
                    throw new InvalidDataException("存档中的地图数据不完整。");
                foreach (SlimeProgress actor in progress.actors)
                    if (actor == null || actor.state == null ||
                        !Enum.IsDefined(typeof(SlimeElement), actor.state.element) ||
                        !Enum.IsDefined(typeof(SlimeForm), actor.state.form) ||
                        !Enum.IsDefined(typeof(SlimeSize), actor.state.size) ||
                        !Enum.IsDefined(typeof(BlueOpticsMode), actor.state.opticsMode))
                        throw new InvalidDataException("存档中的角色数据不完整。");
                if (progress.mechanisms != null)
                    foreach (MechanismProgress mechanism in progress.mechanisms)
                        if (mechanism == null) throw new InvalidDataException("存档中的机关数据不完整。");
            }
            return save;
        }

        public static void Write(int slot, SlimaSave save, string directory = null)
        {
            string path = SlotPath(slot, directory);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            save.savedUtc = DateTime.UtcNow.ToString("O");
            save.hasProgress = save.progress != null;
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(save, true));
            // Replace only after the complete JSON is on disk, preserving the old slot if writing fails.
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
