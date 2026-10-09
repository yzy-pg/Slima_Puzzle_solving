using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockMap.Gameplay
{
    public readonly struct GameplayBlockCell
    {
        public readonly Vector3Int Cell;
        public readonly BlockDefinition Definition;
        public readonly GameplayBlockProfile Profile;
        public readonly string LinkId;
        readonly bool _allowCatalogFallback;

        public GameplayBlockCell(
            Vector3Int cell,
            BlockDefinition definition,
            GameplayBlockProfile profile,
            string linkId,
            bool allowCatalogFallback)
        {
            Cell = cell;
            Definition = definition;
            Profile = profile;
            LinkId = linkId;
            _allowCatalogFallback = allowCatalogFallback;
        }

        public string SignalChannel =>
            BlockLinkRules.ResolveChannel(LinkId, Profile, _allowCatalogFallback);
    }

    /// <summary>
    /// 关卡机关协调器：集中处理通行、地板效果、信号和机关状态。
    /// 它不处理输入、不负责角色表现，也不包含光线追踪实现。
    /// </summary>
    public sealed class BlockGameplayController : MonoBehaviour
    {
        sealed class RuntimeState
        {
            public bool toggled;
            public bool latched;
            public bool active;
            public GameplayBlockView view;
            public int blockIndex;
            public int direction;
        }

        readonly Dictionary<Vector3Int, RuntimeState> _states = new Dictionary<Vector3Int, RuntimeState>();
        readonly BlockSignalBus _signals = new BlockSignalBus();
        readonly HashSet<Vector3Int> _activeReceivers = new HashSet<Vector3Int>();

        BlockWorld _world;
        SlimePartyController _party;
        BlockOpticsSystem _optics;
        Transform _viewRoot;
        bool _worldDirty;
        Vector2Int _minXZ;
        Vector2Int _maxXZ;

        public event Action Changed;

        public void Initialize(BlockWorld world)
        {
            if (_world != null) _world.Changed -= OnWorldChanged;
            _world = world;
            _world.Changed += OnWorldChanged;

            _optics = GetComponent<BlockOpticsSystem>();
            if (_optics == null) _optics = gameObject.AddComponent<BlockOpticsSystem>();
            _optics.Initialize(this);
            RebuildViews(false);
        }

        public void BindParty(SlimePartyController party)
        {
            if (_party != null) _party.Changed -= OnPartyChanged;
            _party = party;
            if (_party != null) _party.Changed += OnPartyChanged;
            _optics?.MarkDirty();
        }

        public void ResetState()
        {
            _signals.Clear();
            _activeReceivers.Clear();
            RebuildViews(false);
            _optics?.MarkDirty();
        }

        void OnDestroy()
        {
            if (_world != null) _world.Changed -= OnWorldChanged;
            if (_party != null) _party.Changed -= OnPartyChanged;
        }

        void Update()
        {
            if (_worldDirty) RebuildViews(true);
            _optics?.RefreshIfNeeded();
            EvaluateSignals();
        }

        void OnWorldChanged()
        {
            _worldDirty = true;
            _optics?.MarkDirty();
            Changed?.Invoke();
        }

        void OnPartyChanged()
        {
            _optics?.MarkDirty();
            Changed?.Invoke();
        }

        public GameplayBlockProfile GetProfileAt(Vector3Int cell)
        {
            BlockDefinition definition = GetDefinitionAt(cell);
            return definition != null ? BlockGameplayCatalog.Get(definition.id) : null;
        }

        public BlockDefinition GetDefinitionAt(Vector3Int cell)
        {
            if (_world == null) return null;
            return BlockLibrary.Get(_world.GetIndex(cell));
        }

        public int GetDirection(Vector3Int cell) => _world != null ? _world.GetDir(cell) : 0;

        public IEnumerable<GameplayBlockCell> Enumerate(GameplayBlockKind kind)
        {
            if (_world == null) yield break;
            foreach (KeyValuePair<Vector3Int, int> item in _world.Blocks)
            {
                BlockDefinition definition = BlockLibrary.Get(item.Value);
                GameplayBlockProfile profile = definition != null ? BlockGameplayCatalog.Get(definition.id) : null;
                if (profile != null && profile.Kind == kind)
                    yield return new GameplayBlockCell(
                        item.Key,
                        definition,
                        profile,
                        _world.GetLinkId(item.Key),
                        !_world.HasLinkIds);
            }
        }

        public bool CanEnter(SlimeActor actor, Vector3Int cell, int direction, bool allowPush)
        {
            if (actor == null || !WithinLevel(cell)) return false;

            if (actor.State.form == SlimeForm.Gas)
            {
                // 气态位于普通史莱姆上一层：忽略脚下机关和一格高障碍，只检查头顶层。
                return !BlocksMovement(cell + Vector3Int.up, false);
            }

            GameplayBlockProfile profile = GetProfileAt(cell);
            if (profile != null && profile.blocksMovement)
            {
                if (profile.Kind == GameplayBlockKind.Door && IsActivated(cell))
                {
                    // 开门后可通过。
                }
                else if (allowPush && profile.pushable && TryPush(cell, direction))
                {
                    // 推动物件后原格变为空格。
                }
                else return false;
            }
            else if (profile == null && BlocksMovement(cell, true))
            {
                return false;
            }

            return HasSupport(cell + Vector3Int.down);
        }

        public bool CanSolidify(SlimeActor actor)
        {
            if (actor == null) return false;
            Vector3Int cell = actor.Mover.Cell;
            if (_party != null && _party.HasOtherSolidAt(actor, cell)) return false;
            GameplayBlockProfile profile = GetProfileAt(cell);
            if (profile != null && profile.blocksMovement && !(profile.Kind == GameplayBlockKind.Door && IsActivated(cell)))
                return false;
            if (profile == null && BlocksMovement(cell, true)) return false;
            return HasSupport(cell + Vector3Int.down);
        }

        public bool ApplyPassive(SlimeActor actor)
        {
            if (actor == null || actor.State.form == SlimeForm.Gas) return false;
            GameplayBlockProfile profile = GetProfileAt(actor.Mover.Cell);
            if (profile == null) return false;

            SlimeState next = actor.State.Copy();
            switch (profile.Kind)
            {
                case GameplayBlockKind.RedFloor:
                    if (next.element == SlimeElement.Blue)
                    {
                        next.element = SlimeElement.Neutral;
                        next.form = SlimeForm.Gas;
                    }
                    else next.element = SlimeElement.Red;
                    actor.SetState(next);
                    return false;

                case GameplayBlockKind.BlueFloor:
                    if (next.element == SlimeElement.Red)
                    {
                        next.element = SlimeElement.Neutral;
                        next.form = SlimeForm.Gas;
                    }
                    else next.element = SlimeElement.Blue;
                    actor.SetState(next);
                    return false;

                case GameplayBlockKind.DamageFloor:
                    return actor.Weight > 0;

                default:
                    return false;
            }
        }

        public bool TryGetInteraction(SlimeActor actor, out SlimeInteraction interaction)
        {
            interaction = default;
            if (actor == null || actor.State.form == SlimeForm.Gas || (_party != null && _party.IsBusy)) return false;

            if (_party != null && _party.CanFuseFacing(actor))
            {
                interaction = new SlimeInteraction(SlimeInteractionKind.Fuse, actor.Mover.Cell);
                return true;
            }

            GameplayBlockProfile current = GetProfileAt(actor.Mover.Cell);
            if (current != null && current.Kind == GameplayBlockKind.Splitter && _party != null && _party.CanSplit(actor))
            {
                interaction = new SlimeInteraction(SlimeInteractionKind.Split, actor.Mover.Cell);
                return true;
            }

            int facing = actor.Mover.Facing;
            int[] order = { facing, GridDirection.Left(facing), GridDirection.Right(facing), GridDirection.Back(facing) };
            foreach (int direction in order)
            {
                Vector3Int cell = actor.Mover.Cell + GridDirection.ToVector(direction);
                GameplayBlockProfile profile = GetProfileAt(cell);
                if (profile == null || profile.Kind != GameplayBlockKind.Lever) continue;
                interaction = new SlimeInteraction(SlimeInteractionKind.Lever, cell);
                return true;
            }
            return false;
        }

        public bool TryInteract(SlimeActor actor)
        {
            if (!TryGetInteraction(actor, out SlimeInteraction interaction)) return false;
            switch (interaction.Kind)
            {
                case SlimeInteractionKind.Fuse: return _party.TryFuseFacing(actor);
                case SlimeInteractionKind.Split: return _party.TrySplit(actor);
                default:
                    RuntimeState state = StateAt(interaction.Cell);
                    state.toggled = !state.toggled;
                    _optics?.MarkDirty();
                    Changed?.Invoke();
                    return true;
            }
        }

        public bool TryDestroyFacing(SlimeActor actor)
        {
            if (actor == null || actor.State.form != SlimeForm.Solid || actor.State.element != SlimeElement.Red)
                return false;
            Vector3Int target = actor.Mover.Cell + GridDirection.ToVector(actor.Mover.Facing);
            GameplayBlockProfile profile = GetProfileAt(target);
            if (profile == null || profile.Kind != GameplayBlockKind.Destructible) return false;
            _world.Remove(target);
            return true;
        }

        public bool IsExit(Vector3Int cell)
        {
            BlockDefinition definition = GetDefinitionAt(cell);
            return definition != null && definition.id == "Exit";
        }

        public SlimeActor GetActorAt(Vector3Int cell)
        {
            return _party != null ? _party.FindAt(cell) : null;
        }

        public bool IsBeamBlocked(Vector3Int cell)
        {
            GameplayBlockProfile profile = GetProfileAt(cell);
            if (profile != null)
            {
                if (profile.Kind == GameplayBlockKind.BeamReceiver) return false;
                if (profile.Kind == GameplayBlockKind.Door) return !IsActivated(cell);
                return profile.blocksMovement;
            }
            return BlocksMovement(cell, true);
        }

        public void BeginReceiverEvaluation()
        {
            _activeReceivers.Clear();
        }

        public void ActivateReceiver(Vector3Int cell)
        {
            GameplayBlockProfile profile = GetProfileAt(cell);
            if (profile == null || profile.Kind != GameplayBlockKind.BeamReceiver) return;
            RuntimeState state = StateAt(cell);
            if (profile.IsLatched) state.latched = true;
            _activeReceivers.Add(cell);
        }

        bool TryPush(Vector3Int from, int direction)
        {
            Vector3Int to = from + GridDirection.ToVector(direction);
            if (!WithinLevel(to) || _world.IsSolid(to) || !HasSupport(to + Vector3Int.down)) return false;
            return _world.Move(from, to);
        }

        bool HasSupport(Vector3Int cell)
        {
            if (_world == null || !_world.IsSolid(cell)) return false;
            BlockDefinition definition = GetDefinitionAt(cell);
            if (definition == null) return false;
            GameplayBlockProfile profile = BlockGameplayCatalog.Get(definition.id);
            return profile == null && !definition.interactive;
        }

        bool BlocksMovement(Vector3Int cell, bool unknownInteractiveBlocks)
        {
            if (_world == null || !_world.IsSolid(cell)) return false;
            BlockDefinition definition = GetDefinitionAt(cell);
            if (definition == null || BlockLibrary.IsMarkerBlock(definition.id)) return false;
            GameplayBlockProfile profile = BlockGameplayCatalog.Get(definition.id);
            if (profile != null) return profile.blocksMovement;
            return !definition.interactive || unknownInteractiveBlocks;
        }

        bool WithinLevel(Vector3Int cell)
        {
            return _world != null && _world.InBounds(cell) &&
                   cell.x >= _minXZ.x && cell.x <= _maxXZ.x &&
                   cell.z >= _minXZ.y && cell.z <= _maxXZ.y;
        }

        bool IsActivated(Vector3Int cell) => _states.TryGetValue(cell, out RuntimeState state) && state.active;

        RuntimeState StateAt(Vector3Int cell)
        {
            if (!_states.TryGetValue(cell, out RuntimeState state))
            {
                state = new RuntimeState();
                _states.Add(cell, state);
            }
            return state;
        }

        void EvaluateSignals()
        {
            if (_world == null) return;
            _signals.Clear();

            foreach (GameplayBlockCell block in Enumerate(GameplayBlockKind.PressurePlate))
            {
                int weight = 0;
                if (_party != null)
                {
                    foreach (SlimeActor actor in _party.Actors)
                        if (actor != null && actor.Mover.Cell == block.Cell) weight += actor.Weight;
                }
                int required = Mathf.Max(1, block.Profile.value);
                _signals.Set(block.SignalChannel, SourceKey("plate", block.Cell), weight >= required);
                StateAt(block.Cell).active = weight >= required;
            }

            foreach (GameplayBlockCell block in Enumerate(GameplayBlockKind.Lever))
                _signals.Set(block.SignalChannel, SourceKey("lever", block.Cell), StateAt(block.Cell).toggled);

            foreach (GameplayBlockCell block in Enumerate(GameplayBlockKind.BeamReceiver))
            {
                RuntimeState state = StateAt(block.Cell);
                bool active = _activeReceivers.Contains(block.Cell) || state.latched;
                state.active = active;
                _signals.Set(block.SignalChannel, SourceKey("receiver", block.Cell), active);
            }

            foreach (GameplayBlockCell block in Enumerate(GameplayBlockKind.Door))
            {
                RuntimeState state = StateAt(block.Cell);
                bool active = _signals.IsActive(block.SignalChannel);
                if (state.active != active) _optics?.MarkDirty();
                state.active = active;
            }

            foreach (KeyValuePair<Vector3Int, RuntimeState> item in _states)
                item.Value.view?.SetActivated(item.Value.active || item.Value.toggled);
        }

        void RebuildViews(bool preserveState)
        {
            var previous = new Dictionary<Vector3Int, RuntimeState>(_states);
            _states.Clear();
            _worldDirty = false;

            if (!preserveState && _viewRoot != null) DestroySafe(_viewRoot.gameObject);
            if (!preserveState || _viewRoot == null)
            {
                var container = new GameObject("GameplayBlocks");
                container.transform.SetParent(transform, false);
                _viewRoot = container.transform;
            }

            bool hasBounds = false;
            int minX = 0, maxX = 0, minZ = 0, maxZ = 0;
            foreach (KeyValuePair<Vector3Int, int> item in _world.Blocks)
            {
                Vector3Int cell = item.Key;
                if (!hasBounds)
                {
                    minX = maxX = cell.x;
                    minZ = maxZ = cell.z;
                    hasBounds = true;
                }
                else
                {
                    minX = Mathf.Min(minX, cell.x);
                    maxX = Mathf.Max(maxX, cell.x);
                    minZ = Mathf.Min(minZ, cell.z);
                    maxZ = Mathf.Max(maxZ, cell.z);
                }

                BlockDefinition definition = BlockLibrary.Get(item.Value);
                GameplayBlockProfile profile = definition != null ? BlockGameplayCatalog.Get(definition.id) : null;
                if (profile == null) continue;

                int direction = _world.GetDir(cell);
                RuntimeState state;
                if (preserveState && previous.TryGetValue(cell, out RuntimeState old) &&
                    old.blockIndex == item.Value && old.direction == direction && old.view != null)
                {
                    state = old;
                }
                else
                {
                    state = new RuntimeState { blockIndex = item.Value, direction = direction };
                    state.view = GameplayPrototypeFactory.Create(definition, profile, cell, direction, _viewRoot);
                }
                _states[cell] = state;
            }
            if (preserveState) RemoveChangedViews(previous);
            _minXZ = hasBounds ? new Vector2Int(minX, minZ) : Vector2Int.zero;
            _maxXZ = hasBounds ? new Vector2Int(maxX, maxZ) : Vector2Int.zero;
            _optics?.MarkDirty();
        }

        void RemoveChangedViews(Dictionary<Vector3Int, RuntimeState> previous)
        {
            foreach (KeyValuePair<Vector3Int, RuntimeState> item in previous)
            {
                if (_states.TryGetValue(item.Key, out RuntimeState current) && current == item.Value) continue;
                if (item.Value.view == null) continue;
                item.Value.view.gameObject.SetActive(false);
                DestroySafe(item.Value.view.gameObject);
            }
        }

        static string SourceKey(string type, Vector3Int cell) => $"{type}:{cell.x}:{cell.y}:{cell.z}";

        static void DestroySafe(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
