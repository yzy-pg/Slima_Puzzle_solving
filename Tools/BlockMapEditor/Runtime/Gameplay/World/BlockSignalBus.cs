using System.Collections.Generic;

namespace BlockMap.Gameplay
{
    /// <summary>按频道汇总机关信号；多个来源采用“任意一个开启即开启”。</summary>
    public sealed class BlockSignalBus
    {
        readonly Dictionary<string, Dictionary<string, bool>> _channels =
            new Dictionary<string, Dictionary<string, bool>>();

        public void Set(string channel, string source, bool active)
        {
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(source)) return;
            if (!_channels.TryGetValue(channel, out Dictionary<string, bool> sources))
            {
                sources = new Dictionary<string, bool>();
                _channels.Add(channel, sources);
            }
            sources[source] = active;
        }

        public bool IsActive(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel) || !_channels.TryGetValue(channel, out Dictionary<string, bool> sources))
                return false;
            foreach (bool active in sources.Values)
                if (active) return true;
            return false;
        }

        public void Clear() => _channels.Clear();
    }
}
