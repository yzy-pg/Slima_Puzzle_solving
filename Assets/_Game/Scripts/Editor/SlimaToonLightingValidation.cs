using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.Rendering.EditorTools
{
    // Batch-only GPU regression check. Creates a temporary scene; never saves a user scene.
    public static class SlimaToonLightingValidation
    {
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this check in Unity batch mode.");
            AssetDatabase.Refresh();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Game/Art/Shaders/SlimaToon.shader");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Slima Toon shader failed to import.");
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (!data.rendererFeatures.Exists(f => f is SlimaToonLightingFeature))
                    throw new Exception("Lighting feature missing: " + data.name);
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var oldGraphics = GraphicsSettings.renderPipelineAsset;
            var oldQuality = QualitySettings.renderPipeline;
            int oldAntiAliasing = QualitySettings.antiAliasing;
            var renderer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/Settings/URP-Balanced-Renderer.asset"));
            renderer.rendererFeatures.Clear();
            var feature = ScriptableObject.CreateInstance<SlimaToonLightingFeature>();
            renderer.rendererFeatures.Add(feature);
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1;
            pipeline.maxAdditionalLightsCount = 8;
            var pipelineSO = new SerializedObject(pipeline);
            pipelineSO.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1;
            pipelineSO.ApplyModifiedPropertiesWithoutUndo();
            var target = new RenderTexture(600, 200, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(600, 200, TextureFormat.RGBAFloat, false, true);
            var material = new Material(shader);
            try
            {
                GraphicsSettings.renderPipelineAsset = pipeline;
                QualitySettings.renderPipeline = pipeline;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientIntensity = 0;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2();
                RenderSettings.fog = false;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.localScale = new Vector3(12, 4, 1);
                quad.GetComponent<MeshRenderer>().sharedMaterial = material;
                var mesh = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
                var vertexColors = new Color[mesh.vertexCount];
                for (int i = 0; i < vertexColors.Length; i++) vertexColors[i] = Color.white;
                mesh.colors = vertexColors;
                quad.GetComponent<MeshFilter>().sharedMesh = mesh;
                material.SetColor("_ShadowColor", Color.black);
                material.SetColor("_AmbientColor", Color.clear);
                material.SetFloat("_FallbackLight", 0);
                material.SetFloat("_OutlineWidth", 0);
                material.SetFloat("_NoiseStrength", 0);

                var camera = new GameObject("Lighting validation camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 2;
                camera.aspect = 3;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.enabled = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                var light = new GameObject("Lighting validation point light").AddComponent<Light>();
                light.type = LightType.Point;
                light.transform.position = new Vector3(-2, 0, -1);
                light.range = 8;
                light.intensity = 3;
                light.shadows = LightShadows.None;
                var control = light.gameObject.AddComponent<SlimaToonLight>();

                Color[] Capture(string name)
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 600, 200), 0, 0);
                    readback.Apply();
                    RenderTexture.active = previous;
                    Directory.CreateDirectory("Logs/ToonLighting");
                    Color[] pixels = readback.GetPixels();
                    var preview = new Texture2D(600, 200, TextureFormat.RGBA32, false);
                    var previewPixels = new Color[pixels.Length];
                    for (int i = 0; i < pixels.Length; i++) previewPixels[i] = pixels[i].gamma;
                    preview.SetPixels(previewPixels);
                    preview.Apply();
                    File.WriteAllBytes("Logs/ToonLighting/" + name + ".png", preview.EncodeToPNG());
                    Object.DestroyImmediate(preview);
                    return pixels;
                }

                // Warm up the SRP and shader variant before reading assertions.
                Capture("warmup");
                var hard = Capture("pixel-hard");
                CheckBands(hard, true);
                material.SetFloat("_NoiseStrength", 1);
                material.SetFloat("_NoiseSpace", 1);
                material.SetFloat("_NoisePixelSize", 4);
                // Legacy screen settings must not affect world-anchored sampling.
                material.SetFloat("_NoiseWorldSize", .08f);
                var noisy = Capture("pixel-noise");
                CheckBands(noisy, true);
                var worldCells = CheckWorldCells(noisy, camera, quad.transform, .08f);
                int changed = 0;
                for (int i = 0; i < hard.Length; i++)
                {
                    float difference = Mathf.Abs(hard[i].r - noisy[i].r);
                    if (difference > 0.02f) changed++;
                    if (difference > 0.27f) throw new Exception("Noise skipped a neighboring light band.");
                }
                if (changed < 10) throw new Exception("Boundary pixel noise had no visible effect.");
                camera.transform.position = new Vector3(.37f, .19f, -10);
                CheckWorldCells(Capture("world-noise-camera-moved"), camera, quad.transform, .08f, worldCells);
                camera.orthographicSize = 2.4f;
                CheckWorldCells(Capture("world-noise-camera-zoomed"), camera, quad.transform, .08f, worldCells);
                camera.transform.position = new Vector3(2, 1, -10);
                camera.transform.LookAt(Vector3.zero);
                camera.orthographic = false;
                camera.fieldOfView = 25;
                var perspective = Capture("pixel-noise-perspective");
                CheckWorldCells(perspective, camera, quad.transform, .08f, worldCells);
                control.overrideMaterialSettings = true;
                control.transitionWidth = 1;
                control.worldPixelSize = .16f;
                var overrideCells = CheckWorldCells(Capture("world-noise-light-override"), camera, quad.transform, .16f);
                camera.transform.position += new Vector3(.31f, -.17f, 0);
                CheckWorldCells(Capture("world-noise-light-override-moved"), camera, quad.transform, .16f, overrideCells);
                control.overrideMaterialSettings = false;
                camera.orthographic = true;
                camera.orthographicSize = 2;
                camera.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.identity);
                // Generated lamps refine the receiver grid without changing material bands.
                Object.DestroyImmediate(control);
                light.name = "Light_RedLight_Lamp_0_0_0";
                var lampFine = Capture("lamp-fine-pixels");
                CheckBands(lampFine, true);
                var fineCells = CheckWorldCells(lampFine, camera, quad.transform, .04f);
                if (fineCells.Count <= worldCells.Count * 2)
                    throw new Exception("Lamp sampling did not increase the visible pixel count.");
                foreach (string generatedName in new[] { "Light_Lab_Heater_01_0_0_0", "Light_Lab_Heater_02_0_0_0", "OpticalLight_0" })
                {
                    light.name = generatedName;
                    var generated = Capture(generatedName + "-fine-pixels");
                    for (int i = 0; i < generated.Length; i++)
                        if (Mathf.Abs(generated[i].r - lampFine[i].r) > .02f)
                            throw new Exception("Generated lights use different pixel distributions: " + generatedName);
                }
                camera.transform.position += new Vector3(.31f, -.17f, 0);
                CheckWorldCells(Capture("lamp-fine-pixels-moved"), camera, quad.transform, .04f, fineCells);
                var actor = light.transform.parent = new GameObject("Sampling validation slime").transform;
                actor.gameObject.AddComponent<BlockMap.Gameplay.SlimeActor>();
                light.name = "Slime Glow";
                CheckWorldCells(Capture("slime-fine-pixels"), camera, quad.transform, .04f, fineCells);
                light.transform.SetParent(null, true);
                Object.DestroyImmediate(actor.gameObject);
                light.name = "Lighting validation point light";
                control = light.gameObject.AddComponent<SlimaToonLight>();
                camera.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.identity);
                Directory.CreateDirectory("Docs/images");
                File.Copy("Logs/ToonLighting/pixel-noise.png", "Docs/images/PixelLightNoise.png", true);

                control.mode = SlimaToonLight.LightingMode.Smooth;
                var smooth = Capture("smooth");
                var unique = new HashSet<int>();
                for (int x = 1; x < 599; x++) unique.Add(Mathf.RoundToInt(smooth[100 * 600 + x].r * 1000));
                if (unique.Count < 50) throw new Exception("Per-light smooth mode did not override pixel mode.");
                control.mode = SlimaToonLight.LightingMode.Pixel;
                control.overrideMaterialSettings = true;
                control.bands = 3;
                control.transitionWidth = 0;
                var three = Capture("three-bands");
                foreach (Color p in three)
                    if (Mathf.Abs(p.r * 2 - Mathf.Round(p.r * 2)) > 0.02f)
                        throw new Exception("Per-light band override failed.");

                light.enabled = false;
                var dark = Capture("disabled-light");
                foreach (Color p in dark) if (p.r > 0.02f) throw new Exception("A disabled light still illuminated the surface.");

                light.enabled = true;
                light.type = LightType.Directional;
                light.shadows = LightShadows.None;
                light.intensity = 0.42f;
                control.overrideMaterialSettings = false;
                material.SetFloat("_NoiseStrength", 0);
                material.SetFloat("_ShadowInfluence", 0);
                Capture("main-warmup");
                foreach (Color p in Capture("main-pixel"))
                    if (Mathf.Abs(p.r - 0.5f) > 0.02f)
                        throw new Exception("Main light ignored five-band quantization or intensity: " + p.r);
                control.mode = SlimaToonLight.LightingMode.Smooth;
                foreach (Color p in Capture("main-smooth"))
                    if (Mathf.Abs(p.r - 0.42f) > 0.02f) throw new Exception("Main light smooth mode failed.");

                light.type = LightType.Point;
                material.SetFloat("_ShadowInfluence", 1);
                light.intensity = 3;
                light.color = Color.red;
                control.mode = SlimaToonLight.LightingMode.Pixel;
                var redOnly = Capture("red-pixel");
                var blue = new GameObject("Overlapping smooth blue light").AddComponent<Light>();
                blue.type = LightType.Point;
                blue.transform.position = light.transform.position;
                blue.range = light.range;
                blue.intensity = light.intensity;
                blue.color = Color.blue;
                blue.gameObject.AddComponent<SlimaToonLight>().mode = SlimaToonLight.LightingMode.Smooth;
                var mixed = Capture("overlapping-modes");
                unique.Clear();
                for (int i = 0; i < mixed.Length; i++)
                    if (Mathf.Abs(mixed[i].r - redOnly[i].r) > 0.02f)
                        throw new Exception("Co-located lights overwrote each other's mode.");
                for (int x = 1; x < 599; x++) unique.Add(Mathf.RoundToInt(mixed[100 * 600 + x].b * 1000));
                if (unique.Count < 50) throw new Exception("Co-located smooth light became quantized.");
                if (ShaderUtil.ShaderHasError(shader)) throw new Exception("Rendered shader variant compilation failed.");
                Object.DestroyImmediate(mesh);
                Debug.Log("[Slima Lighting Validation] PASS: world cells stay uniform and unchanged after camera translation, zoom and perspective rotation; material and per-light world sizes; legacy screen settings ignored; five discrete bands; adjacent-band noise; smooth mode; band override; disabled-light cleanup; main-light modes; independent co-located modes; renderer installation.");
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics;
                QualitySettings.renderPipeline = oldQuality;
                QualitySettings.antiAliasing = oldAntiAliasing;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(pipeline);
                Object.DestroyImmediate(renderer);
                Object.DestroyImmediate(feature);
            }
        }

        static Dictionary<Vector2Int, float> CheckWorldCells(Color[] pixels, Camera camera,
            Transform surface, float size, Dictionary<Vector2Int, float> reference = null)
        {
            var cells = new Dictionary<Vector2Int, float>();
            var plane = new Plane(surface.forward, surface.position);
            int compared = 0;
            for (int y = 2; y < 198; y++)
                for (int x = 2; x < 598; x++)
                {
                    Ray ray = camera.ViewportPointToRay(new Vector3((x + .5f) / 600, (y + .5f) / 200, 0));
                    if (!plane.Raycast(ray, out float distance)) continue;
                    Vector3 hit = ray.GetPoint(distance);
                    Vector3 local = surface.InverseTransformPoint(hit);
                    if (Mathf.Abs(local.x) > .49f || Mathf.Abs(local.y) > .49f) continue;
                    Vector2 grid = new Vector2(hit.x, hit.y) / size;
                    float fx = grid.x - Mathf.Floor(grid.x), fy = grid.y - Mathf.Floor(grid.y);
                    // Ignore rasterization uncertainty right on a world-cell boundary.
                    if (fx < .05f || fx > .95f || fy < .05f || fy > .95f) continue;
                    var cell = new Vector2Int(Mathf.FloorToInt(grid.x), Mathf.FloorToInt(grid.y));
                    float value = pixels[y * 600 + x].r;
                    if (cells.TryGetValue(cell, out float existing) && Mathf.Abs(existing - value) > .02f)
                        throw new Exception("Split world pixel cell: " + cell);
                    cells[cell] = value;
                    if (reference != null && reference.TryGetValue(cell, out float expected))
                    {
                        if (Mathf.Abs(expected - value) > .02f)
                            throw new Exception("Camera movement changed world pixel cell: " + cell);
                        compared++;
                    }
                }
            if (cells.Count < 100 || (reference != null && compared < 100))
                throw new Exception("Too few world cells visible to validate camera stability.");
            return cells;
        }

        static void CheckBands(Color[] pixels, bool requireAll)
        {
            var seen = new HashSet<int>();
            foreach (Color p in pixels)
            {
                int band = Mathf.RoundToInt(p.r * 4);
                if (band < 0 || band > 4 || Mathf.Abs(p.r - band * 0.25f) > 0.02f)
                    throw new Exception("Continuous brightness leaked into the pixel mode: " + p.r);
                seen.Add(band);
            }
            if (requireAll && seen.Count != 5) throw new Exception("Expected five bands; observed " + seen.Count);
        }
    }
}
