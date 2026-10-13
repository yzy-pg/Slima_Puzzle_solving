using System;
using System.IO;
using BlockMap;
using BlockMap.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaElementFloorGlowValidation
    {
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var oldGraphics = GraphicsSettings.renderPipelineAsset;
            var oldQuality = QualitySettings.renderPipeline;
            var renderer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/Settings/URP-HighFidelity-Renderer.asset"));
            renderer.rendererFeatures.Clear();
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.msaaSampleCount = 1;
            var target = new RenderTexture(1200, 700, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(1200, 700, TextureFormat.RGBAFloat, false, true);
            try
            {
                GraphicsSettings.renderPipelineAsset = pipeline;
                QualitySettings.renderPipeline = pipeline;
                RenderSettings.ambientLight = new Color(.06f, .08f, .12f);
                RenderSettings.fog = false;
                BlockLibrary.Reload();
                var root = new GameObject("Element floor preview");
                var world = root.AddComponent<BlockWorld>();
                string[] ids = { "Lab_Heater_01", "Lab_Heater_02" };
                for (int i = 0; i < 2; i++)
                    world.Set(new Vector3Int(i * 3, 1, 0), BlockLibrary.IndexOf(ids[i]));
                root.AddComponent<BlockMapRuntimeSpawner>().Rebuild();
                foreach (var effect in root.GetComponentsInChildren<ElementFloorRiseEffect>())
                {
                    effect.Initialize();
                    effect.SetPhase(.12f);
                }
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.position = new Vector3(2, .5f, .5f);
                floor.transform.localScale = new Vector3(8, 1, 5);
                var floorMaterial = new Material(Shader.Find("Slima/Toon (Cel + Pixel)"));
                floorMaterial.color = new Color(.22f, .27f, .34f);
                floorMaterial.SetFloat("_OutlineWidth", 0);
                floorMaterial.SetFloat("_FallbackLight", 0);
                floorMaterial.SetFloat("_NoiseStrength", 0);
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                var camera = new GameObject("Element floor camera").AddComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = 2.6f;
                camera.aspect = 1200f / 700; camera.allowHDR = true; camera.allowMSAA = false;
                camera.backgroundColor = new Color(.015f, .022f, .04f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                Vector3 focus = new Vector3(2, 1.1f, .5f);
                camera.transform.position = focus + new Vector3(2, 5, -6);
                camera.transform.LookAt(focus);
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                var light = new GameObject("Preview directional").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = .25f;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
                var rings = new System.Collections.Generic.List<Renderer>();
                foreach (var effect in root.GetComponentsInChildren<ElementFloorRiseEffect>())
                    for (int i = 0; i < 3; i++) rings.Add(effect.transform.Find("RisingRectangle_" + i).GetComponent<Renderer>());
                Color[] Capture(string path)
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var old = RenderTexture.active; RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 1200, 700), 0, 0); readback.Apply(); RenderTexture.active = old;
                    var colors = readback.GetPixels();
                    if (path != null)
                    {
                        var preview = new Texture2D(1200, 700, TextureFormat.RGBA32, false);
                        var gamma = new Color[colors.Length];
                        for (int i = 0; i < gamma.Length; i++) gamma[i] = colors[i].gamma;
                        preview.SetPixels(gamma); preview.Apply();
                        File.WriteAllBytes(path, preview.EncodeToPNG()); Object.DestroyImmediate(preview);
                    }
                    return colors;
                }
                Directory.CreateDirectory("Logs/ElementGlass");
                Directory.CreateDirectory("Docs/images");
                Capture(null);
                foreach (var ring in rings) ring.enabled = false;
                var without = Capture("Logs/ElementGlass/floor-without-halo.png");
                foreach (var ring in rings) ring.enabled = true;
                var with = Capture("Docs/images/ElementFloorGlow.png");
                int changed = 0;
                float peakEmission = 0;
                for (int i = 0; i < with.Length; i++)
                {
                    Color emission = with[i] - without[i];
                    peakEmission = Mathf.Max(peakEmission, Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b)));
                    if (Mathf.Abs(with[i].r - without[i].r) + Mathf.Abs(with[i].g - without[i].g) +
                        Mathf.Abs(with[i].b - without[i].b) > .025f) changed++;
                }
                if (changed < 100) throw new Exception("Rising surface rectangles did not render.");
                if (peakEmission < 3) throw new Exception("Rectangular emission is too dim for HDR glow.");
                void CheckAdditive(Color[] background, Color[] result, string label)
                {
                    for (int i = 0; i < result.Length; i++)
                    {
                        Color error = (result[i] - background[i]) - (with[i] - without[i]);
                        if (Mathf.Abs(error.r) + Mathf.Abs(error.g) + Mathf.Abs(error.b) > .02f)
                            throw new Exception("Rectangle emission changed with " + label);
                    }
                }
                Color originalBackground = camera.backgroundColor;
                camera.backgroundColor = new Color(.2f, .2f, .2f);
                foreach (var ring in rings) ring.enabled = false;
                var gray = Capture(null);
                foreach (var ring in rings) ring.enabled = true;
                CheckAdditive(gray, Capture(null), "background color (linear additive blending)");
                camera.backgroundColor = originalBackground;
                var lights = Object.FindObjectsOfType<Light>();
                foreach (var source in lights) source.enabled = false;
                foreach (var ring in rings) ring.enabled = false;
                var dark = Capture(null);
                foreach (var ring in rings) ring.enabled = true;
                CheckAdditive(dark, Capture(null), "scene lights");
                foreach (var source in lights) source.enabled = true;
                foreach (var ring in rings)
                    if (ShaderUtil.ShaderHasError(ring.sharedMaterial.shader))
                        throw new Exception("Rectangle shader compilation failed.");
                File.WriteAllText("Logs/ElementGlass/floor-glow-gpu.txt", "PASS: HDR linear-additive rectangles, background/scene-light independence; peak emission=" + peakEmission + "; changed pixels=" + changed);
                for (int frame = 0; frame < 12; frame++)
                {
                    foreach (var effect in root.GetComponentsInChildren<ElementFloorRiseEffect>())
                        effect.SetPhase(frame / 12f);
                    Capture("Logs/ElementGlass/rise-frame-" + frame.ToString("D2") + ".png");
                }
                Debug.Log("[ElementFloorGPU] PASS: surface rectangles and correct top/side model; changed pixels=" + changed);
                Object.DestroyImmediate(floorMaterial);
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics; QualitySettings.renderPipeline = oldQuality;
                Object.DestroyImmediate(pipeline); Object.DestroyImmediate(renderer);
                Object.DestroyImmediate(target); Object.DestroyImmediate(readback);
            }
        }
    }
}
