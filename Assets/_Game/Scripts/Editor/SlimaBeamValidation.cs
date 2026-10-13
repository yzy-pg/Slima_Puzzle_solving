using System;
using System.IO;
using System.Reflection;
using BlockMap;
using BlockMap.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.Rendering.EditorTools
{
    [InitializeOnLoad]
    public static class SlimaBeamValidation
    {
        const string Key = "Slima.BeamValidation";
        const string Output = "Logs/Beam";
        static Camera _camera;
        static RenderTexture _target;
        static Texture2D _readback;
        static Color[] _firstFrame;
        static BlockGameplayController _gameplay;
        static double _started;
        static int _particleFrames;

        static SlimaBeamValidation()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += ValidatePlay;
        }

        public static void Run()
        {
            try { RunValidation(); }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }

        static void RunValidation()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch-only GPU validation.");
            Directory.CreateDirectory(Output);
            var material = Resources.Load<Material>("BlockMapBeam");
            Check(material != null && !ShaderUtil.ShaderHasError(material.shader), "Beam shader failed to import.");
            Check(Resources.Load<Material>("BlockMapBeamParticle") != null, "Missing particle material.");
            Check(material.GetTexture("_NoiseTex") != null, "Missing flowing noise texture.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var originalGraphics = GraphicsSettings.renderPipelineAsset;
            var originalQuality = QualitySettings.renderPipeline;
            var renderer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP-HighFidelity-Renderer.asset"));
            renderer.rendererFeatures.Clear();
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.msaaSampleCount = 1;
            var quiet = new Material(material);
            quiet.SetFloat("_NoiseStrength", 0);
            quiet.SetFloat("_EdgeNoiseStrength", 0);
            quiet.SetFloat("_NoiseTransparency", 0);
            var glowTest = new Material(Resources.Load<Material>("BlockMapBeamGlow"));
            Check(glowTest.GetTexture("_FalloffTex") != null, "Missing glow gradient falloff texture.");
            glowTest.SetTexture("_NoiseTex", Texture2D.blackTexture);
            glowTest.SetFloat("_NoiseTransparency", 1);
            float originalWidth = BlockGameplaySettings.Current.beamWidth;
            try
            {
                GraphicsSettings.renderPipelineAsset = pipeline;
                QualitySettings.renderPipeline = pipeline;
                SetupCamera(new Vector3(3, 8, 3.5f), 4);
                const float previewWidth = 0.5f;
                BlockGameplaySettings.Current.beamWidth = previewWidth;
                var optics = new GameObject("Beam material preview").AddComponent<BlockOpticsSystem>();
                var draw = typeof(BlockOpticsSystem).GetMethod("DrawSegment", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int row = 0; row < 4; row++)
                    for (int x = 0; x < 5; x++)
                        draw.Invoke(optics, new object[] { new Vector3Int(x, 0, row * 2), new Vector3Int(x + 1, 0, row * 2), (BeamColor)row });
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "BeamGlow") line.enabled = false;
                    else line.sharedMaterial = quiet;
                Capture(null);
                Capture("gradient");
                var center = new Vector3(2.2f, 0.52f, 0.5f);
                CheckCoreColor(Sample(center), BeamColor.White);
                for (int row = 0; row < 4; row++)
                    foreach (int side in new[] { -1, 1 })
                    {
                        Vector3 edgePosition = new Vector3(center.x, center.y, row * 2 + 0.5f + side * previewWidth * 0.42f);
                        Color edge = Sample(edgePosition);
                        if (QualitySettings.activeColorSpace == ColorSpace.Linear) edge = edge.gamma;
                        Color.RGBToHSV(edge, out float hue, out float saturation, out float value);
                        Color.RGBToHSV(BlockOpticsSystem.ToColor((BeamColor)row), out float expectedHue, out _, out _);
                        Check(Mathf.Abs(value - 0.5f) < 0.025f && saturation > 0.97f
                            && Mathf.Abs(Mathf.DeltaAngle(hue * 360, expectedHue * 360)) < 2,
                            "Beam edge should match its hue with HSV saturation=1, value=0.5: " + edge);
                    }
                Check(Sample(center + Vector3.forward * previewWidth * 0.6f).r > 0.15f, "Beam spills outside its width.");
                Check(Sample(new Vector3(2.5f, 0.52f, 0.5f)).r > 0.95f, "Grid join has a dark seam.");
                float background = Sample(center + Vector3.forward * 0.28f).r;
                quiet.SetTexture("_NoiseTex", Texture2D.blackTexture);
                quiet.SetFloat("_NoiseTransparency", 1);
                quiet.SetFloat("_CoreMinOpacity", 0);
                Capture("body-noise-transparent");
                Check(Mathf.Abs(Sample(center).r - background) < 0.02f, "Body noise transparency does not reveal the background.");
                quiet.SetFloat("_NoiseTransparency", 0);
                Capture("body-noise-opaque");
                Check(Sample(center).r > 0.95f, "Body noise transparency cannot be turned off.");
                quiet.SetFloat("_NoiseTransparency", 1);
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "BeamGlow") { line.enabled = true; line.sharedMaterial = glowTest; }
                Capture("glow-noise-transparent");
                Check(Mathf.Abs(Sample(center + Vector3.forward * 0.28f).r - background) < 0.02f,
                    "Glow noise transparency does not mask the glow.");
                glowTest.SetFloat("_NoiseTransparency", 0);
                Capture("glow-noise-opaque");
                Check(Sample(center + Vector3.forward * 0.28f).r > background + 0.05f,
                    "Glow noise transparency cannot be turned off.");
                float nearGlow = Sample(center + Vector3.forward * 0.3f).r - background;
                float middleGlow = Sample(center + Vector3.forward * 0.6f).r - background;
                float farGlow = Sample(center + Vector3.forward * 0.85f).r - background;
                Check(nearGlow > middleGlow + 0.03f && middleGlow > farGlow + 0.02f,
                    "Glow should fade progressively away from its center.");
                glowTest.SetTexture("_FalloffTex", Texture2D.whiteTexture);
                Capture("glow-without-gradient");
                float unfadedGlow = Sample(center + Vector3.forward * 0.85f).r - background;
                Check(farGlow < unfadedGlow * 0.3f, "The gradient texture did not fade the outer glow.");
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "BeamGlow") line.sharedMaterial = Resources.Load<Material>("BlockMapBeamGlow");
                    else line.sharedMaterial = material;
                Capture("flow-preview");
                for (int row = 0; row < 4; row++)
                    for (float x = 0.75f; x < 5.25f; x += 0.125f)
                    {
                        CheckCoreColor(Sample(new Vector3(x, 0.52f, row * 2 + 0.5f)), (BeamColor)row);
                    }
                // Validate framebuffer blocks at the material's pixel size, including perspective.
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "BeamSegment") line.enabled = false;
                CheckScreenBlocks(Capture("screen-grid"), background, "orthographic");
                _camera.orthographicSize = 2.8f;
                CheckScreenBlocks(Capture("screen-grid-zoom"), background, "zoom");
                _camera.orthographic = false;
                _camera.transform.position = new Vector3(4.5f, 8, -3);
                _camera.transform.LookAt(new Vector3(3, 0.52f, 3.5f));
                CheckScreenBlocks(Capture("screen-grid-perspective"), background, "perspective");
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "BeamSegment") line.enabled = true;
                Capture("perspective-preview");
                _camera.orthographic = true; _camera.orthographicSize = 4;
                _camera.transform.SetPositionAndRotation(new Vector3(3, 8, 3.5f),
                    Quaternion.LookRotation(Vector3.down, Vector3.forward));
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = center + Vector3.up;
                wall.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);
                Capture("occluded");
                Check(Sample(center).r < 0.85f, "Beam drew through an opaque wall.");
                Object.DestroyImmediate(wall);
                ValidateOutlineOrdering(renderer, optics);
                Check(!ShaderUtil.ShaderHasError(material.shader), "Beam shader failed GPU compilation.");
                Debug.Log("[Slima Beam Validation] GPU PASS: saturated matching-hue cores, 2x2 screen blocks, background outlines stay behind beams, foreground occlusion, gradient and noise transparency.");
            }
            finally
            {
                BlockGameplaySettings.Current.beamWidth = originalWidth;
                GraphicsSettings.renderPipelineAsset = originalGraphics;
                QualitySettings.renderPipeline = originalQuality;
                Object.DestroyImmediate(quiet);
                Object.DestroyImmediate(glowTest);
                Object.DestroyImmediate(pipeline);
                Object.DestroyImmediate(renderer);
                ReleaseCapture();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Key, true);
            SessionState.SetFloat(Key + ".Deadline", (float)EditorApplication.timeSinceStartup + 60);
            EditorApplication.update += ValidatePlay;
            EditorApplication.isPlaying = true;
        }

        static void ValidatePlay()
        {
            try
            {
                Check(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + ".Deadline", 0), "Play validation timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.timeSinceLevelLoad < 0.5f) return;
                if (_gameplay == null)
                {
                    var root = new GameObject("Beam play validation");
                    var world = root.AddComponent<BlockWorld>();
                    for (int z = 0; z < 7; z++) world.Set(new Vector3Int(0, 0, z), BlockLibrary.IndexOf("Lab_WallBrick1"));
                    world.Set(new Vector3Int(0, 1, 0), BlockLibrary.IndexOf("Lab_Laser_01"));
                    world.Set(new Vector3Int(0, 1, 5), BlockLibrary.IndexOf("Lab_Laser_04"));
                    _gameplay = root.AddComponent<BlockGameplayController>();
                    _gameplay.Initialize(world);
                    root.GetComponent<BlockOpticsSystem>().RefreshIfNeeded();
                    foreach (LineRenderer line in root.GetComponentsInChildren<LineRenderer>())
                        if (line.name == "BeamSegment") Check(Mathf.Abs(line.startWidth - 0.1f) < 0.001f, "Beam width was not increased.");
                        else if (line.name == "BeamGlow") Check(Mathf.Abs(line.startWidth - 0.5f) < 0.001f, "Glow width was not increased.");
                    Check(root.GetComponentsInChildren<BlockBeamParticles>().Length == 1, "Expected one particle system per emitter.");
                    Check(root.GetComponentsInChildren<LineRenderer>().Length >= 5, "Emitter did not trace its beam.");
                    foreach (MeshRenderer view in root.GetComponentsInChildren<MeshRenderer>()) view.enabled = false;
                    root.GetComponentInChildren<ParticleSystemRenderer>().enabled = false;
                    SetupCamera(new Vector3(0.5f, 8, 3), 3.5f);
                    Capture(null);
                    _firstFrame = Capture("play-start");
                    _started = EditorApplication.timeSinceStartup;
                    return;
                }
                var particles = _gameplay.GetComponentInChildren<ParticleSystem>();
                Check(particles != null && particles.main.maxParticles == 24, "Particle cap is missing.");
                if (particles.particleCount > 0) _particleFrames++;
                Check(particles.particleCount <= 16, "Particle emission is too dense.");
                if (EditorApplication.timeSinceStartup - _started < 2) return;
                var frame = Capture("play-flow");
                int changed = 0;
                for (int i = 0; i < frame.Length; i++)
                    if (Mathf.Abs(frame[i].r - _firstFrame[i].r) > 0.005f) changed++;
                Check(changed > 10, "Body/glow noise did not move over time.");
                Check(_particleFrames > 0, "No random particles emitted.");
                var live = new ParticleSystem.Particle[24];
                int count = particles.GetParticles(live);
                Check(count >= 3, "Pixel particles are not visible enough.");
                var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
                Check(particleRenderer.renderMode == ParticleSystemRenderMode.Billboard && particles.noise.enabled,
                    "Particles should be square billboards with random wandering.");
                for (int i = 0; i < count; i++)
                {
                    Check(Vector3.Distance(live[i].position, new Vector3(0.5f, 1.52f, 1.08f)) < 0.8f,
                        "Pixel particle moved too far from the emitter.");
                }
                particles.GetComponent<ParticleSystemRenderer>().enabled = true;
                var sparks = Capture("play-particles");
                int sparkPixels = 0;
                for (int i = 0; i < sparks.Length; i++)
                    if (sparks[i].r - frame[i].r > 0.03f) sparkPixels++;
                Check(sparkPixels > 30, "Particles do not visibly brighten the rendered image.");
                _camera.transform.position = new Vector3(4.5f, 6, 8);
                _camera.transform.LookAt(new Vector3(0.5f, 1.5f, 2.5f));
                foreach (MeshRenderer view in _gameplay.GetComponentsInChildren<MeshRenderer>()) view.enabled = true;
                Capture("emitter-sparks");
                var optics = _gameplay.GetComponent<BlockOpticsSystem>();
                optics.MarkDirty(); optics.RefreshIfNeeded();
                Check(_gameplay.GetComponentsInChildren<BlockBeamParticles>().Length == 1, "Beam rebuild duplicated particles.");
                Debug.Log("[Slima Beam Validation] PLAY PASS: moving body/glow noise, visible wandering pixel particles and clean rebuild; noise pixels=" + changed + ", particle pixels=" + sparkPixels);
                Finish(0);
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }

        static void SetupCamera(Vector3 position, float size)
        {
            _camera = new GameObject("Beam validation camera").AddComponent<Camera>();
            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            _camera.orthographic = true; _camera.orthographicSize = size; _camera.aspect = 2;
            _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
            _camera.allowHDR = true; _camera.allowMSAA = false; _camera.enabled = false;
            _camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            _target = new RenderTexture(1024, 512, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _readback = new Texture2D(1024, 512, TextureFormat.RGBAFloat, false, true);
        }

        static Color[] Capture(string name)
        {
            RenderPipeline.SubmitRenderRequest(_camera, new UniversalRenderPipeline.SingleCameraRequest { destination = _target });
            var previous = RenderTexture.active; RenderTexture.active = _target;
            _readback.ReadPixels(new Rect(0, 0, 1024, 512), 0, 0); _readback.Apply(); RenderTexture.active = previous;
            var pixels = _readback.GetPixels();
            if (name != null)
            {
                var preview = new Texture2D(1024, 512, TextureFormat.RGBA32, false);
                var colors = new Color[pixels.Length];
                for (int i = 0; i < colors.Length; i++) colors[i] = pixels[i].gamma;
                preview.SetPixels(colors); preview.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", preview.EncodeToPNG());
                Object.DestroyImmediate(preview);
            }
            return pixels;
        }

        static Color Sample(Vector3 world)
        {
            Vector3 view = _camera.WorldToViewportPoint(world);
            return _readback.GetPixel((int)(view.x * 1024), (int)(view.y * 512));
        }

        static void CheckCoreColor(Color color, BeamColor beamColor)
        {
            float peak = Mathf.Max(color.r, color.g, color.b);
            Check(peak > 0.95f, "The bright core is too dark: " + color);
            Color normalized = color / peak;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) normalized = normalized.gamma;
            Color.RGBToHSV(normalized, out float hue, out float saturation, out _);
            Color.RGBToHSV(BlockOpticsSystem.ToColor(beamColor), out float expectedHue, out _, out _);
            Check(saturation > 0.97f && Mathf.Abs(Mathf.DeltaAngle(hue * 360, expectedHue * 360)) < 2,
                "Core should be saturated and share the beam hue: " + color);
        }

        static void ValidateOutlineOrdering(UniversalRendererData source, BlockOpticsSystem optics)
        {
            var lines = optics.GetComponentsInChildren<LineRenderer>();
            var originals = new Material[lines.Length];
            var fixedCore = new Material(Resources.Load<Material>("BlockMapBeam"));
            var fixedGlow = new Material(Resources.Load<Material>("BlockMapBeamGlow"));
            foreach (Material fixedMaterial in new[] { fixedCore, fixedGlow })
            {
                fixedMaterial.SetFloat("_NoiseStrength", 0);
                fixedMaterial.SetFloat("_NoiseTransparency", 0);
                fixedMaterial.SetFloat("_EdgeNoiseStrength", 0);
            }
            for (int i = 0; i < lines.Length; i++)
            {
                originals[i] = lines[i].sharedMaterial;
                lines[i].sharedMaterial = lines[i].name == "BeamSegment" ? fixedCore : fixedGlow;
            }
            var originalGraphics = GraphicsSettings.renderPipelineAsset;
            var originalQuality = QualitySettings.renderPipeline;
            var data = Object.Instantiate(source);
            data.rendererFeatures.Clear();
            var feature = ScriptableObject.CreateInstance<SlimaToonOutlineFeature>();
            // Old saved renderer assets use 550. They must work without rewriting the assets.
            feature.settings.passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            feature.settings.crispSpecials = false;
            feature.settings.protectNameFilter = "";
            feature.settings.protectMaterialFilter = "";
            feature.settings.pixelSize = 4;
            feature.settings.pixelateAmount = 1;
            feature.settings.maskGroups.Clear();
            feature.settings.maskGroups.Add(new SlimaToonOutlineFeature.MaskGroup
                { name = "地面", materialContains = "_Floor", thickness = 6 });
            data.rendererFeatures.Add(feature);
            var pipeline = UniversalRenderPipelineAsset.Create(data);
            pipeline.msaaSampleCount = 1;
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Regression_Floor" };
            floorMaterial.SetColor("_BaseColor", new Color(0.65f, 0.65f, 0.65f));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Outline background";
            floor.transform.position = new Vector3(3, 0, 0.5f);
            floor.transform.localScale = new Vector3(1, 0.2f, 1);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            Vector3 left = new Vector3(2.44f, 0.52f, 0.5f);
            Vector3 right = new Vector3(3.56f, 0.52f, 0.5f);
            try
            {
                Capture("outline-baseline");
                Color baselineLeft = Sample(left), baselineRight = Sample(right);
                GraphicsSettings.renderPipelineAsset = pipeline;
                QualitySettings.renderPipeline = pipeline;
                Capture(null);
                Capture("outline-behind-beam");
                Color outlinedLeft = Sample(left), outlinedRight = Sample(right);
                Check(Mathf.Abs(outlinedLeft.r - baselineLeft.r) < 0.05f
                    && Mathf.Abs(outlinedLeft.g - baselineLeft.g) < 0.05f
                    && Mathf.Abs(outlinedRight.r - baselineRight.r) < 0.05f,
                    "Background outline or scene pixelation covered the beam core.");
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>()) line.enabled = false;
                int outlinePixels = 0;
                foreach (Color p in Capture("outline-scene-only"))
                    if (Mathf.Max(p.r, p.g, p.b) < 0.03f) outlinePixels++;
                Check(outlinePixels > 100, "The scene outline itself is missing: " + outlinePixels);
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>()) line.enabled = true;
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "BeamSegment") line.enabled = false;
                CheckScreenBlocks(Capture("outline-detail-grid"), 0.214f, "outlined scene");
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "BeamSegment") line.enabled = true;
                // The same object in front of the beam must still occlude it normally.
                floor.transform.position = new Vector3(3, 1, 0.5f);
                Capture("outline-foreground-occlusion");
                Check(Sample(new Vector3(3, 0.52f, 0.5f)).r < 0.85f,
                    "Moving the outline pass broke foreground geometry occlusion.");
                Debug.Log("[Slima Beam Validation] OUTLINE PASS: old event 550 is clamped before transparents; background outlines and scene pixelation do not cover beams; foreground geometry still occludes.");
            }
            finally
            {
                for (int i = 0; i < lines.Length; i++) lines[i].sharedMaterial = originals[i];
                Object.DestroyImmediate(fixedCore); Object.DestroyImmediate(fixedGlow);
                foreach (LineRenderer line in optics.GetComponentsInChildren<LineRenderer>())
                    if (line.name == "BeamSegment") line.enabled = true;
                GraphicsSettings.renderPipelineAsset = originalGraphics;
                QualitySettings.renderPipeline = originalQuality;
                Object.DestroyImmediate(floor); Object.DestroyImmediate(floorMaterial);
                Object.DestroyImmediate(pipeline); Object.DestroyImmediate(data);
                feature.Dispose(); Object.DestroyImmediate(feature);
            }
        }

        static void CheckScreenBlocks(Color[] pixels, float background, string view)
        {
            int block = Mathf.RoundToInt(Resources.Load<Material>("BlockMapBeamGlow").GetFloat("_ScreenPixelSize"));
            int filled = 0, varying = 0;
            for (int y = 0; y < 512; y += block)
                for (int x = 0; x < 1024; x += block)
                {
                    Color first = pixels[y * 1024 + x];
                    float minimum = float.MaxValue, difference = 0;
                    for (int dy = 0; dy < block; dy++)
                        for (int dx = 0; dx < block; dx++)
                        {
                            Color p = pixels[(y + dy) * 1024 + x + dx];
                            minimum = Mathf.Min(minimum, (p.r + p.g + p.b) / 3);
                            difference = Mathf.Max(difference, Mathf.Abs(p.r - first.r),
                                Mathf.Abs(p.g - first.g), Mathf.Abs(p.b - first.b));
                        }
                    // Ignore blocks clipped by the line mesh silhouette.
                    if (minimum <= background + 0.025f) continue;
                    filled++;
                    if (difference > 0.004f) varying++;
                }
            Check(filled > 25 && varying <= Mathf.Max(1, filled / 100),
                view + " screen blocks are not uniform: " + varying + "/" + filled);
            Debug.Log("[Slima Beam Validation] " + view + " " + block + "x" + block + " screen grid: " + filled + " blocks, varying=" + varying);
        }

        static void ReleaseCapture()
        {
            Object.DestroyImmediate(_readback); _target.Release(); Object.DestroyImmediate(_target);
        }

        static void Finish(int code)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= ValidatePlay;
            EditorApplication.Exit(code);
        }

        static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); }
    }
}
