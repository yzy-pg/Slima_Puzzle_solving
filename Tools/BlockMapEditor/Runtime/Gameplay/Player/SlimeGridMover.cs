using System.Collections.Generic;
using UnityEngine;

namespace BlockMap.Gameplay
{
    public interface ISlimeMovementPlanner
    {
        bool TryBuildRoute(SlimeGridMover mover, int direction, List<Vector3Int> route);
        bool CanEnterQueuedStep(SlimeGridMover mover, Vector3Int target);
        void OnCellEntered(SlimeGridMover mover, Vector3Int cell);
        float GetStepDuration(SlimeGridMover mover, int direction, float normalDuration);
    }

    /// <summary>只负责输入和格间动画；所有通行规则由 ISlimeMovementPlanner 提供。</summary>
    public sealed class SlimeGridMover : MonoBehaviour
    {
        public float cellSize = 1f;
        public float viewYaw;
        public float stepDuration = 0.16f;
        public float hopHeight = 0.14f;
        public Transform visual;

        public Vector3Int Cell { get; private set; }
        public int Facing { get; private set; }
        public bool IsMoving { get; private set; }
        public Vector3Int DestinationCell => IsMoving && !_interactionMotion ? CellOf(_stepTarget) : Cell;
        public bool HasPendingMovement => IsMoving || _route.Count > 0;
        public bool HasQueuedRoute => _route.Count > 0;
        public bool InputEnabled { get; set; } = true;
        public ISlimeMovementPlanner Planner { get; set; }

        readonly List<Vector3Int> _route = new List<Vector3Int>();
        Vector3 _stepStart;
        Vector3 _stepTarget;
        float _progress;
        float _visualHeight;
        bool _interactionMotion;

        void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            CharacterController controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            BlockMapGridWalker legacy = GetComponent<BlockMapGridWalker>();
            if (legacy != null) legacy.enabled = false;
        }

        void Update()
        {
            if (Time.timeScale == 0f || _interactionMotion) return;
            if (IsMoving) AdvanceStep();
            else if (_route.Count > 0) BeginNextStep();
            else if (InputEnabled) TryStartRoute();
        }

        public void Teleport(Vector3Int cell, int facing)
        {
            _route.Clear();
            _interactionMotion = false;
            Cell = cell;
            Facing = ((facing % 4) + 4) % 4;
            IsMoving = false;
            transform.position = CenterOf(cell);
            transform.rotation = Quaternion.Euler(0f, Facing * 90f, 0f);
            ResetVisualPosition();
        }

        public void StopAfterCurrentStep() => _route.Clear();

        public void SetFacing(int direction)
        {
            Facing = ((direction % 4) + 4) % 4;
            transform.rotation = Quaternion.Euler(0f, Facing * 90f, 0f);
        }

        public void BeginInteractionMotion()
        {
            _route.Clear();
            _interactionMotion = true;
            IsMoving = true;
            InputEnabled = false;
            ResetVisualPosition();
        }

        public bool TryMoveTo(Vector3Int cell)
        {
            if (HasPendingMovement) return false;
            _route.Add(cell);
            BeginNextStep();
            return true;
        }

        public bool QueueRoute(IList<Vector3Int> route)
        {
            if (route == null || route.Count == 0 || HasPendingMovement) return false;
            for (int i = 0; i < route.Count; i++) _route.Add(route[i]);
            BeginNextStep();
            return true;
        }

        public void SetVisualHeight(float height)
        {
            _visualHeight = Mathf.Max(0f, height);
            if (!IsMoving) ResetVisualPosition();
        }

        void TryStartRoute()
        {
            if (!FaceInputDirection()) return;
            int direction = Facing;

            _route.Clear();
            if (Planner == null || !Planner.TryBuildRoute(this, direction, _route)) return;
            if (_route.Count > 0) BeginNextStep();
        }

        public bool FaceInputDirection()
        {
            int direction = ReadDirection();
            if (direction < 0 || HasPendingMovement) return false;
            int yawSteps = Mathf.RoundToInt(viewYaw / 90f) & 3;
            Facing = (direction + yawSteps) & 3;
            transform.rotation = Quaternion.Euler(0f, Facing * 90f, 0f);
            return true;
        }

        void BeginNextStep()
        {
            if (_route.Count == 0) return;
            if (Planner != null && !Planner.CanEnterQueuedStep(this, _route[0]))
            {
                _route.Clear();
                IsMoving = false;
                return;
            }
            _stepStart = transform.position;
            _stepTarget = CenterOf(_route[0]);
            _route.RemoveAt(0);
            _progress = 0f;
            IsMoving = true;
        }

        void AdvanceStep()
        {
            Vector3Int delta = CellOf(_stepTarget) - CellOf(_stepStart);
            int direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.z)
                ? (delta.x > 0 ? 1 : 3) : (delta.z > 0 ? 0 : 2);
            float duration = Planner != null ? Planner.GetStepDuration(this, direction, stepDuration) : stepDuration;
            _progress += Time.deltaTime / Mathf.Max(0.01f, duration);
            float t = Mathf.Clamp01(_progress);
            transform.position = Vector3.Lerp(_stepStart, _stepTarget, t);

            if (visual != null)
            {
                float hop = Mathf.Sin(t * Mathf.PI) * hopHeight;
                visual.localPosition = new Vector3(0f, _visualHeight + hop, 0f);
            }

            if (t < 1f) return;
            transform.position = _stepTarget;
            Cell = CellOf(_stepTarget);
            IsMoving = false;
            ResetVisualPosition();
            Planner?.OnCellEntered(this, Cell);
        }

        void ResetVisualPosition()
        {
            if (visual != null) visual.localPosition = new Vector3(0f, _visualHeight, 0f);
        }

        static int ReadDirection()
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) return 0;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) return 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) return 2;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) return 3;
            return -1;
        }

        Vector3 CenterOf(Vector3Int cell) =>
            new Vector3(cell.x + cellSize * 0.5f, cell.y, cell.z + cellSize * 0.5f);

        static Vector3Int CellOf(Vector3 position) =>
            new Vector3Int(Mathf.FloorToInt(position.x), Mathf.RoundToInt(position.y), Mathf.FloorToInt(position.z));
    }
}
