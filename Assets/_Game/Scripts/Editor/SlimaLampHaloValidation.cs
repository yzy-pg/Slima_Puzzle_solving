using System;
using System.Collections.Generic;
using System.IO;
using BlockMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.Rendering.EditorTools
{
    public static class SlimaLampHaloValidation
    {
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch-only GPU validation.");
            AssetDatabase.Refresh();
            var shader = Resources.Load<Material>("BlockMapLampHalo").shader;
            if (ShaderUtil.ShaderHasError(shader)) throw new Exception("Lamp halo shader failed to import.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var oldGraphics = GraphicsSettings.renderPipelineAsset;
            var oldQuality = QualitySettings.renderPipeline;
            var renderer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP-HighFidelity-Renderer.asset"));
            renderer.rendererFeatures.Clear();
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.msaaSampleCount = 1; pipeline.renderScale = 1;
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            Material wallMaterial = null;
            try
            {
                GraphicsSettings.renderPipelineAsset = pipeline; QualitySettings.renderPipeline = pipeline;
                RenderSettings.fog = false;
                var camera = new GameObject("Halo validation camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true; camera.orthographicSize = 2; camera.aspect = 1;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.allowHDR = true; camera.allowMSAA = false; camera.enabled = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                def.id = "Validation_Lamp"; def.lightColor = Color.white;
                def.lampHaloIntensity = 1; def.lampHaloBrightness = new Vector3(.1f, .3f, .7f);
                def.lampHaloTransitionWidth = 0;
                var halo = new GameObject("Validation halo").AddComponent<BlockMapLampHalo>();
                halo.Configure(def);
                Color[] Capture(string name)
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var previous = RenderTexture.active; RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); readback.Apply(); RenderTexture.active = previous;
                    var pixels = readback.GetPixels();
                    var preview = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    var colors = new Color[pixels.Length];
                    for (int i = 0; i < colors.Length; i++) colors[i] = pixels[i].gamma;
                    preview.SetPixels(colors); preview.Apply();
                    Directory.CreateDirectory("Logs/LampHalo");
                    File.WriteAllBytes("Logs/LampHalo/" + name + ".png", preview.EncodeToPNG());
                    Object.DestroyImmediate(preview);
                    return pixels;
                }
                Capture("warmup");
                var hard = Capture("smooth-gradient");
                CheckSmoothGradient(hard);
                def.lampHaloTransitionWidth = 1; halo.Configure(def);
                var noisy = Capture("legacy-noise-ignored"); CheckSmoothGradient(noisy);
                int differences = 0;
                for (int i = 0; i < hard.Length; i++) if (Mathf.Abs(hard[i].r - noisy[i].r) > .02f) differences++;
                if (differences != 0) throw new Exception("Legacy pixel parameters changed the smooth halo.");
                camera.transform.position = new Vector3(7, 3, -5); camera.transform.LookAt(Vector3.zero);
                var orbit = Capture("camera-orbit");
                differences = 0;
                for (int i = 0; i < noisy.Length; i++) if (Mathf.Abs(noisy[i].r - orbit[i].r) > .02f) differences++;
                if (differences > noisy.Length / 100) throw new Exception("Halo stopped facing the rendering camera.");
                camera.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.identity);
                def.lampHalo = false; halo.Configure(def); camera.backgroundColor = new Color(.2f, .2f, .2f, 1);
                var background = Capture("background");
                def.lampHalo = true; halo.Configure(def);
                var added = Capture("linear-dodge");
                for (int i = 0; i < added.Length; i++)
                    if (Mathf.Abs(added[i].r - background[i].r - noisy[i].r) > .02f)
                        throw new Exception("Halo did not add linearly to the existing framebuffer.");
                camera.backgroundColor = Color.black;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
                wall.transform.position = new Vector3(0, 0, -2); wall.transform.localScale = Vector3.one * 4;
                wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                wallMaterial.SetColor("_BaseColor", Color.black); wall.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;
                foreach (Color pixel in Capture("occluded"))
                    if (pixel.r > .02f) throw new Exception("Halo showed through an opaque wall.");
                Object.DestroyImmediate(wall);
                camera.backgroundColor = Color.black;
                def.lampHaloIntensity = 0; halo.Configure(def);
                foreach (Color pixel in Capture("zero-intensity"))
                    if (pixel.r > .02f) throw new Exception("Zero intensity still drew a halo.");
                if (ShaderUtil.ShaderHasError(shader)) throw new Exception("Rendered halo shader failed to compile.");
                Directory.CreateDirectory("Docs/images");
                File.Copy("Logs/LampHalo/smooth-gradient.png", "Docs/images/LampHalo.png", true);
                Object.DestroyImmediate(halo.gameObject);
                Debug.Log("[Slima Lamp Halo Validation] PASS: smooth radial gradient, legacy pixel settings ignored, per-camera billboard, linear additive blending, opaque wall occlusion and zero intensity.");
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics; QualitySettings.renderPipeline = oldQuality;
                Object.DestroyImmediate(target); Object.DestroyImmediate(readback); Object.DestroyImmediate(def);
                Object.DestroyImmediate(pipeline); Object.DestroyImmediate(renderer);
                if (wallMaterial != null) Object.DestroyImmediate(wallMaterial);
            }
        }

        static void CheckSmoothGradient(Color[] pixels)
        {
            var seen = new HashSet<int>();
            foreach (Color p in pixels)
            {
                if (p.r < .02f) continue;
                seen.Add(Mathf.RoundToInt(p.r * 1000));
            }
            if (seen.Count < 80) throw new Exception("Halo is still quantized instead of smoothly graded.");
            float center = pixels[128 * 256 + 128].r;
            if (center < .9f) throw new Exception("Halo center is not its brightest region.");
            for (int x = 129; x < 190; x++)
                if (pixels[128 * 256 + x].r > pixels[128 * 256 + x - 1].r + .002f)
                    throw new Exception("Halo falloff contains noise or non-monotonic bands.");
        }
    }
}
