using System.Collections.Generic;

namespace BlockMap.Gameplay
{
    /// <summary>记录各机关当前信号，并保存每个关联频道的切换状态。</summary>
    public sealed class BlockSignalBus
    {
        readonly Dictionary<string, Dictionary<string, bool>> _channels =
            new Dictionary<string, Dictionary<string, bool>>();
        readonly Dictionary<string, bool> _toggleStates = new Dictionary<string, bool>();

        public bool Set(string channel, string source, bool active)
        {
            bool changed = SetContinuous(channel, source, active);
            return active && changed;
        }

        /// <summary>持续机关在接通和断开时都返回一次变化；保持状态时返回 false。</summary>
        public bool SetContinuous(string channel, string source, bool active)
        {
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(source)) return false;
            if (!_channels.TryGetValue(channel, out Dictionary<string, bool> sources))
            {
                sources = new Dictionary<string, bool>();
                _channels.Add(channel, sources);
            }
            bool wasActive = sources.TryGetValue(source, out bool previous) && previous;
            sources[source] = active;
            return active != wasActive;
        }

        public bool IsActive(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel) || !_channels.TryGetValue(channel, out Dictionary<string, bool> sources))
                return false;
            foreach (bool active in sources.Values)
                if (active) return true;
            return false;
        }

        public void Toggle(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return;
            _toggleStates[channel] = !IsToggled(channel);
        }

        public void SetToggled(string channel, bool toggled)
        {
            if (string.IsNullOrWhiteSpace(channel)) return;
            _toggleStates[channel] = toggled;
        }

        public bool IsToggled(string channel) =>
            !string.IsNullOrWhiteSpace(channel) && _toggleStates.TryGetValue(channel, out bool toggled) && toggled;

        public void Clear()
        {
            _channels.Clear();
            _toggleStates.Clear();
        }
    }
}
