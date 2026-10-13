using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 方块世界的数据层。只存有方块的位置（稀疏字典），值是指向 BlockLibrary.All 的索引。
    /// 连续的同种方块按 chunk 归组 —— 每个 chunk 生成一个 GameObject/Mesh，所以"同种连续方块"
    /// 既可以看成一片（同一个 chunk 网格），也可以看成分块（多个 chunk 对象），两种理解都成立。
    /// </summary>
    public partial class BlockWorld : MonoBehaviour, ISerializationCallbackReceiver
    {
        [Tooltip("chunk 边长（方块数）。16 = 一个 Mesh 最多覆盖 16³ 格")]
        public int chunkSize = 16;

        [Tooltip("世界尺寸上限（格）。超出范围的放置会被忽略")]
        public Vector3Int maxSize = new Vector3Int(256, 36, 256);

        public bool InBounds(Vector3Int p) =>
            p.x >= 0 && p.y >= 0 && p.z >= 0 &&
            p.x < maxSize.x && p.y < maxSize.y && p.z < maxSize.z;

        readonly Dictionary<Vector3Int, int> _blocks = new Dictionary<Vector3Int, int>();
        readonly Dictionary<Vector3Int, string> _linkIds = new Dictionary<Vector3Int, string>();
        readonly HashSet<Vector3Int> _dirty = new HashSet<Vector3Int>();

        /// <summary>
        /// 场景持久化用的镜像。Unity 不能序列化 Dictionary，所以方块数据必须另存一份可序列化副本，
        /// 否则烘好的场景一重新加载就变成"空世界"——起点/终点、特殊模型、光源会全部找不到。
        /// 存的是方块 **id**（不是索引），这样方块库以后新增方块也不会错位。
        /// </summary>
        [SerializeField, HideInInspector] List<SaveEntry> _serializedBlocks = new List<SaveEntry>();

        void Awake() { RestoreIfNeeded(); }

        public void OnBeforeSerialize()
        {
            // ★ 编辑器里没有运行时数据（非 ExecuteInEditMode 的脚本在编辑模式下不跑 Awake），
            //   这时绝对不能清掉已持久化的镜像 —— 否则任何一次场景序列化
            //   （进 Play、Ctrl+S、脚本域重载）都会把场景里的方块数据抹掉，
            //   表现就是"第一次能跑，第二次报没有起点/终点"。
            if (_blocks.Count == 0) return;

            EnsureMechanismNumbers();
            _serializedBlocks.Clear();
            foreach (var kv in _blocks)
            {
                var def = BlockLibrary.Get(kv.Value);
                if (def == null) continue;
                _serializedBlocks.Add(new SaveEntry
                {
                    x = kv.Key.x,
                    y = kv.Key.y,
                    z = kv.Key.z,
                    r = GetDir(kv.Key),
                    linkId = GetLinkId(kv.Key),
                    mechanismNumber = GetMechanismNumber(kv.Key),
                    mechanismColor = GetMechanismColorHex(kv.Key),
                    id = def.id
                });
            }
        }

        public void OnAfterDeserialize()
        {
            // 反序列化可能发生在非主线程，这里绝对不能碰 Resources / BlockLibrary。
            // 真正的恢复推迟到 RestoreIfNeeded()（Awake、编辑器工具、游戏根都会调它）。
        }

        /// <summary>把序列化镜像恢复成运行时数据。幂等，可以随便调。</summary>
        public void RestoreIfNeeded()
        {
            if (_blocks.Count > 0) return;
            if (_serializedBlocks == null || _serializedBlocks.Count == 0) return;

            _blocks.Clear();
            _dir.Clear();
            _linkIds.Clear();
            _mechanismNumbers.Clear();
            _mechanismColors.Clear();

            int restored = 0;
            for (int i = 0; i < _serializedBlocks.Count; i++)
            {
                SaveEntry entry = _serializedBlocks[i];
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

                int index = BlockLibrary.IndexOf(entry.id);
                if (index < 0)
                {
                    Debug.LogWarning($"[BlockMap] 场景里的方块「{entry.id}」在方块库里找不到，已跳过");
                    continue;
                }

                var pos = new Vector3Int(entry.x, entry.y, entry.z);
                _blocks[pos] = index;
                if (entry.r != 0) SetDir(pos, entry.r);
                SetLinkIdRaw(pos, entry.linkId);
                SetMechanismNumberRaw(pos, entry.mechanismNumber);
                SetMechanismColorRaw(pos, entry.mechanismColor);
                restored++;
            }

            EnsureMechanismNumbers();
            Debug.Log($"[BlockMap] 已从场景恢复 {restored} 个方块（含朝向）", this);
            foreach (var c in AllChunks()) MarkDirty(c);
            MarkDirty(Vector3Int.zero);
        }

        /// <summary>某 chunk 变脏（需要重建网格）时触发。</summary>
        public event Action<Vector3Int> ChunkDirty;

        /// <summary>方块、朝向或运行时位置发生变化时触发，玩法层据此刷新机关与光线。</summary>
        public event Action Changed;

        public int Version { get; private set; }

        public int Count => _blocks.Count;
        public bool HasLinkIds => _linkIds.Count > 0;

        public static Vector3Int ChunkOf(Vector3Int p, int size) => new Vector3Int(
            FloorDiv(p.x, size), FloorDiv(p.y, size), FloorDiv(p.z, size));

        public static int FloorDiv(int a, int b) => (a >= 0) ? a / b : -((-a + b - 1) / b);

        public int GetIndex(Vector3Int pos) => _blocks.TryGetValue(pos, out var v) ? v : -1;

        public bool IsSolid(Vector3Int pos) => _blocks.ContainsKey(pos);

        public bool IsSame(Vector3Int pos, int index) => index >= 0 && GetIndex(pos) == index;

        public IEnumerable<KeyValuePair<Vector3Int, int>> Blocks => _blocks;

        public string GetLinkId(Vector3Int pos) =>
            _linkIds.TryGetValue(pos, out string linkId) ? linkId : string.Empty;

        /// <summary>设置单个机关实例的关联组。空字符串表示解除关联。</summary>
        public bool SetLinkId(Vector3Int pos, string linkId)
        {
            if (!_blocks.ContainsKey(pos)) return false;

            string normalized = NormalizeLinkId(linkId);
            string before = GetLinkId(pos);
            if (string.Equals(before, normalized, StringComparison.Ordinal)) return false;

            int index = GetIndex(pos);
            RecordEdit(pos, index, index, before, normalized);
            SetLinkIdRaw(pos, normalized);
            NotifyChanged();
            return true;
        }

        /// <summary>放置 / 替换方块，并把受影响的 chunk 标脏。</summary>
        // ---------------- 朝向（0=+Z 前 / 1=+X 右 / 2=-Z 后 / 3=-X 左，顺时针）----------------

        readonly Dictionary<Vector3Int, byte> _dir = new Dictionary<Vector3Int, byte>();

        /// <summary>取某格的朝向（0~3）。没记录时返回 0。</summary>
        public int GetDir(Vector3Int pos) => _dir.TryGetValue(pos, out var d) ? d : 0;

        /// <summary>设置某格朝向（0~3）。0 会删掉记录，保持存档干净。</summary>
        public void SetDir(Vector3Int pos, int dir)
        {
            if (!InBounds(pos)) return;
            int normalized = dir & 3;
            int before = GetDir(pos);
            if (before == normalized) return;
            if (normalized == 0) _dir.Remove(pos);
            else _dir[pos] = (byte)normalized;
            NotifyChanged();
        }

        public void Set(Vector3Int pos, int index)
        {
            if (index < 0) { Remove(pos); return; }
            if (!InBounds(pos)) return;                        // 超出 256x36x256 直接忽略
            int before = GetIndex(pos);
            if (before == index) return;
            string beforeLink = GetLinkId(pos);
            int number = CreateMechanismNumber(index);
            RecordEdit(pos, before, index, beforeLink, string.Empty, number);
            _blocks[pos] = index;
            _linkIds.Remove(pos);
            _mechanismColors.Remove(pos);
            SetMechanismNumberRaw(pos, number);
            MarkNeighborhoodDirty(pos);
            NotifyChanged();
        }

        public void Remove(Vector3Int pos)
        {
            if (!InBounds(pos)) return;
            int before = GetIndex(pos);
            if (before < 0) return;
            RecordEdit(pos, before, -1, GetLinkId(pos), string.Empty, 0);
            _blocks.Remove(pos);
            _dir.Remove(pos);                                  // 删方块时一起清掉朝向
            _linkIds.Remove(pos);
            _mechanismNumbers.Remove(pos);
            _mechanismColors.Remove(pos);
            MarkNeighborhoodDirty(pos);
            NotifyChanged();
        }

        /// <summary>移动一个方块并保留朝向。用于可推动机关；目标格必须为空。</summary>
        public bool Move(Vector3Int from, Vector3Int to)
        {
            if (!InBounds(from) || !InBounds(to) || from == to) return false;
            if (!_blocks.TryGetValue(from, out int index) || _blocks.ContainsKey(to)) return false;

            int dir = GetDir(from);
            string linkId = GetLinkId(from);
            int number = GetMechanismNumber(from);
            string color = GetMechanismColorHex(from);
            _blocks.Remove(from);
            _dir.Remove(from);
            _linkIds.Remove(from);
            _mechanismNumbers.Remove(from);
            _mechanismColors.Remove(from);
            _blocks[to] = index;
            if (dir != 0) _dir[to] = (byte)dir;
            SetLinkIdRaw(to, linkId);
            SetMechanismNumberRaw(to, number);
            SetMechanismColorRaw(to, color);
            MarkNeighborhoodDirty(from);
            MarkNeighborhoodDirty(to);
            NotifyChanged();
            return true;
        }

        public void Clear()
        {
            foreach (var chunk in AllChunks()) MarkDirty(chunk);
            _dir.Clear();
            _linkIds.Clear();
            _blocks.Clear();
            _mechanismNumbers.Clear();
            _nextMechanismNumber = 1;
            _mechanismColors.Clear();
            _undo.Clear();
            _redo.Clear();
            _serializedBlocks.Clear();      // 显式清空 = 持久化数据一起清（FromJson 紧接着会重新填上）
            NotifyChanged();
        }

        // ---------------- 撤销 / 重做（以"一次按下-松开"为一个笔画） ----------------

        public struct Edit
        {
            public Vector3Int pos;
            public int before;
            public int after;
            public string beforeLink;
            public string afterLink;
            public int beforeNumber;
            public int afterNumber;
            public string beforeColor;
            public string afterColor;
        }

        readonly List<List<Edit>> _undo = new List<List<Edit>>();
        readonly List<List<Edit>> _redo = new List<List<Edit>>();
        List<Edit> _pendingStroke;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>鼠标按下时调用，开始记录一个笔画。</summary>
        public void BeginStroke() => _pendingStroke = new List<Edit>();

        /// <summary>鼠标松开时调用，把这一笔画压入撤销栈（没改动就不压）。</summary>
        public void EndStroke()
        {
            if (_pendingStroke == null) return;
            if (_pendingStroke.Count > 0)
            {
                _undo.Add(_pendingStroke);
                _redo.Clear();
                if (_undo.Count > 200) _undo.RemoveAt(0);      // 栈上限，防止内存无限涨
            }
            _pendingStroke = null;
        }

        void RecordEdit(Vector3Int pos, int before, int after, string beforeLink, string afterLink, int afterNumber = -1, string afterColor = null)
        {
            if (_pendingStroke == null) return;                 // 不在笔画里（读档/程序生成）就不记录
            _pendingStroke.Add(new Edit
            {
                pos = pos,
                before = before,
                after = after,
                beforeLink = beforeLink,
                afterLink = afterLink,
                beforeNumber = GetMechanismNumber(pos),
                afterNumber = afterNumber < 0 ? GetMechanismNumber(pos) : afterNumber,
                beforeColor = GetMechanismColorHex(pos),
                afterColor = afterColor ?? (before == after ? GetMechanismColorHex(pos) : string.Empty)
            });
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var stroke = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            for (int i = stroke.Count - 1; i >= 0; i--)
                ApplyRaw(stroke[i].pos, stroke[i].before, stroke[i].beforeLink, stroke[i].beforeNumber, stroke[i].beforeColor);
            _redo.Add(stroke);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var stroke = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            foreach (var e in stroke) ApplyRaw(e.pos, e.after, e.afterLink, e.afterNumber, e.afterColor);
            _undo.Add(stroke);
        }

        void ApplyRaw(Vector3Int pos, int index, string linkId, int mechanismNumber, string color)
        {
            if (index < 0)
            {
                _blocks.Remove(pos);
                _dir.Remove(pos);
                _linkIds.Remove(pos);
                _mechanismNumbers.Remove(pos);
                _mechanismColors.Remove(pos);
            }
            else
            {
                _blocks[pos] = index;
                SetLinkIdRaw(pos, linkId);
                SetMechanismNumberRaw(pos, mechanismNumber);
                SetMechanismColorRaw(pos, color);
            }
            MarkNeighborhoodDirty(pos);                          // 脏 chunk 由渲染器下一帧重建
            NotifyChanged();
        }

        public IEnumerable<KeyValuePair<Vector3Int, int>> BlocksInChunk(Vector3Int chunk)
        {
            foreach (var kv in _blocks)
                if (ChunkOf(kv.Key, chunkSize) == chunk) yield return kv;
        }

        public IEnumerable<Vector3Int> AllChunks()
        {
            var seen = new HashSet<Vector3Int>();
            foreach (var kv in _blocks)
            {
                var c = ChunkOf(kv.Key, chunkSize);
                if (seen.Add(c)) yield return c;
            }
        }

        /// <summary>改动一格会影响本 chunk 与相邻 chunk（因为贴图包边要看邻居）。</summary>
        void MarkNeighborhoodDirty(Vector3Int pos)
        {
            var c = ChunkOf(pos, chunkSize);
            MarkDirty(c);
            // 只在与 chunk 边界相邻时才影响邻 chunk，这里图省事把 3x3x3 都标上
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        MarkDirty(c + new Vector3Int(dx, dy, dz));
        }

        public void MarkDirty(Vector3Int chunk)
        {
            if (_dirty.Add(chunk)) ChunkDirty?.Invoke(chunk);
        }

        /// <summary>取出并清空脏 chunk 列表（渲染器每帧调用）。</summary>
        public void ConsumeDirty(List<Vector3Int> buffer)
        {
            buffer.Clear();
            foreach (var c in _dirty) buffer.Add(c);
            _dirty.Clear();
        }

        // ---------------- 存档 ----------------

        [Serializable] public class SaveEntry
        {
            public int x, y, z, r;
            public string id;
            public string linkId;
            public int mechanismNumber;
            public string mechanismColor;
        }
        [Serializable] public class SaveData
        {
            public int chunkSize = 16;
            public int nextMechanismNumber = 1;
            public List<SaveEntry> blocks = new List<SaveEntry>();
        }

        public string ToJson()
        {
            EnsureMechanismNumbers();
            var data = new SaveData { chunkSize = chunkSize, nextMechanismNumber = _nextMechanismNumber };
            foreach (var kv in _blocks)
            {
                var def = BlockLibrary.Get(kv.Value);
                if (def == null) continue;
                data.blocks.Add(new SaveEntry
                {
                    x = kv.Key.x,
                    y = kv.Key.y,
                    z = kv.Key.z,
                    r = GetDir(kv.Key),
                    id = def.id,
                    linkId = GetLinkId(kv.Key),
                    mechanismNumber = GetMechanismNumber(kv.Key),
                    mechanismColor = GetMechanismColorHex(kv.Key)
                });
            }
            return JsonUtility.ToJson(data, true);
        }

        public void FromJson(string json)
        {
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data == null) return;
            if (data.blocks == null) data.blocks = new List<SaveEntry>();
            Clear();
            _blocks.Clear();
            _linkIds.Clear();
            if (data.chunkSize > 0) chunkSize = data.chunkSize;
            _nextMechanismNumber = Mathf.Max(1, data.nextMechanismNumber);

            // 同步持久化镜像：烘好的场景保存时会把这份数据一起写进场景
            _serializedBlocks.Clear();
            _serializedBlocks.AddRange(data.blocks);

            foreach (var e in data.blocks)
            {
                int idx = BlockLibrary.IndexOf(e.id);
                if (idx < 0)
                {
                    Debug.LogWarning($"[BlockMap] 存档里的方块 id 找不到：{e.id}，已跳过");
                    continue;
                }
                var p = new Vector3Int(e.x, e.y, e.z);
                _blocks[p] = idx;
                if (e.r != 0) SetDir(p, e.r);          // 老存档没有 r → 0，自动兼容
                SetLinkIdRaw(p, e.linkId);             // 老存档没有 linkId → 空，自动兼容
                SetMechanismNumberRaw(p, e.mechanismNumber);
                SetMechanismColorRaw(p, e.mechanismColor);
            }
            EnsureMechanismNumbers();
            foreach (var c in AllChunks()) MarkDirty(c);
            MarkDirty(Vector3Int.zero);
            NotifyChanged();
        }

        void NotifyChanged()
        {
            Version++;
            Changed?.Invoke();
        }

        static string NormalizeLinkId(string linkId) =>
            string.IsNullOrWhiteSpace(linkId) ? string.Empty : linkId.Trim();

        void SetLinkIdRaw(Vector3Int pos, string linkId)
        {
            string normalized = NormalizeLinkId(linkId);
            if (normalized.Length == 0) _linkIds.Remove(pos);
            else _linkIds[pos] = normalized;
        }
    }
}
