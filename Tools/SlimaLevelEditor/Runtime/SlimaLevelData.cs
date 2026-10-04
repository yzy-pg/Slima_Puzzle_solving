using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimaPrototype
{
    public enum SlimaTerrainType
    {
        Floor = 0,
        Wall = 1,
        Acid = 2,
        Heat = 3,
        Cold = 4
    }

    public enum SlimaEntityType
    {
        Spawn = 0,
        PaintStation = 1,
        Splitter = 2,
        LightEmitter = 3,
        LightReceiver = 4,
        PressurePlate = 5,
        Button = 6,
        Lever = 7,
        Door = 8,
        GelPotion = 9,
        GelPill = 10,
        TransferPipe = 11,
        LaserSplitter = 12,
        CatapultSplitter = 13,
        Exit = 14
    }

    public enum SlimaWinCondition
    {
        ReachExit = 0,
        ActivateAllReceivers = 1,
        ExitAndReceivers = 2
    }

    [Flags]
    public enum SlimaRecipe
    {
        None = 0,
        Red = 1,
        Yellow = 2,
        Blue = 4,
        All = Red | Yellow | Blue
    }

    [Serializable]
    public sealed class SlimaLevelRules
    {
        public string _说明 = "胜利条件始终由关卡决定；其余数值为 -1 时继承全局配置。布尔覆盖值：-1 继承、0 否、1 是。";
        public SlimaWinCondition winCondition = SlimaWinCondition.ReachExit;
        public int initialGel = -1;
        public int maxGel = -1;
        public int baseMoveCost = -1;
        public int environmentDamage = -1;
        public int splitThreshold = -1;
        public int buttonDurationSteps = -1;
        public int transferAmount = -1;
        public int maxSlimes = -1;
        public int capGelAtMax = -1;
        public int anySlimeZeroFails = -1;
    }

    [Serializable]
    public sealed class SlimaCellData
    {
        public int x;
        public int z;
        public SlimaTerrainType terrain;

        public SlimaCellData() { }

        public SlimaCellData(int x, int z, SlimaTerrainType terrain)
        {
            this.x = x;
            this.z = z;
            this.terrain = terrain;
        }
    }

    [Serializable]
    public sealed class SlimaEntityData
    {
        public string instanceId = "entity";
        public SlimaEntityType type;
        public int x;
        public int z;
        [Range(0, 3)] public int direction;
        public SlimaRecipe recipe;
        public string linkId = "";
        public int value;

        public Vector2Int Cell => new Vector2Int(x, z);

        public SlimaEntityData Clone()
        {
            return new SlimaEntityData
            {
                instanceId = instanceId,
                type = type,
                x = x,
                z = z,
                direction = direction,
                recipe = recipe,
                linkId = linkId,
                value = value
            };
        }
    }

    [Serializable]
    public sealed class SlimaLevelData
    {
        public int version = 1;
        public string levelId = "prototype_01";
        public string displayName = "原型关卡 01";
        public int width = 16;
        public int height = 16;
        public SlimaLevelRules rules = new SlimaLevelRules();
        public List<SlimaCellData> cells = new List<SlimaCellData>();
        public List<SlimaEntityData> entities = new List<SlimaEntityData>();

        public bool InBounds(Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;
        }

        public SlimaTerrainType GetTerrain(Vector2Int cell)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                SlimaCellData candidate = cells[i];
                if (candidate.x == cell.x && candidate.z == cell.y)
                {
                    return candidate.terrain;
                }
            }

            return SlimaTerrainType.Floor;
        }

        public void SetTerrain(Vector2Int cell, SlimaTerrainType terrain)
        {
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                if (cells[i].x == cell.x && cells[i].z == cell.y)
                {
                    cells.RemoveAt(i);
                }
            }

            if (terrain != SlimaTerrainType.Floor)
            {
                cells.Add(new SlimaCellData(cell.x, cell.y, terrain));
            }
        }

        public List<SlimaEntityData> GetEntities(Vector2Int cell)
        {
            var result = new List<SlimaEntityData>();
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i].x == cell.x && entities[i].z == cell.y)
                {
                    result.Add(entities[i]);
                }
            }

            return result;
        }

        public SlimaLevelData Clone()
        {
            return SlimaLevelJson.FromJson(SlimaLevelJson.ToJson(this));
        }

        public static SlimaLevelData CreatePrototype()
        {
            var level = new SlimaLevelData();

            for (int x = 0; x < level.width; x++)
            {
                level.SetTerrain(new Vector2Int(x, 0), SlimaTerrainType.Wall);
                level.SetTerrain(new Vector2Int(x, level.height - 1), SlimaTerrainType.Wall);
            }

            for (int z = 1; z < level.height - 1; z++)
            {
                level.SetTerrain(new Vector2Int(0, z), SlimaTerrainType.Wall);
                level.SetTerrain(new Vector2Int(level.width - 1, z), SlimaTerrainType.Wall);
            }

            for (int x = 6; x <= 8; x++) level.SetTerrain(new Vector2Int(x, 8), SlimaTerrainType.Acid);
            for (int x = 9; x <= 11; x++) level.SetTerrain(new Vector2Int(x, 10), SlimaTerrainType.Heat);
            for (int x = 4; x <= 6; x++) level.SetTerrain(new Vector2Int(x, 12), SlimaTerrainType.Cold);

            level.entities.Add(Entity("spawn", SlimaEntityType.Spawn, 1, 1));
            level.entities.Add(Entity("paint_red", SlimaEntityType.PaintStation, 3, 2, recipe: SlimaRecipe.Red));
            level.entities.Add(Entity("paint_yellow", SlimaEntityType.PaintStation, 5, 2, recipe: SlimaRecipe.Yellow));
            level.entities.Add(Entity("paint_blue", SlimaEntityType.PaintStation, 7, 2, recipe: SlimaRecipe.Blue));
            level.entities.Add(Entity("splitter", SlimaEntityType.Splitter, 5, 5, direction: 0));
            level.entities.Add(Entity("plate", SlimaEntityType.PressurePlate, 9, 4, linkId: "door_a", value: 45));
            level.entities.Add(Entity("door_a", SlimaEntityType.Door, 11, 4, linkId: "door_a"));
            level.entities.Add(Entity("button", SlimaEntityType.Button, 9, 6, linkId: "door_b"));
            level.entities.Add(Entity("door_b", SlimaEntityType.Door, 11, 6, linkId: "door_b"));
            level.entities.Add(Entity("lever", SlimaEntityType.Lever, 9, 8, linkId: "door_c"));
            level.entities.Add(Entity("door_c", SlimaEntityType.Door, 11, 8, linkId: "door_c"));
            level.entities.Add(Entity("gel_potion", SlimaEntityType.GelPotion, 3, 10));
            level.entities.Add(Entity("gel_pill", SlimaEntityType.GelPill, 4, 10));
            level.entities.Add(Entity("transfer", SlimaEntityType.TransferPipe, 7, 6));
            level.entities.Add(Entity("emitter", SlimaEntityType.LightEmitter, 1, 13, direction: 1));
            level.entities.Add(Entity("receiver_red", SlimaEntityType.LightReceiver, 14, 13, recipe: SlimaRecipe.Red));
            level.entities.Add(Entity("exit", SlimaEntityType.Exit, 14, 14));
            return level;
        }

        private static SlimaEntityData Entity(
            string id,
            SlimaEntityType type,
            int x,
            int z,
            int direction = 0,
            SlimaRecipe recipe = SlimaRecipe.None,
            string linkId = "",
            int value = 0)
        {
            return new SlimaEntityData
            {
                instanceId = id,
                type = type,
                x = x,
                z = z,
                direction = direction,
                recipe = recipe,
                linkId = linkId,
                value = value
            };
        }
    }

    public enum SlimaIssueSeverity
    {
        Warning,
        Error
    }

    public sealed class SlimaLevelIssue
    {
        public readonly SlimaIssueSeverity severity;
        public readonly string message;

        public SlimaLevelIssue(SlimaIssueSeverity severity, string message)
        {
            this.severity = severity;
            this.message = message;
        }

        public override string ToString() => $"{severity}: {message}";
    }

    public static class SlimaLevelValidator
    {
        public static List<SlimaLevelIssue> Validate(SlimaLevelData level)
        {
            var issues = new List<SlimaLevelIssue>();
            if (level == null)
            {
                issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Error, "关卡数据为空"));
                return issues;
            }

            if (level.width < 2 || level.height < 2 || level.width > 64 || level.height > 64)
            {
                issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Error, "地图尺寸必须在 2 至 64 之间"));
            }

            var ids = new HashSet<string>();
            var doorLinks = new HashSet<string>();
            int spawnCount = 0;
            int emitterCount = 0;
            int receiverCount = 0;
            int exitCount = 0;

            for (int i = 0; i < level.entities.Count; i++)
            {
                SlimaEntityData entity = level.entities[i];
                if (!level.InBounds(entity.Cell))
                {
                    issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Error, $"{entity.instanceId} 超出地图范围"));
                }

                if (string.IsNullOrWhiteSpace(entity.instanceId) || !ids.Add(entity.instanceId))
                {
                    issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Error, $"实体 ID 为空或重复：{entity.instanceId}"));
                }

                if (entity.type == SlimaEntityType.Spawn) spawnCount++;
                if (entity.type == SlimaEntityType.LightEmitter) emitterCount++;
                if (entity.type == SlimaEntityType.Exit) exitCount++;
                if (entity.type == SlimaEntityType.LightReceiver)
                {
                    receiverCount++;
                    if (entity.recipe == SlimaRecipe.None || entity.recipe == SlimaRecipe.All)
                    {
                        issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Error, $"接收器 {entity.instanceId} 需要红 黄 蓝之一"));
                    }
                }

                if (entity.type == SlimaEntityType.Door && !string.IsNullOrWhiteSpace(entity.linkId))
                {
                    doorLinks.Add(entity.linkId);
                }
            }

            if (spawnCount != 1) issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Error, $"出生点数量必须为 1  当前为 {spawnCount}"));
            if (level.rules.winCondition != SlimaWinCondition.ActivateAllReceivers && exitCount != 1)
            {
                issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Error, $"当前胜利条件要求终点数量为 1  当前为 {exitCount}"));
            }
            if (emitterCount == 0) issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Warning, "没有光学发射器"));
            if (level.rules.winCondition != SlimaWinCondition.ReachExit && receiverCount == 0)
            {
                issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Error, "当前胜利条件要求至少一个光学接收器"));
            }

            for (int i = 0; i < level.entities.Count; i++)
            {
                SlimaEntityData entity = level.entities[i];
                bool controlsDoor = entity.type == SlimaEntityType.PressurePlate ||
                                    entity.type == SlimaEntityType.Button ||
                                    entity.type == SlimaEntityType.Lever;
                if (controlsDoor && (string.IsNullOrWhiteSpace(entity.linkId) || !doorLinks.Contains(entity.linkId)))
                {
                    issues.Add(new SlimaLevelIssue(SlimaIssueSeverity.Error, $"机关 {entity.instanceId} 未连接有效门"));
                }
            }

            return issues;
        }
    }

    public static class SlimaDirections
    {
        private static readonly Vector2Int[] Values =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        public static Vector2Int Get(int direction) => Values[Mathf.Abs(direction) % Values.Length];
    }
}
