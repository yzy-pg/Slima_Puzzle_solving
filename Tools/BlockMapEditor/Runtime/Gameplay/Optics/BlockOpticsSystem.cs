using System.Collections.Generic;
using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>独立光线追踪：每束光单独计算，交叉时互不影响。</summary>
    public sealed class BlockOpticsSystem : MonoBehaviour
    {
        BlockGameplayController _gameplay;
        Transform _beamRoot;
        bool _dirty = true;

        public void Initialize(BlockGameplayController gameplay)
        {
            _gameplay = gameplay;
            MarkDirty();
        }

        public void MarkDirty() => _dirty = true;

        public void RefreshIfNeeded()
        {
            if (!_dirty || _gameplay == null) return;
            _dirty = false;
            ClearBeams();
            _gameplay.BeginReceiverEvaluation();

            foreach (GameplayBlockCell emitter in _gameplay.Enumerate(GameplayBlockKind.BeamEmitter))
                Trace(emitter);
        }

        void Trace(GameplayBlockCell emitter)
        {
            int direction = _gameplay.GetDirection(emitter.Cell);
            Vector3Int cell = emitter.Cell;
            BeamColor color = emitter.Profile.BeamColor;
            int range = Mathf.Max(1, BlockGameplaySettings.Current.beamRange);

            for (int i = 0; i < range; i++)
            {
                Vector3Int next = cell + GridDirection.ToVector(direction);
                DrawSegment(cell, next, color);

                SlimeActor actor = _gameplay.GetActorAt(next);
                if (actor != null && actor.State.form == SlimeForm.Solid)
                {
                    if (actor.State.element == SlimeElement.Blue && actor.State.opticsMode != BlueOpticsMode.PassThrough)
                    {
                        direction = actor.State.opticsMode == BlueOpticsMode.ReflectLeft
                            ? GridDirection.Left(direction)
                            : GridDirection.Right(direction);
                    }
                    else
                    {
                        color = SlimeRules.MixBeam(color, actor.State.element);
                    }
                }

                GameplayBlockProfile profile = _gameplay.GetProfileAt(next);
                if (profile != null && profile.Kind == GameplayBlockKind.BeamReceiver)
                {
                    if (SlimeRules.ReceiverAccepts(profile.BeamColor, color))
                        _gameplay.ActivateReceiver(next);
                    break;
                }

                if (_gameplay.IsBeamBlocked(next)) break;
                cell = next;
            }
        }

        void DrawSegment(Vector3Int from, Vector3Int to, BeamColor color)
        {
            if (_beamRoot == null)
            {
                var container = new GameObject("GameplayBeams");
                container.transform.SetParent(transform, false);
                _beamRoot = container.transform;
            }

            var beam = new GameObject("BeamSegment");
            beam.transform.SetParent(_beamRoot, false);
            LineRenderer line = beam.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = BlockGameplaySettings.Current.beamWidth;
            line.endWidth = BlockGameplaySettings.Current.beamWidth;
            line.numCapVertices = 4;
            line.SetPosition(0, Center(from));
            line.SetPosition(1, Center(to));

            Color beamColor = ToColor(color);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            line.sharedMaterial = new Material(shader) { color = beamColor };
            line.startColor = beamColor;
            line.endColor = beamColor;
        }

        void ClearBeams()
        {
            if (_beamRoot == null) return;
            if (Application.isPlaying) Destroy(_beamRoot.gameObject);
            else DestroyImmediate(_beamRoot.gameObject);
            _beamRoot = null;
        }

        static Vector3 Center(Vector3Int cell) =>
            new Vector3(cell.x + 0.5f, cell.y + 0.52f, cell.z + 0.5f);

        public static Color ToColor(BeamColor color)
        {
            switch (color)
            {
                case BeamColor.Red: return new Color(1f, 0.16f, 0.10f);
                case BeamColor.Blue: return new Color(0.12f, 0.48f, 1f);
                case BeamColor.Purple: return new Color(0.68f, 0.18f, 1f);
                default: return new Color(1f, 0.96f, 0.82f);
            }
        }
    }
}
