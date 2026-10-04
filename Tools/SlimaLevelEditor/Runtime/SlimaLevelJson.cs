using System;
using UnityEngine;

namespace SlimaPrototype
{
    public static class SlimaLevelJson
    {
        public static string ToJson(SlimaLevelData level, bool pretty = true)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            return JsonUtility.ToJson(level, pretty);
        }

        public static SlimaLevelData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("关卡 JSON 为空", nameof(json));
            SlimaLevelData level = JsonUtility.FromJson<SlimaLevelData>(json);
            if (level == null) throw new InvalidOperationException("无法解析关卡 JSON");
            if (level.rules == null) level.rules = new SlimaLevelRules();
            if (level.cells == null) level.cells = new System.Collections.Generic.List<SlimaCellData>();
            if (level.entities == null) level.entities = new System.Collections.Generic.List<SlimaEntityData>();
            return level;
        }

        public static SlimaLevelData FromTextAsset(TextAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            return FromJson(asset.text);
        }
    }
}
