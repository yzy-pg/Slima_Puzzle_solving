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
        bool _synchronousSplitMovement;

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
            float viewYaw,
            bool synchronousSplitMovement = false)
        {
            _gameplay = gameplay;
            _playerPrefab = playerPrefab;
            _spawnCell = spawnCell;
            _spawnFacing = spawnFacing;
            _cellSize = cellSize;
            _viewYaw = viewYaw;
            _synchronousSplitMovement = synchronousSplitMovement;
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

            if (AnyActorMoving()) return;
            if (Input.GetKeyDown(KeyCode.Tab)) SelectNext();
            if (Input.GetKeyDown(KeyCode.F)) _gameplay?.TryInteract(ActiveActor);
        }

        /// <summary>兼容旧集成调用；主动能力当前不由 E 触发。</summary>
        public void RequestAbility() { }

        public void RestartParty()
        {
            CancelSplitAnimation();
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
            Vector3Int firstCell = mover.Cell + GridDirection.ToVector(direction);
            if (actor.State.form == SlimeForm.Solid && actor.State.size == SlimeSize.Large &&
                _gameplay.GetProfileAt(firstCell)?.Kind == GameplayBlockKind.Splitter)
            {
                // 切割只由移动进入触发；提示和 F 交互不提供切割入口。
                if (direction == mover.Facing) TrySplit(actor);
                return false;
            }
            bool sourceCanMove = TryBuildActorRoute(actor, direction, route);
            if (_synchronousSplitMovement && _actors.Count > 1)
                QueueSynchronousRouteForPartner(actor, direction);
            return sourceCanMove;
        }

        bool TryBuildActorRoute(SlimeActor actor, int direction, List<Vector3Int> route)
        {
            route.Clear();
            Vector3Int next = actor.Mover.Cell + GridDirection.ToVector(direction);
            if (!CanOccupy(actor, next, actor.State.element != SlimeElement.Blue)) return false;
            route.Add(next);

            if (actor.State.form != SlimeForm.Solid || actor.State.element != SlimeElement.Blue) return true;

            int limit = Mathf.Max(1, BlockGameplaySettings.Current.blueSlideLimit);
            for (int step = 1; step < limit; step++)
            {
                // 同步移动时，同伴会离开当前格；在实际落格时判定融合，不能提前截断路线。
                if (!_synchronousSplitMovement && CanFusePair(actor, FindAt(next))) break;
                GameplayBlockProfile floor = _gameplay.GetProfileAt(next);
                if (floor != null && floor.Kind == GameplayBlockKind.RedFloor) break;

                Vector3Int candidate = next + GridDirection.ToVector(direction);
                if (actor.State.size == SlimeSize.Large &&
                    _gameplay.GetProfileAt(candidate)?.Kind == GameplayBlockKind.Splitter) break;
                if (!CanOccupy(actor, candidate, false)) break;
                route.Add(candidate);
                next = candidate;
            }
            return true;
        }

        void QueueSynchronousRouteForPartner(SlimeActor source, int direction)
        {
            if (_actors.Count < 2) return;
            SlimeActor partner = _actors[0] == source ? _actors[1] : _actors[0];
            var partnerRoute = new List<Vector3Int>();
            if (!TryBuildActorRoute(partner, direction, partnerRoute) || partnerRoute.Count == 0) return;

            partner.Mover.SetFacing(direction);
            partner.Mover.QueueRoute(partnerRoute);
        }

        public bool CanEnterQueuedStep(SlimeGridMover mover, Vector3Int target)
        {
            SlimeActor actor = ActorFor(mover);
            return actor != null && CanOccupy(actor, target, false);
        }

        public float GetStepDuration(SlimeGridMover mover, int direction, float normalDuration)
        {
            SlimeActor actor = ActorFor(mover);
            if (actor == null || actor.State.form != SlimeForm.Solid || actor.State.element != SlimeElement.Blue ||
                !mover.HasPendingMovement) return normalDuration;
            int expectedInput = (direction - Mathf.RoundToInt(_viewYaw / 90f) + 4) & 3;
            if (ReadHeldDirection() != expectedInput) return normalDuration;
            float multiplier = BlockGameplaySettings.Current.blueSlideHeldDurationMultiplier;
            return normalDuration * Mathf.Clamp(multiplier, 0.1f, 1f);
        }

        static int ReadHeldDirection()
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) return 0;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) return 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) return 2;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) return 3;
            return -1;
        }

        public void OnCellEntered(SlimeGridMover mover, Vector3Int cell)
        {
            SlimeActor actor = ActorFor(mover);
            if (actor == null) return;

            if (_gameplay.ProcessCellEntry(actor, cell))
            {
                mover.StopAfterCurrentStep();
                RestartRequested?.Invoke();
                return;
            }

            // A floor transformation changes the active slime's sliding behavior immediately.
            if (actor.State.form != SlimeForm.Solid || actor.State.element != SlimeElement.Blue)
                mover.StopAfterCurrentStep();

            if (actor.State.form == SlimeForm.Solid)
            {
                if (FindFusionPartnerAt(actor, cell) != null) mover.StopAfterCurrentStep();
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
                bool mayFuse = CanFusePair(actor, occupied);
                if (!gasPassesThrough && !mayFuse) return false;
            }
            Vector3Int delta = cell - actor.Mover.Cell;
            int direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.z)
                ? (delta.x > 0 ? 1 : 3) : (delta.z > 0 ? 0 : 2);
            return _gameplay.CanEnter(actor, cell, direction, allowPush);
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
