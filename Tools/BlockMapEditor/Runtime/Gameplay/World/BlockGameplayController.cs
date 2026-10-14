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
            public int receiverFaces;
            public int latchedReceiverFaces;
            public GameplayBlockView view;
            public int blockIndex;
            public int direction;
            public float buttonRemaining;
        }

        readonly Dictionary<Vector3Int, RuntimeState> _states = new Dictionary<Vector3Int, RuntimeState>();
        readonly BlockSignalBus _signals = new BlockSignalBus();
        readonly HashSet<Vector3Int> _activeReceivers = new HashSet<Vector3Int>();
        readonly Dictionary<int, Color> _mechanismColors = new Dictionary<int, Color>();

        BlockWorld _world;
        SlimePartyController _party;
        BlockOpticsSystem _optics;
        Transform _viewRoot;
        Transform _burnRoot;
        bool _worldDirty;
        Vector2Int _minXZ;
        Vector2Int _maxXZ;

        public event Action Changed;

        /// <summary>修改一个机关的关联色，同频道的全部门立即更新；编号保证移动或重建后仍使用同色。</summary>
        public void SetMechanismColor(Vector3Int cell, Color color)
        {
            if (_world == null) return;
            var profile = GetProfileAt(cell);
            if (profile == null || !BlockLinkRules.IsSignalSource(profile.Kind)) return;
            _world.EnsureMechanismNumbers();
            _mechanismColors[_world.GetMechanismNumber(cell)] = color;
            RefreshLinkColors();
        }

        void RefreshLinkColors()
        {
            var views = new List<GameplayBlockView>();
            foreach (var state in _states.Values) if (state.view != null) views.Add(state.view);
            BlockLinkVisuals.Apply(_world, views, _mechanismColors);
        }

        public void Initialize(BlockWorld world)
        {
            if (_world != null) _world.Changed -= OnWorldChanged;
            _world = world;
            if (_viewRoot == null)
            {
                var previews = new Dictionary<string, GameplayBlockView>();
                var bakedViews = GetComponentsInChildren<GameplayBlockView>(true);
                foreach (var view in bakedViews)
                    previews[view.name] = view;
                // A scene-authored closed door orientation must survive entering Play.
                foreach (var item in _world.Blocks)
                {
                    var definition = BlockLibrary.Get(item.Value);
                    if (definition == null || BlockGameplayCatalog.Get(definition.id)?.Kind != GameplayBlockKind.Door) continue;
                    string name = $"Gameplay_{definition.id}_{item.Key.x}_{item.Key.y}_{item.Key.z}";
                    if (previews.TryGetValue(name, out var view))
                        _world.SetDir(item.Key, Mathf.RoundToInt(view.transform.eulerAngles.y / 90f) & 3);
                }
                // Baked previews are replaced by live views, not left underneath animations.
                foreach (var view in bakedViews) DestroySafe(view.gameObject);
            }
            _mechanismColors.Clear();
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
            if (_burnRoot != null) DestroySafe(_burnRoot.gameObject);
            _burnRoot = null;
            _signals.Clear();
            _activeReceivers.Clear();
            RebuildViews(false);
            _optics?.MarkDirty();
        }

        public MechanismProgress[] CaptureProgress()
        {
            if (_worldDirty) RebuildViews(true);
            var result = new List<MechanismProgress>();
            foreach (KeyValuePair<Vector3Int, RuntimeState> item in _states)
                if (item.Value.toggled || item.Value.latched || item.Value.buttonRemaining > 0f)
                    result.Add(new MechanismProgress
                    {
                        cell = item.Key,
                        toggled = item.Value.toggled,
                        receiverFaces = item.Value.latchedReceiverFaces,
                        latched = item.Value.latched,
                        buttonRemaining = item.Value.buttonRemaining
                    });
            return result.ToArray();
        }

        public void RestoreProgress(MechanismProgress[] mechanisms)
        {
            ResetState();
            if (mechanisms != null)
                foreach (MechanismProgress saved in mechanisms)
                    if (_states.TryGetValue(saved.cell, out RuntimeState state))
                    {
                        state.toggled = saved.toggled;
                        state.latched = saved.latched;
                        state.latchedReceiverFaces = saved.receiverFaces;
                        state.buttonRemaining = saved.buttonRemaining;
                    }
            foreach (GameplayBlockCell door in Enumerate(GameplayBlockKind.Door))
                _signals.SetToggled(door.SignalChannel, StateAt(door.Cell).toggled);
            _optics?.RefreshIfNeeded();
            EvaluateSignals(false);
        }

        void OnDestroy()
        {
            if (_world != null) _world.Changed -= OnWorldChanged;
            if (_party != null) _party.Changed -= OnPartyChanged;
        }

        void Update()
        {
            if (_worldDirty) RebuildViews(true);
            foreach (var state in _states.Values)
                state.buttonRemaining = Mathf.Max(0f, state.buttonRemaining - Time.deltaTime);
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
            // The blade runs along local +Z: enter from the rear, never across its sides.
            if (GetProfileAt(cell)?.Kind == GameplayBlockKind.Splitter && direction != GetDirection(cell))
                return false;
            Vector3Int from = cell - GridDirection.ToVector(direction);
            if (GetProfileAt(from)?.Kind == GameplayBlockKind.Splitter &&
                (direction & 1) != (GetDirection(from) & 1)) return false;
            return CanOccupyCell(actor, cell, direction, allowPush);
        }

        // A split hop can land sideways without walking through the blade's side barrier.
        public bool CanLand(SlimeActor actor, Vector3Int cell) => CanOccupyCell(actor, cell, 0, false);

        bool CanOccupyCell(SlimeActor actor, Vector3Int cell, int direction, bool allowPush)
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
                else if (profile.Kind == GameplayBlockKind.Destructible && CanDestroyOnEntry(actor, cell))
                {
                    // Keep the route legal; the object is removed only when the actor lands there.
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
            if (actor == null) return false;
            GameplayBlockProfile profile = GetProfileAt(actor.Mover.Cell);
            if (profile == null) return false;

            SlimeState next = actor.State.Copy();
            switch (profile.Kind)
            {
                case GameplayBlockKind.RedFloor:
                    next.element = SlimeElement.Red;
                    next.form = SlimeForm.Solid;
                    actor.SetState(next);
                    return false;

                case GameplayBlockKind.BlueFloor:
                    next.element = SlimeElement.Blue;
                    next.form = SlimeForm.Solid;
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

            if (_party != null && _party.CanSplit(actor) && _party.TryGetSplitterCell(actor, out Vector3Int splitter))
            {
                interaction = new SlimeInteraction(SlimeInteractionKind.Split, splitter);
                return true;
            }
            int facing = actor.Mover.Facing;
            int[] order = { facing, GridDirection.Left(facing), GridDirection.Right(facing), GridDirection.Back(facing) };
            foreach (int direction in order)
            {
                Vector3Int cell = actor.Mover.Cell + GridDirection.ToVector(direction);
                GameplayBlockProfile profile = GetProfileAt(cell);
                if (profile == null || profile.Kind != GameplayBlockKind.Button) continue;
                interaction = new SlimeInteraction(SlimeInteractionKind.Button, cell);
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
                case SlimeInteractionKind.Button:
                    StateAt(interaction.Cell).buttonRemaining = .65f;
                    GameplayBlockProfile button = GetProfileAt(interaction.Cell);
                    string buttonChannel = ResolveChannel(interaction.Cell, button);
                    _signals.Set(buttonChannel, SourceKey("button", interaction.Cell), true);
                    ToggleLinkedDoors(buttonChannel);
                    EvaluateSignals();
                    Changed?.Invoke();
                    return true;
                case SlimeInteractionKind.Lever:
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>处理每次实际进入的格子，包含滑动途中经过的格子。</summary>
        public bool ProcessCellEntry(SlimeActor actor, Vector3Int cell)
        {
            if (actor == null) return false;
            if (CanDestroyOnEntry(actor, cell)) DestroyOnEntry(actor, cell);

            GameplayBlockProfile profile = GetProfileAt(cell);
            if (profile != null && profile.Kind == GameplayBlockKind.Lever)
                ToggleLeverAt(cell, profile);

            return ApplyPassive(actor);
        }

        void ToggleLeverAt(Vector3Int cell, GameplayBlockProfile profile)
        {
            RuntimeState state = StateAt(cell);
            state.toggled = !state.toggled;
            string channel = ResolveChannel(cell, profile);
            _signals.Set(channel, SourceKey("lever", cell), state.toggled);
            ToggleLinkedDoors(channel);
        }

        string ResolveChannel(Vector3Int cell, GameplayBlockProfile profile) =>
            BlockLinkRules.ResolveChannel(
                _world != null ? _world.GetLinkId(cell) : string.Empty,
                profile,
                _world == null || !_world.HasLinkIds);

        void ToggleLinkedDoors(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return;
            _signals.Toggle(channel);
            bool signalState = _signals.IsToggled(channel);
            foreach (GameplayBlockCell door in Enumerate(GameplayBlockKind.Door))
            {
                if (!string.Equals(door.SignalChannel, channel, StringComparison.Ordinal)) continue;
                RuntimeState state = StateAt(door.Cell);
                state.toggled = signalState;
                SetDoorSignalState(door, state, signalState);
            }
        }

        void SetDoorSignalState(GameplayBlockCell door, RuntimeState state, bool signalState)
        {
            bool isOpen = door.Profile.invertSignal ? !signalState : signalState;
            if (state.active != isOpen) _optics?.MarkDirty();
            state.active = isOpen;
            state.view?.SetActivated(isOpen);
        }

        public bool CanDestroyOnEntry(SlimeActor actor, Vector3Int cell)
        {
            return actor != null && actor.State.form == SlimeForm.Solid && actor.State.element == SlimeElement.Red &&
                   GetProfileAt(cell)?.Kind == GameplayBlockKind.Destructible;
        }

        public bool DestroyOnEntry(SlimeActor actor, Vector3Int cell)
        {
            if (!CanDestroyOnEntry(actor, cell)) return false;
            if (_worldDirty) RebuildViews(true);
            if (_states.TryGetValue(cell, out RuntimeState state) && state.view != null)
            {
                // Hide the live object immediately; only the detached burn effect may remain visible.
                if (_burnRoot == null)
                {
                    _burnRoot = new GameObject("BurningPackages").transform;
                    _burnRoot.SetParent(transform, false);
                }
                DestructibleBurnEffect.Create(state.view, _burnRoot);
                state.view.gameObject.SetActive(false);
            }
            _world.Remove(cell);
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
            foreach (var state in _states.Values) state.receiverFaces = 0;
        }

        public void ActivateReceiver(Vector3Int cell, int sourceDirection = 0)
        {
            GameplayBlockProfile profile = GetProfileAt(cell);
            if (profile == null || profile.Kind != GameplayBlockKind.BeamReceiver) return;
            RuntimeState state = StateAt(cell);
            int face = 1 << (sourceDirection & 3);
            state.receiverFaces |= face;
            if (profile.IsLatched) state.latchedReceiverFaces |= face;
            if (profile.IsLatched && !state.latched)
            {
                state.latched = true;
                Changed?.Invoke();
            }
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

        void EvaluateSignals(bool processActivationEdges = true)
        {
            if (_world == null) return;

            foreach (GameplayBlockCell block in Enumerate(GameplayBlockKind.PressurePlate))
            {
                int weight = 0;
                if (_party != null)
                {
                    foreach (SlimeActor actor in _party.Actors)
                        if (actor != null && actor.Mover.Cell == block.Cell) weight += actor.Weight;
                }
                int required = Mathf.Max(1, block.Profile.value);
                bool active = weight >= required;
                if (_signals.Set(block.SignalChannel, SourceKey("plate", block.Cell), active) && processActivationEdges)
                    ToggleLinkedDoors(block.SignalChannel);
                StateAt(block.Cell).active = active;
            }

            foreach (GameplayBlockCell block in Enumerate(GameplayBlockKind.Lever))
                _signals.Set(block.SignalChannel, SourceKey("lever", block.Cell), StateAt(block.Cell).toggled);

            foreach (GameplayBlockCell block in Enumerate(GameplayBlockKind.Button))
            {
                var state = StateAt(block.Cell);
                state.active = state.buttonRemaining > 0f;
                _signals.Set(block.SignalChannel, SourceKey("button", block.Cell), state.active);
            }

            foreach (GameplayBlockCell block in Enumerate(GameplayBlockKind.BeamReceiver))
            {
                RuntimeState state = StateAt(block.Cell);
                bool active = _activeReceivers.Contains(block.Cell) || state.latched;
                state.active = active;
                state.view?.SetReceiverFaces(state.receiverFaces | state.latchedReceiverFaces);
                if (_signals.Set(block.SignalChannel, SourceKey("receiver", block.Cell), active) && processActivationEdges)
                    ToggleLinkedDoors(block.SignalChannel);
            }

            foreach (GameplayBlockCell block in Enumerate(GameplayBlockKind.Door))
            {
                RuntimeState state = StateAt(block.Cell);
                bool signalState = _signals.IsToggled(block.SignalChannel);
                state.toggled = signalState;
                SetDoorSignalState(block, state, signalState);
            }

            foreach (KeyValuePair<Vector3Int, RuntimeState> item in _states)
            {
                if (item.Value.view == null || GetProfileAt(item.Key)?.Kind == GameplayBlockKind.Door) continue;
                item.Value.view.SetActivated(item.Value.active || item.Value.toggled);
            }
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
            RefreshLinkColors();
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
            target.SetActive(false);
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
