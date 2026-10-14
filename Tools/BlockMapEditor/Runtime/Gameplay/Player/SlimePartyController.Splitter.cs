using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockMap.Gameplay
{
    public sealed partial class SlimePartyController
    {
        [Header("切分机交互")]
        [Min(.1f)] public float splitterCutDuration = 1.4f;
        [Min(.1f)] public float splitterJumpDuration = .45f;
        [Min(0f)] public float splitterJumpHeight = .45f;

        readonly List<Vector3Int> _splitCells = new List<Vector3Int>(2);
        readonly List<ParticleSystem> _cutParticleTails = new List<ParticleSystem>();
        SlimeActor _splitSource;
        SlimeActor _splitSecond;
        ParticleSystem _cutParticles;
        Vector3Int _splitCell;
        Vector3Int _splitOriginalCell;
        Vector3 _splitStart;
        float _splitElapsed;
        int _splitFacing;

        public bool TryGetSplitterCell(SlimeActor source, out Vector3Int cell)
        {
            cell = default;
            if (source == null || _gameplay == null) return false;
            cell = source.Mover.Cell;
            if (_gameplay.GetProfileAt(cell)?.Kind != GameplayBlockKind.Splitter)
                cell += GridDirection.ToVector(source.Mover.Facing);
            return _gameplay.GetProfileAt(cell)?.Kind == GameplayBlockKind.Splitter &&
                   source.Mover.Facing == _gameplay.GetDirection(cell);
        }

        public bool CanSplit(SlimeActor source)
        {
            if (source == null || source != ActiveActor || IsBusy ||
                source.State.form != SlimeForm.Solid || source.State.size != SlimeSize.Large ||
                _actors.Count >= 2 || !TryGetSplitterCell(source, out Vector3Int cell) ||
                !_gameplay.CanEnter(source, cell, source.Mover.Facing, false)) return false;
            CollectSplitCells(source, cell);
            return _splitCells.Count > 0;
        }

        void CollectSplitCells(SlimeActor source, Vector3Int center)
        {
            _splitCells.Clear();
            int facing = _gameplay.GetDirection(center);
            Vector3Int front = GridDirection.ToVector(facing);
            Vector3Int right = GridDirection.ToVector(GridDirection.Right(facing));
            AddSplitCell(source, center + front - right);
            AddSplitCell(source, center + front + right);
            // 1 = front, then front-right, right, back-right, back, back-left, left, front-left.
            for (int side = 0; side < 4 && _splitCells.Count < 2; side++)
            {
                int direction = (facing + side) & 3;
                Vector3Int offset = GridDirection.ToVector(direction);
                AddSplitCell(source, center + offset);
                AddSplitCell(source, center + offset + GridDirection.ToVector(GridDirection.Right(direction)));
            }
        }

        void AddSplitCell(SlimeActor source, Vector3Int cell)
        {
            if (_splitCells.Count >= 2 || _splitCells.Contains(cell) || FindAt(cell) != null && FindAt(cell) != source)
                return;
            GameplayBlockKind? kind = _gameplay.GetProfileAt(cell)?.Kind;
            if (kind == GameplayBlockKind.DamageFloor || kind == GameplayBlockKind.Splitter) return;
            if (_gameplay.CanLand(source, cell)) _splitCells.Add(cell);
        }

        public bool TrySplit(SlimeActor source)
        {
            if (!CanSplit(source)) return false;
            TryGetSplitterCell(source, out _splitCell);
            _splitSource = source;
            _splitOriginalCell = source.Mover.Cell;
            _splitStart = source.transform.position;
            _splitFacing = _gameplay.GetDirection(_splitCell);
            _splitElapsed = 0f;
            source.Mover.BeginInteractionMotion();
            BuildCutParticles(source.BodyColor);
            RefreshControl();
            return true;
        }

        public void AdvanceSplitAnimation(float deltaTime)
        {
            if (_splitSource == null || Time.timeScale == 0f) return;
            _splitElapsed += Mathf.Max(0f, deltaTime);
            float duration = Mathf.Max(.1f, splitterCutDuration);
            float cut = Mathf.Clamp01(_splitElapsed / duration);
            Vector3 center = SplitCenter(_splitCell);
            Vector3 right = GridDirection.ToVector(GridDirection.Right(_splitFacing));

            if (cut >= .25f && !_cutParticles.isPlaying && _splitSecond == null)
            {
                _cutParticles.Play();
                _cutParticles.Emit(28);
            }
            if (cut >= .75f && _splitSecond == null)
            {
                // Recheck after the slow entry: a door or map change may have removed a landing.
                CollectSplitCells(_splitSource, _splitCell);
                if (_splitCells.Count == 0)
                {
                    SlimeActor source = _splitSource;
                    Vector3Int original = _splitOriginalCell;
                    CancelSplitAnimation();
                    source.Mover.Teleport(original, _splitFacing);
                    RefreshControl();
                    return;
                }
                var small = _splitSource.State.Copy();
                small.size = SlimeSize.Small;
                _splitSource.SetState(small.Copy());
                _splitSecond = SpawnActor(small.Copy(), _splitCell, _splitFacing);
                _actors.Add(_splitSecond);
                _splitSecond.Mover.BeginInteractionMotion();
                RefreshControl();
                _cutParticles.Emit(52);
            }

            // Reach the saw center before creating either half, then accelerate outwards.
            Vector3 entering = Vector3.Lerp(_splitStart, center, Mathf.Clamp01(cut / .75f));
            float split = Mathf.InverseLerp(.75f, 1f, cut);
            float separation = _splitSecond != null
                ? split * split * .3f * _cellSize : 0f;
            _splitSource.transform.position = entering - right * separation;
            if (_splitSecond != null) _splitSecond.transform.position = entering + right * separation;
            _cutParticles.transform.position = entering + Vector3.up * (.35f * _cellSize);
            if (cut < 1f) return;

            if (_cutParticles.isPlaying) _cutParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            float hop = Mathf.Clamp01((_splitElapsed - duration) / Mathf.Max(.1f, splitterJumpDuration));
            Vector3Int firstCell = _splitCells[0];
            Vector3Int secondCell = _splitCells.Count > 1 ? _splitCells[1] : _splitCell;
            Vector3 lift = Vector3.up * (Mathf.Sin(hop * Mathf.PI) * splitterJumpHeight * _cellSize);
            float travel = hop * hop;
            _splitSource.transform.position = Vector3.Lerp(center - right * (.3f * _cellSize), SplitCenter(firstCell), travel) + lift;
            _splitSecond.transform.position = Vector3.Lerp(center + right * (.3f * _cellSize), SplitCenter(secondCell), travel) + lift;
            if (hop < 1f) return;

            SlimeActor first = _splitSource;
            SlimeActor second = _splitSecond;
            first.Mover.Teleport(firstCell, _splitFacing);
            second.Mover.Teleport(secondCell, _splitFacing);
            CancelSplitAnimation(true);
            _activeIndex = _actors.IndexOf(first);
            RefreshControl();
            OnCellEntered(first.Mover, firstCell);
            OnCellEntered(second.Mover, secondCell);
        }

        Vector3 SplitCenter(Vector3Int cell) => new Vector3(cell.x + _cellSize * .5f, cell.y, cell.z + _cellSize * .5f);

        void CancelSplitAnimation(bool keepParticleTail = false)
        {
            _splitSource = null;
            _splitSecond = null;
            _splitElapsed = 0f;
            if (!keepParticleTail)
            {
                foreach (ParticleSystem tail in _cutParticleTails)
                    if (tail != null) DestroyCutParticles(tail);
                _cutParticleTails.Clear();
            }
            if (_cutParticles != null)
            {
                if (keepParticleTail)
                {
                    _cutParticles.gameObject.name = "Splitter Slime Droplet Tail";
                    var main = _cutParticles.main;
                    if (Application.isPlaying) main.stopAction = ParticleSystemStopAction.Destroy;
                    _cutParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    _cutParticleTails.RemoveAll(tail => tail == null);
                    _cutParticleTails.Add(_cutParticles);
                }
                else DestroyCutParticles(_cutParticles);
                _cutParticles = null;
            }
        }

        static void DestroyCutParticles(ParticleSystem particles)
        {
            particles.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(particles.gameObject);
            else DestroyImmediate(particles.gameObject);
        }

        void BuildCutParticles(Color color)
        {
            var droplets = new GameObject("Splitter Slime Droplets");
            droplets.transform.SetParent(transform, false);
            droplets.transform.position = _splitStart + Vector3.up * (.35f * _cellSize);
            droplets.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            _cutParticles = droplets.AddComponent<ParticleSystem>();
            _cutParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _cutParticles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 320;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = color;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(.13f * _cellSize, .24f * _cellSize);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f * _cellSize, 2.4f * _cellSize);
            main.gravityModifier = .12f;
            var emission = _cutParticles.emission;
            emission.rateOverTime = 180f;
            var shape = _cutParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 65f;
            shape.radius = .12f * _cellSize;
            var fade = _cutParticles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var shrink = _cutParticles.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .2f));
            var renderer = droplets.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("SlimeFireParticle");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
