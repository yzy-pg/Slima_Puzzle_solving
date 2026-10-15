using System.Collections.Generic;
using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>同频道机关的颜色按稳定编号排序，门沿自身横向等宽显示每个机关的颜色。</summary>
    public static class BlockLinkVisuals
    {
        public static bool CanEditColor(GameplayBlockKind kind) => kind == GameplayBlockKind.Door ||
            kind == GameplayBlockKind.PressurePlate || kind == GameplayBlockKind.Button || kind == GameplayBlockKind.Lever;

        /// <summary>门换色时同步其关联机关；颜色修改作为一个可撤销笔画保存。</summary>
        public static void SetEditorColor(BlockWorld world, Vector3Int cell, Color? color)
        {
            var definition = BlockLibrary.Get(world.GetIndex(cell));
            var profile = definition != null ? BlockGameplayCatalog.Get(definition.id) : null;
            if (profile == null || !CanEditColor(profile.Kind)) return;
            world.BeginStroke();
            SetSavedColor(world, cell, color);
            if (profile.Kind == GameplayBlockKind.Door)
            {
                string channel = BlockLinkRules.ResolveChannel(world.GetLinkId(cell), profile, !world.HasLinkIds);
                if (channel.Length > 0) foreach (var item in world.Blocks)
                {
                    var sourceDefinition = BlockLibrary.Get(item.Value);
                    var source = sourceDefinition != null ? BlockGameplayCatalog.Get(sourceDefinition.id) : null;
                    if (source == null || !CanEditColor(source.Kind) || source.Kind == GameplayBlockKind.Door) continue;
                    if (BlockLinkRules.ResolveChannel(world.GetLinkId(item.Key), source, !world.HasLinkIds) == channel)
                        SetSavedColor(world, item.Key, color);
                }
            }
            world.EndStroke();
        }

        static void SetSavedColor(BlockWorld world, Vector3Int cell, Color? color)
        {
            if (color.HasValue) world.SetMechanismColor(cell, color.Value);
            else world.ClearMechanismColor(cell);
        }

        public static void Apply(BlockWorld world, IEnumerable<GameplayBlockView> views,
            IReadOnlyDictionary<int, Color> overrides = null)
        {
            world.EnsureMechanismNumbers();
            var groups = new Dictionary<string, List<Vector3Int>>();
            var targets = new HashSet<string>();
            foreach (var item in world.Blocks)
            {
                var definition = BlockLibrary.Get(item.Value);
                var profile = definition != null ? BlockGameplayCatalog.Get(definition.id) : null;
                if (profile == null || !BlockLinkRules.IsLinkable(profile.Kind)) continue;
                string channel = BlockLinkRules.ResolveChannel(world.GetLinkId(item.Key), profile, !world.HasLinkIds);
                if (channel.Length == 0) continue;
                if (profile.Kind == GameplayBlockKind.Door) { targets.Add(channel); continue; }
                if (!groups.TryGetValue(channel, out var sources))
                    groups[channel] = sources = new List<Vector3Int>();
                sources.Add(item.Key);
            }
            foreach (BlockWorld.SaveEntry covered in world.CoveredBlocks)
            {
                var profile = BlockGameplayCatalog.Get(covered.id);
                if (profile?.Kind != GameplayBlockKind.Door) continue;
                string channel = BlockLinkRules.ResolveChannel(covered.linkId, profile, !world.HasLinkIds);
                if (channel.Length > 0) targets.Add(channel);
            }
            foreach (var sources in groups.Values)
                sources.Sort((a, b) => world.GetMechanismNumber(a).CompareTo(world.GetMechanismNumber(b)));
            foreach (var view in views)
            {
                if (view == null || !BlockLinkRules.IsLinkable(view.Profile.Kind)) continue;
                string channel = BlockLinkRules.ResolveChannel(world.GetLinkId(view.Cell, view.Profile.id), view.Profile, !world.HasLinkIds);
                if (view.Profile.Kind == GameplayBlockKind.Door)
                {
                    var colors = new List<Color>();
                    if (groups.TryGetValue(channel, out var sources))
                        foreach (var cell in sources) colors.Add(SourceColor(world, cell, overrides));
                    if (colors.Count == 0 && world.TryGetMechanismColor(view.Cell, view.Profile.id, out var doorColor)) colors.Add(doorColor);
                    view.SetLinkColors(colors.ToArray());
                }
                else if (view.Profile.Kind != GameplayBlockKind.BeamReceiver)
                    view.SetLinkColors(!targets.Contains(channel) && !world.TryGetMechanismColor(view.Cell, out _) ? new Color[0] :
                        new[] { SourceColor(world, view.Cell, overrides) });
            }
        }

        static Color SourceColor(BlockWorld world, Vector3Int cell, IReadOnlyDictionary<int, Color> overrides)
        {
            int number = world.GetMechanismNumber(cell);
            if (overrides != null && overrides.TryGetValue(number, out var color)) return color;
            if (world.TryGetMechanismColor(cell, out var savedColor)) return savedColor;
            return BlockLinkRules.ColorForSource(number);
        }
    }
}
