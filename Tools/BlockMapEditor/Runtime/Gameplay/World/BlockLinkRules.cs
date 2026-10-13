using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>机关关联的领域规则。地图实例配置优先，目录中的 channel 仅兼容旧地图。</summary>
    public static class BlockLinkRules
    {
        static readonly Color[] GroupColors =
        {
            new Color(0.20f, 0.80f, 1.00f),
            new Color(1.00f, 0.55f, 0.20f),
            new Color(0.45f, 0.90f, 0.35f),
            new Color(0.85f, 0.45f, 1.00f),
            new Color(1.00f, 0.85f, 0.20f),
            new Color(1.00f, 0.35f, 0.50f)
        };

        public static bool IsSignalSource(GameplayBlockKind kind) =>
            kind == GameplayBlockKind.PressurePlate ||
            kind == GameplayBlockKind.Button ||
            kind == GameplayBlockKind.Lever ||
            kind == GameplayBlockKind.BeamReceiver;

        public static bool IsSignalTarget(GameplayBlockKind kind) => kind == GameplayBlockKind.Door;

        public static bool IsLinkable(GameplayBlockKind kind) => IsSignalSource(kind) || IsSignalTarget(kind);

        public static string ResolveChannel(
            string instanceLinkId,
            GameplayBlockProfile profile,
            bool allowCatalogFallback = true)
        {
            if (!string.IsNullOrWhiteSpace(instanceLinkId)) return instanceLinkId.Trim();
            return allowCatalogFallback && profile != null && !string.IsNullOrWhiteSpace(profile.channel)
                ? profile.channel.Trim()
                : string.Empty;
        }

        public static Color ColorFor(string linkId)
        {
            if (string.IsNullOrWhiteSpace(linkId)) return Color.gray;
            string normalized = linkId.Trim();
            uint hash = 2166136261;
            for (int index = 0; index < normalized.Length; index++)
                hash = (hash ^ normalized[index]) * 16777619;
            return GroupColors[hash % GroupColors.Length];
        }

        public static Color ColorForSource(int number) =>
            GroupColors[Mathf.Max(0, number - 1) % GroupColors.Length];
    }
}
