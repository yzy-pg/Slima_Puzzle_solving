using System;
using UnityEngine;

namespace SlimaPrototype
{
    [Serializable]
    public sealed class SlimaGelConfig
    {
        public string _说明 = "史莱姆凝胶、移动和环境伤害参数。";
        public int initialGel = 100;
        public int maximumGel = 100;
        public int moveCostPerCell = 1;
        public int environmentDamage = 2;
        public int splitMinimumGel = 40;
    }

    [Serializable]
    public sealed class SlimaItemConfig
    {
        public string _说明 = "消耗道具参数，单位均为凝胶点数或移动步数。";
        public int potionRestore = 3;
        public int pillDurationSteps = 5;
        public int pillGelPerStep = 2;
    }

    [Serializable]
    public sealed class SlimaMechanismConfig
    {
        public string _说明 = "机关和分裂装置参数，距离单位为格。";
        public int buttonDurationSteps = 3;
        public int transferAmount = 10;
        public int catapultDistanceCells = 3;
        public float oneColorPressureMultiplier = 1f;
        public float twoColorPressureMultiplier = 1.5f;
        public float threeColorPressureMultiplier = 2f;
    }

    [Serializable]
    public sealed class SlimaSystemConfig
    {
        public string _说明 = "全局系统限制。";
        public int maximumSlimes = 2;
        public bool capGelAtMaximum = true;
        public bool anySlimeZeroFails = true;
        public int undoHistoryLimit = 100;
    }

    [Serializable]
    public sealed class SlimaGlobalGameplayConfig
    {
        public int version = 1;
        public string _说明 = "史莱姆解谜全局配置。关卡覆盖值为 -1 时使用这里的数值。";
        public SlimaGelConfig gel = new SlimaGelConfig();
        public SlimaItemConfig items = new SlimaItemConfig();
        public SlimaMechanismConfig mechanisms = new SlimaMechanismConfig();
        public SlimaSystemConfig system = new SlimaSystemConfig();

        public static SlimaGlobalGameplayConfig CreateDefault() => new SlimaGlobalGameplayConfig();
    }

    public sealed class SlimaResolvedRules
    {
        public int initialGel;
        public int maxGel;
        public int baseMoveCost;
        public int environmentDamage;
        public int splitThreshold;
        public int buttonDurationSteps;
        public int transferAmount;
        public int maxSlimes;
        public bool capGelAtMax;
        public bool anySlimeZeroFails;
        public int potionRestore;
        public int pillDurationSteps;
        public int pillGelPerStep;
        public int catapultDistanceCells;
        public float oneColorPressureMultiplier;
        public float twoColorPressureMultiplier;
        public float threeColorPressureMultiplier;
        public int undoHistoryLimit;

        public static SlimaResolvedRules Resolve(SlimaGlobalGameplayConfig global, SlimaLevelRules overrides)
        {
            global = global ?? SlimaGlobalGameplayConfig.CreateDefault();
            overrides = overrides ?? new SlimaLevelRules();
            return new SlimaResolvedRules
            {
                initialGel = Pick(overrides.initialGel, global.gel.initialGel),
                maxGel = Pick(overrides.maxGel, global.gel.maximumGel),
                baseMoveCost = Pick(overrides.baseMoveCost, global.gel.moveCostPerCell),
                environmentDamage = Pick(overrides.environmentDamage, global.gel.environmentDamage),
                splitThreshold = Pick(overrides.splitThreshold, global.gel.splitMinimumGel),
                buttonDurationSteps = Pick(overrides.buttonDurationSteps, global.mechanisms.buttonDurationSteps),
                transferAmount = Pick(overrides.transferAmount, global.mechanisms.transferAmount),
                maxSlimes = Pick(overrides.maxSlimes, global.system.maximumSlimes),
                capGelAtMax = PickBool(overrides.capGelAtMax, global.system.capGelAtMaximum),
                anySlimeZeroFails = PickBool(overrides.anySlimeZeroFails, global.system.anySlimeZeroFails),
                potionRestore = Mathf.Max(0, global.items.potionRestore),
                pillDurationSteps = Mathf.Max(0, global.items.pillDurationSteps),
                pillGelPerStep = global.items.pillGelPerStep,
                catapultDistanceCells = Mathf.Max(0, global.mechanisms.catapultDistanceCells),
                oneColorPressureMultiplier = Mathf.Max(0f, global.mechanisms.oneColorPressureMultiplier),
                twoColorPressureMultiplier = Mathf.Max(0f, global.mechanisms.twoColorPressureMultiplier),
                threeColorPressureMultiplier = Mathf.Max(0f, global.mechanisms.threeColorPressureMultiplier),
                undoHistoryLimit = Mathf.Max(1, global.system.undoHistoryLimit)
            };
        }

        private static int Pick(int overrideValue, int globalValue) => overrideValue >= 0 ? overrideValue : globalValue;
        private static bool PickBool(int overrideValue, bool globalValue) => overrideValue < 0 ? globalValue : overrideValue != 0;
    }

    public static class SlimaGlobalConfigJson
    {
        public static SlimaGlobalGameplayConfig FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("全局配置 JSON 为空", nameof(json));
            SlimaGlobalGameplayConfig config = JsonUtility.FromJson<SlimaGlobalGameplayConfig>(json);
            if (config == null) throw new InvalidOperationException("无法解析全局配置 JSON");
            if (config.gel == null) config.gel = new SlimaGelConfig();
            if (config.items == null) config.items = new SlimaItemConfig();
            if (config.mechanisms == null) config.mechanisms = new SlimaMechanismConfig();
            if (config.system == null) config.system = new SlimaSystemConfig();
            return config;
        }

        public static SlimaGlobalGameplayConfig FromTextAsset(TextAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            return FromJson(asset.text);
        }
    }
}
