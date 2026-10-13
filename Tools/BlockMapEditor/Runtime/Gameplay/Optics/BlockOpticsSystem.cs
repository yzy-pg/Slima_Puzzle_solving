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
            EnsureBeamRoot();
            var particleObject = new GameObject("BeamParticles");
            particleObject.transform.SetParent(_beamRoot, false);
            var particles = particleObject.AddComponent<BlockBeamParticles>();
            particles.Initialize();
            int direction = _gameplay.GetDirection(emitter.Cell);
            int range = Mathf.Max(1, BlockGameplaySettings.Current.beamRange);
            var visited = new HashSet<string>();
            TraceRay(emitter.Cell, direction, emitter.Profile.BeamColor, range, visited, particles);
        }

        void TraceRay(Vector3Int origin, int direction, BeamColor color, int range,
            HashSet<string> visited, BlockBeamParticles particles)
        {
            Vector3Int cell = origin;
            for (int i = 0; i < range; i++)
            {
                Vector3Int next = cell + GridDirection.ToVector(direction);
                string visitKey = $"{next.x}:{next.y}:{next.z}:{direction}";
                if (!visited.Add(visitKey)) return;
                DrawSegment(cell, next, color);
                particles.AddSegment(Center(cell), Center(next), ToColor(color));

                SlimeActor actor = _gameplay.GetActorAt(next);
                if (actor != null && actor.State.form == SlimeForm.Solid && actor.State.element == SlimeElement.Blue)
                {
                    // Blue slime splits incoming light into two perpendicular blue rays.
                    TraceRay(next, GridDirection.Left(direction), BeamColor.Blue, range - i - 1, visited, particles);
                    TraceRay(next, GridDirection.Right(direction), BeamColor.Blue, range - i - 1, visited, particles);
                    return;
                }
                if (actor != null && actor.State.form == SlimeForm.Solid)
                    color = SlimeRules.MixBeam(color, actor.State.element);

                GameplayBlockProfile profile = _gameplay.GetProfileAt(next);
                if (profile != null && profile.Kind == GameplayBlockKind.BeamReceiver)
                {
                    if (SlimeRules.ReceiverAccepts(profile.BeamColor, color))
                        _gameplay.ActivateReceiver(next, (direction + 2) & 3);
                    break;
                }

                if (_gameplay.IsBeamBlocked(next)) break;
                cell = next;
            }
        }

        void EnsureBeamRoot()
        {
            if (_beamRoot == null)
            {
                var container = new GameObject("GameplayBeams");
                container.transform.SetParent(transform, false);
                _beamRoot = container.transform;
            }
        }

        void DrawSegment(Vector3Int from, Vector3Int to, BeamColor color)
        {
            EnsureBeamRoot();
            float width = BlockGameplaySettings.Current.beamWidth;
            DrawLine("BeamSegment", from, to, color, width, Resources.Load<Material>("BlockMapBeam"));
            Material glow = Resources.Load<Material>("BlockMapBeamGlow");
            DrawLine("BeamGlow", from, to, color, width * glow.GetFloat("_GlowWidth"), glow);
        }

        void DrawLine(string name, Vector3Int from, Vector3Int to, BeamColor color, float width, Material material)
        {
            var beam = new GameObject(name);
            beam.transform.SetParent(_beamRoot, false);
            LineRenderer line = beam.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 0;
            line.textureMode = LineTextureMode.Tile;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.SetPosition(0, Center(from));
            line.SetPosition(1, Center(to));

            Color beamColor = ToColor(color);
            line.sharedMaterial = material;
            line.startColor = beamColor;
            line.endColor = beamColor;
            var properties = new MaterialPropertyBlock();
            properties.SetVector("_FlowDirection", (Vector3)(to - from));
            Color.RGBToHSV(beamColor, out float hue, out _, out _);
            Color bandColor = Color.HSVToRGB(hue, 1f, 0.5f);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) bandColor = bandColor.linear;
            properties.SetVector("_BandColor", new Vector4(bandColor.r, bandColor.g, bandColor.b, 1));
            Color coreColor = Color.HSVToRGB(hue, 1f, 1f);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) coreColor = coreColor.linear;
            properties.SetVector("_CoreColor", new Vector4(coreColor.r, coreColor.g, coreColor.b, 1));
            line.SetPropertyBlock(properties);
        }

        void ClearBeams()
        {
            if (_beamRoot == null) return;
            _beamRoot.gameObject.SetActive(false);
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
