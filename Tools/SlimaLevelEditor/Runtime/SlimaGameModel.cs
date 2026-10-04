using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimaPrototype
{
    [Serializable]
    public sealed class SlimaState
    {
        public string id;
        public Vector2Int cell;
        public int gel;
        public SlimaRecipe recipe;
        public int pillMovesRemaining;

        public SlimaState Clone()
        {
            return new SlimaState
            {
                id = id,
                cell = cell,
                gel = gel,
                recipe = recipe,
                pillMovesRemaining = pillMovesRemaining
            };
        }
    }

    public readonly struct SlimaLightSegment
    {
        public readonly Vector2Int from;
        public readonly Vector2Int to;
        public readonly SlimaRecipe recipe;

        public SlimaLightSegment(Vector2Int from, Vector2Int to, SlimaRecipe recipe)
        {
            this.from = from;
            this.to = to;
            this.recipe = recipe;
        }
    }

    internal sealed class SlimaDeviceState
    {
        public bool active;
        public int remainingSteps;

        public SlimaDeviceState Clone()
        {
            return new SlimaDeviceState { active = active, remainingSteps = remainingSteps };
        }
    }

    internal sealed class SlimaGameSnapshot
    {
        public List<SlimaState> slimes;
        public Dictionary<string, SlimaDeviceState> devices;
        public HashSet<string> consumed;
        public int activeSlimeIndex;
        public int stepCount;
        public bool won;
        public bool lost;
        public bool exitReached;
    }

    public sealed class SlimaGameModel
    {
        private readonly Stack<SlimaGameSnapshot> undo = new Stack<SlimaGameSnapshot>();
        private readonly List<SlimaState> slimes = new List<SlimaState>();
        private readonly Dictionary<string, SlimaDeviceState> devices = new Dictionary<string, SlimaDeviceState>();
        private readonly Dictionary<string, bool> receivers = new Dictionary<string, bool>();
        private readonly HashSet<string> consumed = new HashSet<string>();
        private readonly List<SlimaLightSegment> lightSegments = new List<SlimaLightSegment>();

        private SlimaLevelData level;
        private SlimaResolvedRules rules;
        private int activeSlimeIndex;
        private int stepCount;
        private bool won;
        private bool lost;
        private bool exitReached;

        public event Action Changed;

        public SlimaLevelData Level => level;
        public SlimaResolvedRules Rules => rules;
        public IReadOnlyList<SlimaState> Slimes => slimes;
        public IReadOnlyList<SlimaLightSegment> LightSegments => lightSegments;
        public SlimaState ActiveSlime => slimes.Count == 0 ? null : slimes[Mathf.Clamp(activeSlimeIndex, 0, slimes.Count - 1)];
        public int ActiveSlimeIndex => activeSlimeIndex;
        public int StepCount => stepCount;
        public bool Won => won;
        public bool Lost => lost;
        public bool ExitReached => exitReached;
        public bool CanUndo => undo.Count > 0;

        public void Initialize(SlimaLevelData source)
        {
            Initialize(source, SlimaGlobalGameplayConfig.CreateDefault());
        }

        public void Initialize(SlimaLevelData source, SlimaGlobalGameplayConfig globalConfig)
        {
            level = source != null ? source.Clone() : throw new ArgumentNullException(nameof(source));
            rules = SlimaResolvedRules.Resolve(globalConfig, level.rules);
            Reset();
        }

        public void Reset()
        {
            if (level == null) return;

            slimes.Clear();
            devices.Clear();
            receivers.Clear();
            consumed.Clear();
            lightSegments.Clear();
            undo.Clear();
            activeSlimeIndex = 0;
            stepCount = 0;
            won = false;
            lost = false;
            exitReached = false;

            SlimaEntityData spawn = FindFirst(SlimaEntityType.Spawn);
            if (spawn != null)
            {
                slimes.Add(new SlimaState
                {
                    id = "slime_1",
                    cell = spawn.Cell,
                    gel = rules.initialGel,
                    recipe = spawn.recipe
                });
            }

            for (int i = 0; i < level.entities.Count; i++)
            {
                SlimaEntityData entity = level.entities[i];
                devices[entity.instanceId] = new SlimaDeviceState();
                if (entity.type == SlimaEntityType.LightReceiver) receivers[entity.instanceId] = false;
            }

            ResolveWorld();
            Changed?.Invoke();
        }

        public bool Move(Vector2Int direction)
        {
            if (won || lost || ActiveSlime == null || direction == Vector2Int.zero) return false;

            Vector2Int target = ActiveSlime.cell + direction;
            if (!CanEnter(target, ActiveSlime)) return false;

            SaveUndo();
            AdvanceStepTimers();
            ActiveSlime.cell = target;
            ApplyMovementGel(ActiveSlime);
            ApplyEnvironment(ActiveSlime);

            if (!CheckFailure())
            {
                ResolveCellEntry(activeSlimeIndex);
            }

            stepCount++;
            ResolveWorld();
            Changed?.Invoke();
            return true;
        }

        public bool SwitchActive()
        {
            if (won || lost || slimes.Count < 2) return false;
            activeSlimeIndex = (activeSlimeIndex + 1) % slimes.Count;
            Changed?.Invoke();
            return true;
        }

        public bool MergeAdjacent()
        {
            if (won || lost || slimes.Count != 2 || ActiveSlime == null) return false;
            int otherIndex = activeSlimeIndex == 0 ? 1 : 0;
            SlimaState other = slimes[otherIndex];
            if (Manhattan(ActiveSlime.cell, other.cell) != 1) return false;

            SaveUndo();
            ActiveSlime.gel = ClampGel(ActiveSlime.gel + other.gel);
            ActiveSlime.recipe |= other.recipe;
            ActiveSlime.pillMovesRemaining = Mathf.Max(ActiveSlime.pillMovesRemaining, other.pillMovesRemaining);
            slimes.RemoveAt(otherIndex);
            activeSlimeIndex = 0;
            ResolveWorld();
            Changed?.Invoke();
            return true;
        }

        public bool Interact()
        {
            if (won || lost || ActiveSlime == null) return false;
            List<SlimaEntityData> here = level.GetEntities(ActiveSlime.cell);

            for (int i = 0; i < here.Count; i++)
            {
                SlimaEntityData entity = here[i];
                if (entity.type == SlimaEntityType.Lever)
                {
                    SaveUndo();
                    devices[entity.instanceId].active = !devices[entity.instanceId].active;
                    ResolveWorld();
                    Changed?.Invoke();
                    return true;
                }

                if (entity.type == SlimaEntityType.TransferPipe && slimes.Count == 2)
                {
                    int otherIndex = activeSlimeIndex == 0 ? 1 : 0;
                    int amount = Mathf.Min(rules.transferAmount, Mathf.Max(0, ActiveSlime.gel - 1));
                    if (amount <= 0) return false;

                    SaveUndo();
                    ActiveSlime.gel -= amount;
                    slimes[otherIndex].gel = ClampGel(slimes[otherIndex].gel + amount);
                    ResolveWorld();
                    Changed?.Invoke();
                    return true;
                }
            }

            return false;
        }

        public bool Undo()
        {
            if (undo.Count == 0) return false;
            SlimaGameSnapshot snapshot = undo.Pop();
            Restore(snapshot);
            ResolveWorld();
            Changed?.Invoke();
            return true;
        }

        public bool IsDoorOpen(SlimaEntityData door)
        {
            if (door == null || door.type != SlimaEntityType.Door) return false;
            string link = string.IsNullOrWhiteSpace(door.linkId) ? door.instanceId : door.linkId;

            for (int i = 0; i < level.entities.Count; i++)
            {
                SlimaEntityData controller = level.entities[i];
                if (controller.linkId != link) continue;
                if (controller.type != SlimaEntityType.PressurePlate &&
                    controller.type != SlimaEntityType.Button &&
                    controller.type != SlimaEntityType.Lever) continue;
                if (devices.TryGetValue(controller.instanceId, out SlimaDeviceState state) && state.active) return true;
            }

            return false;
        }

        public bool IsEntityActive(string instanceId)
        {
            return devices.TryGetValue(instanceId, out SlimaDeviceState state) && state.active;
        }

        public bool IsReceiverActive(string instanceId)
        {
            return receivers.TryGetValue(instanceId, out bool active) && active;
        }

        public bool IsConsumed(string instanceId) => consumed.Contains(instanceId);

        private void ResolveCellEntry(int slimeIndex)
        {
            if (slimeIndex < 0 || slimeIndex >= slimes.Count) return;
            SlimaState slime = slimes[slimeIndex];
            List<SlimaEntityData> here = level.GetEntities(slime.cell);

            for (int i = 0; i < here.Count; i++)
            {
                SlimaEntityData entity = here[i];
                switch (entity.type)
                {
                    case SlimaEntityType.PaintStation:
                        slime.recipe |= entity.recipe;
                        break;
                    case SlimaEntityType.Splitter:
                        TryStandardSplit(slimeIndex, entity, SlimaRecipe.None);
                        break;
                    case SlimaEntityType.LaserSplitter:
                        TryStandardSplit(slimeIndex, entity, entity.recipe);
                        break;
                    case SlimaEntityType.CatapultSplitter:
                        TryCatapultSplit(slimeIndex, entity);
                        break;
                    case SlimaEntityType.Button:
                        devices[entity.instanceId].active = true;
                        devices[entity.instanceId].remainingSteps = Mathf.Max(1, rules.buttonDurationSteps);
                        break;
                    case SlimaEntityType.GelPotion:
                        if (consumed.Add(entity.instanceId)) slime.gel = ClampGel(slime.gel + rules.potionRestore);
                        break;
                    case SlimaEntityType.GelPill:
                        if (consumed.Add(entity.instanceId)) slime.pillMovesRemaining = rules.pillDurationSteps;
                        break;
                    case SlimaEntityType.Exit:
                        exitReached = true;
                        break;
                }
            }
        }

        private bool TryStandardSplit(int sourceIndex, SlimaEntityData splitter, SlimaRecipe addedRecipe)
        {
            if (!CanSplit(sourceIndex)) return false;
            SlimaState source = slimes[sourceIndex];
            Vector2Int forward = SlimaDirections.Get(splitter.direction);
            Vector2Int left = new Vector2Int(-forward.y, forward.x);
            Vector2Int a = source.cell + forward + left;
            Vector2Int b = source.cell + forward - left;
            if (!CanEnter(a, source) || !CanEnter(b, source) || a == b) return false;

            int secondGel = source.gel / 2;
            source.gel -= secondGel;
            source.recipe |= addedRecipe;
            source.cell = a;
            slimes.Add(new SlimaState
            {
                id = $"slime_{slimes.Count + 1}",
                cell = b,
                gel = secondGel,
                recipe = source.recipe,
                pillMovesRemaining = source.pillMovesRemaining
            });
            return true;
        }

        private bool TryCatapultSplit(int sourceIndex, SlimaEntityData splitter)
        {
            if (!CanSplit(sourceIndex)) return false;
            SlimaState source = slimes[sourceIndex];
            Vector2Int direction = SlimaDirections.Get(splitter.direction);
            Vector2Int target = source.cell;

            for (int i = 1; i <= rules.catapultDistanceCells; i++)
            {
                Vector2Int candidate = source.cell + direction * i;
                if (!CanEnter(candidate, source)) break;
                target = candidate;
            }

            if (target == source.cell) return false;
            int secondGel = source.gel / 2;
            source.gel -= secondGel;
            slimes.Add(new SlimaState
            {
                id = $"slime_{slimes.Count + 1}",
                cell = target,
                gel = secondGel,
                recipe = source.recipe,
                pillMovesRemaining = source.pillMovesRemaining
            });
            return true;
        }

        private bool CanSplit(int sourceIndex)
        {
            return sourceIndex >= 0 && sourceIndex < slimes.Count &&
                   slimes.Count < Mathf.Max(2, rules.maxSlimes) &&
                   slimes[sourceIndex].gel > rules.splitThreshold;
        }

        private void ApplyMovementGel(SlimaState slime)
        {
            int delta = -Mathf.Max(0, rules.baseMoveCost);
            if (slime.pillMovesRemaining > 0)
            {
                delta += rules.pillGelPerStep;
                slime.pillMovesRemaining--;
            }

            slime.gel = ClampGel(slime.gel + delta);
        }

        private void ApplyEnvironment(SlimaState slime)
        {
            SlimaRecipe required = SlimaRecipe.None;
            switch (level.GetTerrain(slime.cell))
            {
                case SlimaTerrainType.Acid: required = SlimaRecipe.Yellow; break;
                case SlimaTerrainType.Heat: required = SlimaRecipe.Red; break;
                case SlimaTerrainType.Cold: required = SlimaRecipe.Blue; break;
            }

            if (required != SlimaRecipe.None && (slime.recipe & required) == 0)
            {
                slime.gel = ClampGel(slime.gel - Mathf.Max(0, rules.environmentDamage));
            }
        }

        private void AdvanceStepTimers()
        {
            for (int i = 0; i < level.entities.Count; i++)
            {
                SlimaEntityData entity = level.entities[i];
                if (entity.type != SlimaEntityType.Button) continue;
                SlimaDeviceState state = devices[entity.instanceId];
                if (state.remainingSteps <= 0) continue;
                state.remainingSteps--;
                if (state.remainingSteps <= 0) state.active = false;
            }
        }

        private void ResolveWorld()
        {
            ResolvePressurePlates();
            ResolveLight();
            CheckFailure();
        }

        private void ResolvePressurePlates()
        {
            for (int i = 0; i < level.entities.Count; i++)
            {
                SlimaEntityData entity = level.entities[i];
                if (entity.type != SlimaEntityType.PressurePlate) continue;
                int required = entity.value > 0 ? entity.value : 50;
                bool active = false;

                for (int s = 0; s < slimes.Count; s++)
                {
                    if (slimes[s].cell != entity.Cell) continue;
                    if (EffectivePressure(slimes[s]) >= required) active = true;
                }

                devices[entity.instanceId].active = active;
            }
        }

        private void ResolveLight()
        {
            lightSegments.Clear();
            var receiverHits = new HashSet<string>();
            foreach (string key in new List<string>(receivers.Keys)) receivers[key] = false;

            for (int i = 0; i < level.entities.Count; i++)
            {
                SlimaEntityData emitter = level.entities[i];
                if (emitter.type != SlimaEntityType.LightEmitter) continue;

                Vector2Int direction = SlimaDirections.Get(emitter.direction);
                Vector2Int previous = emitter.Cell;
                SlimaRecipe lightRecipe = SlimaRecipe.None;

                for (int distance = 1; distance <= Mathf.Max(level.width, level.height); distance++)
                {
                    Vector2Int cell = emitter.Cell + direction * distance;
                    if (!level.InBounds(cell) || level.GetTerrain(cell) == SlimaTerrainType.Wall) break;
                    if (HasClosedDoor(cell)) break;

                    SlimaState filter = SlimeAt(cell);
                    if (filter != null)
                    {
                        if (filter.recipe == SlimaRecipe.All) break;
                        if (filter.recipe != SlimaRecipe.None) lightRecipe = filter.recipe;
                    }

                    lightSegments.Add(new SlimaLightSegment(previous, cell, lightRecipe));
                    previous = cell;

                    List<SlimaEntityData> here = level.GetEntities(cell);
                    for (int h = 0; h < here.Count; h++)
                    {
                        SlimaEntityData entity = here[h];
                        if (entity.type != SlimaEntityType.LightReceiver) continue;
                        bool active = lightRecipe != SlimaRecipe.None && (lightRecipe & entity.recipe) == entity.recipe;
                        receivers[entity.instanceId] = active;
                        if (active) receiverHits.Add(entity.instanceId);
                    }
                }
            }

            int receiverCount = 0;
            for (int i = 0; i < level.entities.Count; i++)
            {
                if (level.entities[i].type == SlimaEntityType.LightReceiver) receiverCount++;
            }

            bool receiversComplete = receiverCount > 0 && receiverHits.Count == receiverCount;
            switch (level.rules.winCondition)
            {
                case SlimaWinCondition.ActivateAllReceivers:
                    won = receiversComplete;
                    break;
                case SlimaWinCondition.ExitAndReceivers:
                    won = exitReached && receiversComplete;
                    break;
                default:
                    won = exitReached;
                    break;
            }
        }

        private bool CheckFailure()
        {
            if (rules.anySlimeZeroFails)
            {
                for (int i = 0; i < slimes.Count; i++)
                {
                    if (slimes[i].gel <= 0) lost = true;
                }
            }
            else
            {
                for (int i = slimes.Count - 1; i >= 0; i--)
                {
                    if (slimes[i].gel <= 0) slimes.RemoveAt(i);
                }
                lost = slimes.Count == 0;
                activeSlimeIndex = Mathf.Clamp(activeSlimeIndex, 0, Mathf.Max(0, slimes.Count - 1));
            }

            return lost;
        }

        private bool CanEnter(Vector2Int cell, SlimaState moving)
        {
            if (!level.InBounds(cell) || level.GetTerrain(cell) == SlimaTerrainType.Wall) return false;
            for (int i = 0; i < slimes.Count; i++)
            {
                if (slimes[i] != moving && slimes[i].cell == cell) return false;
            }

            List<SlimaEntityData> here = level.GetEntities(cell);
            for (int i = 0; i < here.Count; i++)
            {
                SlimaEntityData entity = here[i];
                if (entity.type == SlimaEntityType.LightEmitter || entity.type == SlimaEntityType.LightReceiver) return false;
                if (entity.type == SlimaEntityType.Door && !IsDoorOpen(entity)) return false;
            }

            return true;
        }

        private bool HasClosedDoor(Vector2Int cell)
        {
            List<SlimaEntityData> here = level.GetEntities(cell);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i].type == SlimaEntityType.Door && !IsDoorOpen(here[i])) return true;
            }
            return false;
        }

        private SlimaState SlimeAt(Vector2Int cell)
        {
            for (int i = 0; i < slimes.Count; i++)
            {
                if (slimes[i].cell == cell) return slimes[i];
            }
            return null;
        }

        private SlimaEntityData FindFirst(SlimaEntityType type)
        {
            for (int i = 0; i < level.entities.Count; i++)
            {
                if (level.entities[i].type == type) return level.entities[i];
            }
            return null;
        }

        private int EffectivePressure(SlimaState slime)
        {
            int componentCount = CountComponents(slime.recipe);
            float multiplier = componentCount >= 3
                ? rules.threeColorPressureMultiplier
                : componentCount == 2
                    ? rules.twoColorPressureMultiplier
                    : rules.oneColorPressureMultiplier;
            return Mathf.RoundToInt(slime.gel * multiplier);
        }

        private int ClampGel(int value)
        {
            if (!rules.capGelAtMax) return Mathf.Max(0, value);
            return Mathf.Clamp(value, 0, Mathf.Max(1, rules.maxGel));
        }

        private void SaveUndo()
        {
            var snapshot = new SlimaGameSnapshot
            {
                slimes = new List<SlimaState>(),
                devices = new Dictionary<string, SlimaDeviceState>(),
                consumed = new HashSet<string>(consumed),
                activeSlimeIndex = activeSlimeIndex,
                stepCount = stepCount,
                won = won,
                lost = lost,
                exitReached = exitReached
            };
            for (int i = 0; i < slimes.Count; i++) snapshot.slimes.Add(slimes[i].Clone());
            foreach (KeyValuePair<string, SlimaDeviceState> pair in devices) snapshot.devices[pair.Key] = pair.Value.Clone();
            undo.Push(snapshot);
            while (undo.Count > rules.undoHistoryLimit)
            {
                var copy = undo.ToArray();
                undo.Clear();
                for (int i = copy.Length - 2; i >= 0; i--) undo.Push(copy[i]);
            }
        }

        private void Restore(SlimaGameSnapshot snapshot)
        {
            slimes.Clear();
            for (int i = 0; i < snapshot.slimes.Count; i++) slimes.Add(snapshot.slimes[i].Clone());
            devices.Clear();
            foreach (KeyValuePair<string, SlimaDeviceState> pair in snapshot.devices) devices[pair.Key] = pair.Value.Clone();
            consumed.Clear();
            foreach (string item in snapshot.consumed) consumed.Add(item);
            activeSlimeIndex = snapshot.activeSlimeIndex;
            stepCount = snapshot.stepCount;
            won = snapshot.won;
            lost = snapshot.lost;
            exitReached = snapshot.exitReached;
        }

        private static int CountComponents(SlimaRecipe recipe)
        {
            int value = (int)recipe;
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }

        private static int Manhattan(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }
    }
}
