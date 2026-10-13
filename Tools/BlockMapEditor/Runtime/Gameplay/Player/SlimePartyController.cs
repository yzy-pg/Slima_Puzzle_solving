using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>管理最多两只史莱姆、控制权、能力、分裂与融合。</summary>
    public sealed partial class SlimePartyController : MonoBehaviour, ISlimeMovementPlanner
    {
        readonly List<SlimeActor> _actors = new List<SlimeActor>(2);

        BlockGameplayController _gameplay;
        GameObject _playerPrefab;
        Vector3Int _spawnCell;
        int _spawnFacing;
        float _cellSize;
        float _viewYaw;
        int _activeIndex;
        SlimeGridMover _fusionMover;
        bool _abilityRequested;

        public event Action Changed;
        public event Action RestartRequested;
        public event Action ExitReached;

        public IReadOnlyList<SlimeActor> Actors => _actors;
        public SlimeActor ActiveActor => _actors.Count == 0 ? null : _actors[Mathf.Clamp(_activeIndex, 0, _actors.Count - 1)];
        public bool IsBusy => AnyActorMoving();

        public void Initialize(
            BlockGameplayController gameplay,
            GameObject playerPrefab,
            Vector3Int spawnCell,
            int spawnFacing,
            float cellSize,
            float viewYaw)
        {
            _gameplay = gameplay;
            _playerPrefab = playerPrefab;
            _spawnCell = spawnCell;
            _spawnFacing = spawnFacing;
            _cellSize = cellSize;
            _viewYaw = viewYaw;
            RestartParty();
        }

        void Update()
        {
            if (Time.timeScale == 0f) return;
            if (Input.GetKeyDown(KeyCode.R))
            {
                RestartRequested?.Invoke();
                return;
            }

            AdvanceSplitAnimation(Time.deltaTime);
            if (_splitSource != null) return;

            // Capture E before the movement guard so a press during the final hop is not lost.
            if (Input.GetKeyDown(KeyCode.E)) RequestAbility();
            if (AnyActorMoving() || _abilityRequested) return;
            if (Input.GetKeyDown(KeyCode.Tab)) SelectNext();
            if (Input.GetKeyDown(KeyCode.F)) _gameplay?.TryInteract(ActiveActor);
        }

        public void RequestAbility()
        {
            var actor = ActiveActor;
            if (actor == null || _splitSource != null ||
                (actor.State.form == SlimeForm.Solid && actor.State.element != SlimeElement.Blue)) return;
            _abilityRequested = true;
            actor.Mover.StopAfterCurrentStep();
            actor.Mover.FaceInputDirection();
            actor.Mover.InputEnabled = false;
        }

        void LateUpdate()
        {
            // All movers have finished Update; use the landing cell and facing, regardless of script order.
            if (Time.timeScale == 0f || !_abilityRequested || AnyActorMoving()) return;
            _abilityRequested = false;
            UseAbility();
            RefreshControl();
        }

        public void RestartParty()
        {
            CancelSplitAnimation();
            _abilityRequested = false;
            _fusionMover = null;
            for (int i = _actors.Count - 1; i >= 0; i--)
                if (_actors[i] != null)
                {
                    _actors[i].gameObject.SetActive(false);
                    if (Application.isPlaying) Destroy(_actors[i].gameObject);
                    else DestroyImmediate(_actors[i].gameObject);
                }
            _actors.Clear();
            _activeIndex = 0;

            SlimeActor actor = SpawnActor(new SlimeState(), _spawnCell, _spawnFacing);
            if (actor != null) _actors.Add(actor);
            RefreshControl();
            Changed?.Invoke();
        }

        public SlimeProgress[] CaptureProgress(out int activeActor)
        {
            activeActor = _activeIndex;
            var result = new SlimeProgress[_actors.Count];
            for (int i = 0; i < _actors.Count; i++)
                result[i] = new SlimeProgress
                {
                    cell = _actors[i].Mover.Cell,
                    facing = _actors[i].Mover.Facing,
                    state = _actors[i].State.Copy()
                };
            return result;
        }

        public void RestoreProgress(SlimeProgress[] actors, int activeActor)
        {
            CancelSplitAnimation();
            _abilityRequested = false;
            _fusionMover = null;
            foreach (SlimeActor actor in _actors)
            {
                actor.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(actor.gameObject);
                else DestroyImmediate(actor.gameObject);
            }
            _actors.Clear();
            foreach (SlimeProgress saved in actors)
                _actors.Add(SpawnActor(saved.state.Copy(), saved.cell, saved.facing));
            _activeIndex = activeActor;
            RefreshControl();
            Changed?.Invoke();
        }

        public bool TryBuildRoute(SlimeGridMover mover, int direction, List<Vector3Int> route)
        {
            SlimeActor actor = ActorFor(mover);
            if (actor == null || actor != ActiveActor || route == null || IsBusy) return false;

            route.Clear();
            Vector3Int next = mover.Cell + GridDirection.ToVector(direction);
            if (actor.State.form == SlimeForm.Solid && actor.State.size == SlimeSize.Large &&
                _gameplay.GetProfileAt(next)?.Kind == GameplayBlockKind.Splitter)
            {
                // Movement and F share the same rear-entry interaction.
                if (direction == mover.Facing) TrySplit(actor);
                return false;
            }
            if (!CanOccupy(actor, next, actor.State.element != SlimeElement.Blue)) return false;
            route.Add(next);

            if (actor.State.form != SlimeForm.Solid || actor.State.element != SlimeElement.Blue)
                return true;

            int limit = Mathf.Max(1, BlockGameplaySettings.Current.blueSlideLimit);
            for (int i = 1; i < limit; i++)
            {
                if (CanFusePair(actor, FindAt(next))) break;
                GameplayBlockProfile floor = _gameplay.GetProfileAt(next);
                if (floor != null && floor.Kind == GameplayBlockKind.RedFloor) break;

                Vector3Int candidate = next + GridDirection.ToVector(direction);
                if (actor.State.size == SlimeSize.Large &&
                    _gameplay.GetProfileAt(candidate)?.Kind == GameplayBlockKind.Splitter) break;
                if (!CanOccupy(actor, candidate, false)) break;
                route.Add(candidate);
                next = candidate;
            }
            return route.Count > 0;
        }

        public void OnCellEntered(SlimeGridMover mover, Vector3Int cell)
        {
            SlimeActor actor = ActorFor(mover);
            if (actor == null) return;

            if (_gameplay.ApplyPassive(actor))
            {
                mover.StopAfterCurrentStep();
                RestartRequested?.Invoke();
                return;
            }

            // 途中变成气态或不再是蓝色时，连续滑行立即结束。
            if (actor.State.form != SlimeForm.Solid || actor.State.element != SlimeElement.Blue)
                mover.StopAfterCurrentStep();

            bool manualFusion = mover == _fusionMover;
            if (manualFusion) _fusionMover = null;
            if (manualFusion || (actor.State.form == SlimeForm.Solid && actor.State.element == SlimeElement.Blue))
            {
                if (HasOtherSolidAt(actor, cell)) mover.StopAfterCurrentStep();
                FuseAt(cell);
            }
            if (_gameplay.IsExit(cell)) ExitReached?.Invoke();
            Changed?.Invoke();
        }

        public bool HasOtherSolidAt(SlimeActor source, Vector3Int cell)
        {
            foreach (SlimeActor actor in _actors)
                if (actor != null && actor != source && actor.State.form == SlimeForm.Solid && actor.Mover.Cell == cell)
                    return true;
            return false;
        }

        public bool CanFuseFacing(SlimeActor source)
        {
            if (source == null || IsBusy) return false;
            Vector3Int target = source.Mover.Cell + GridDirection.ToVector(source.Mover.Facing);
            return CanFusePair(source, FindAt(target)) &&
                   _gameplay.CanEnter(source, target, source.Mover.Facing, false);
        }

        public bool TryFuseFacing(SlimeActor source)
        {
            if (!CanFuseFacing(source)) return false;
            Vector3Int target = source.Mover.Cell + GridDirection.ToVector(source.Mover.Facing);
            if (!source.Mover.TryMoveTo(target)) return false;
            _fusionMover = source.Mover;
            Changed?.Invoke();
            return true;
        }

        static bool CanFusePair(SlimeActor first, SlimeActor second) =>
            first != null && second != null && first != second &&
            first.State.form == SlimeForm.Solid && second.State.form == SlimeForm.Solid &&
            first.State.size == SlimeSize.Small && second.State.size == SlimeSize.Small;

        public SlimeActor FindAt(Vector3Int cell)
        {
            SlimeActor gas = null;
            foreach (SlimeActor actor in _actors)
            {
                if (actor == null || actor.Mover.Cell != cell) continue;
                if (actor.State.form == SlimeForm.Solid) return actor;
                gas = actor;
            }
            return gas;
        }

        void UseAbility()
        {
            SlimeActor actor = ActiveActor;
            if (actor == null) return;

            if (actor.State.form == SlimeForm.Gas)
            {
                if (!_gameplay.CanSolidify(actor)) return;
                SlimeState solid = actor.State.Copy();
                solid.form = SlimeForm.Solid;
                actor.SetState(solid);
                if (_gameplay.ApplyPassive(actor)) RestartRequested?.Invoke();
            }
            else if (actor.State.element == SlimeElement.Blue)
            {
                SlimeState next = actor.State.Copy();
                next.opticsMode = (BlueOpticsMode)(((int)next.opticsMode + 1) % 3);
                actor.SetState(next);
            }
            Changed?.Invoke();
        }

        void SelectNext()
        {
            if (_actors.Count < 2) return;
            _activeIndex = (_activeIndex + 1) % _actors.Count;
            RefreshControl();
            Changed?.Invoke();
        }

        bool CanOccupy(SlimeActor actor, Vector3Int cell, bool allowPush)
        {
            SlimeActor occupied = FindAt(cell);
            if (occupied != null && occupied != actor)
            {
                bool gasPassesThrough = actor.State.form == SlimeForm.Gas || occupied.State.form == SlimeForm.Gas;
                bool mayFuse = actor.State.element == SlimeElement.Blue && CanFusePair(actor, occupied);
                if (!gasPassesThrough && !mayFuse) return false;
            }
            Vector3Int delta = cell - actor.Mover.Cell;
            int direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.z)
                ? (delta.x > 0 ? 1 : 3) : (delta.z > 0 ? 0 : 2);
            return _gameplay.CanEnter(actor, cell, direction, allowPush);
        }

        void FuseAt(Vector3Int cell)
        {
            SlimeActor first = null;
            SlimeActor second = null;
            foreach (SlimeActor actor in _actors)
            {
                if (actor == null || actor.Mover.Cell != cell || actor.State.form != SlimeForm.Solid ||
                    actor.State.size != SlimeSize.Small) continue;
                if (first == null) first = actor;
                else { second = actor; break; }
            }
            if (first == null || second == null) return;

            SlimeActor survivor = first.IsControlled ? first : second.IsControlled ? second : first;
            SlimeActor removed = survivor == first ? second : first;
            survivor.SetState(SlimeRules.Fuse(first.State, second.State));
            _actors.Remove(removed);
            if (Application.isPlaying) Destroy(removed.gameObject);
            else DestroyImmediate(removed.gameObject);
            _activeIndex = Mathf.Max(0, _actors.IndexOf(survivor));
            RefreshControl();
        }

        SlimeActor SpawnActor(SlimeState state, Vector3Int cell, int facing)
        {
            GameObject prefab = _playerPrefab != null
                ? _playerPrefab
                : Resources.Load<GameObject>("Player/BlockMapPlayer");
            GameObject instance = prefab != null ? Instantiate(prefab, transform) : CreateFallbackPlayer(transform);
            instance.name = $"Slime_{_actors.Count + 1}";

            SlimeActor actor = instance.GetComponent<SlimeActor>();
            if (actor == null) actor = instance.AddComponent<SlimeActor>();
            actor.Initialize(state, this, _cellSize, _viewYaw);
            actor.Mover.Teleport(cell, facing);
            return actor;
        }

        static GameObject CreateFallbackPlayer(Transform parent)
        {
            var root = new GameObject("SlimePrototype");
            root.transform.SetParent(parent, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Visual";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.62f, 0.7f);
            Collider collider = body.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var facing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            facing.name = "Facing";
            facing.transform.SetParent(body.transform, false);
            facing.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            facing.transform.localScale = new Vector3(0.22f, 0.18f, 0.24f);
            collider = facing.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return root;
        }

        SlimeActor ActorFor(SlimeGridMover mover)
        {
            foreach (SlimeActor actor in _actors)
                if (actor != null && actor.Mover == mover) return actor;
            return null;
        }

        void RefreshControl()
        {
            _activeIndex = _actors.Count == 0 ? 0 : Mathf.Clamp(_activeIndex, 0, _actors.Count - 1);
            for (int i = 0; i < _actors.Count; i++)
                if (_actors[i] != null)
                {
                    _actors[i].SetControlled(i == _activeIndex);
                    if (_splitSource != null) _actors[i].Mover.InputEnabled = false;
                }
        }

        bool AnyActorMoving()
        {
            if (_splitSource != null) return true;
            foreach (SlimeActor actor in _actors)
                if (actor != null && actor.Mover.HasPendingMovement) return true;
            return false;
        }
    }
}
