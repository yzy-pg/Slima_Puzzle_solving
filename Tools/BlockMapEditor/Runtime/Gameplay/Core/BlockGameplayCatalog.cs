using System.Collections.Generic;
using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>读取方块 ID 到玩法职责的全局映射。地图只保存 ID，因此旧地图格式保持兼容。</summary>
    public static class BlockGameplayCatalog
    {
        const string ResourcePath = "BlockMap/GameplayCatalog";
        static Dictionary<string, GameplayBlockProfile> _byId;
        static List<GameplayBlockProfile> _profiles;

        public static IReadOnlyList<GameplayBlockProfile> All
        {
            get { EnsureLoaded(); return _profiles; }
        }

        public static GameplayBlockProfile Get(string id)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out GameplayBlockProfile profile)
                ? profile
                : null;
        }

        public static void Reload()
        {
            _byId = null;
            _profiles = null;
            EnsureLoaded();
        }

        public static void AddDefinitions(List<BlockDefinition> definitions, Dictionary<string, BlockDefinition> byId)
        {
            EnsureLoaded();
            foreach (GameplayBlockProfile profile in _profiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.id)) continue;

                if (byId.TryGetValue(profile.id, out BlockDefinition existing))
                {
                    ApplyProfile(existing, profile);
                    continue;
                }

                var definition = ScriptableObject.CreateInstance<BlockDefinition>();
                definition.id = profile.id;
                definition.displayName = string.IsNullOrWhiteSpace(profile.displayName) ? profile.id : profile.displayName;
                definition.simpleTexture = CreatePlaceholderTexture(profile.id, profile.Tint);
                ApplyProfile(definition, profile);
                definitions.Add(definition);
                byId.Add(definition.id, definition);
            }
        }

        static void ApplyProfile(BlockDefinition definition, GameplayBlockProfile profile)
        {
            definition.interactive = profile.interactive;
            definition.tint = profile.Tint;
            if (!string.IsNullOrWhiteSpace(profile.displayName)) definition.displayName = profile.displayName;
        }

        static void EnsureLoaded()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, GameplayBlockProfile>();
            _profiles = new List<GameplayBlockProfile>();

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[BlockMap] 未找到玩法方块配置 Resources/{ResourcePath}.json");
                return;
            }

            GameplayBlockCatalogData data = JsonUtility.FromJson<GameplayBlockCatalogData>(asset.text);
            if (data?.blocks == null) return;

            foreach (GameplayBlockProfile profile in data.blocks)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.id)) continue;
                if (_byId.ContainsKey(profile.id))
                {
                    Debug.LogWarning($"[BlockMap] GameplayCatalog 存在重复 id：{profile.id}");
                    continue;
                }
                _profiles.Add(profile);
                _byId.Add(profile.id, profile);
            }
        }

        static Texture2D CreatePlaceholderTexture(string id, Color color)
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                name = id + "_gameplay_placeholder",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[16 * 16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
