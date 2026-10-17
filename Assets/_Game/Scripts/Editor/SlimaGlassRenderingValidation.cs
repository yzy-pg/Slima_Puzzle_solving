using System;
using System.IO;
using BlockMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaGlassRenderingValidation
    {
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var oldGraphics = GraphicsSettings.renderPipelineAsset;
            var oldQuality = QualitySettings.renderPipeline;
            var data = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP-Balanced-Renderer.asset"));
            data.rendererFeatures.Clear();
            var lightingFeature = ScriptableObject.CreateInstance<SlimaPuzzle.Rendering.SlimaToonLightingFeature>();
            data.rendererFeatures.Add(lightingFeature);
            var pipeline = UniversalRenderPipelineAsset.Create(data);
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
            var material = new Material(Shader.Find("BlockMap/BlockUnlitTransparent"));
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                pipeline.msaaSampleCount = 1;
                pipeline.maxAdditionalLightsCount = 8;
                var settings = new SerializedObject(pipeline);
                settings.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
                GraphicsSettings.renderPipelineAsset = pipeline; QualitySettings.renderPipeline = pipeline;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 1;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2(); RenderSettings.fog = false;
                material.SetColor("_ShadowColor", Color.black); material.SetColor("_AmbientColor", Color.clear);
                material.SetFloat("_FallbackLight", 0); material.SetFloat("_LightingMode", 1);
                material.mainTexture = texture;
                void TextureColor(Color color) { texture.SetPixel(0, 0, color); texture.Apply(); }
                TextureColor(Color.white);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.GetComponent<Renderer>().sharedMaterial = material;
                var mesh = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
                mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                quad.GetComponent<MeshFilter>().sharedMesh = mesh;
                void AmbientProbe(Color color)
                {
                    var probe = new SphericalHarmonicsL2(); probe.AddAmbientLight(color);
                    RenderSettings.ambientLight = color; RenderSettings.ambientProbe = probe;
                    // Provide SH immediately; editor environment-probe refresh is asynchronous.
                    var properties = new MaterialPropertyBlock();
                    properties.CopySHCoefficientArraysFrom(new[] { probe });
                    quad.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.CustomProvided;
                    quad.GetComponent<Renderer>().SetPropertyBlock(properties);
                }
                var camera = new GameObject("Glass camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -4);
                camera.orthographic = true; camera.orthographicSize = .7f; camera.aspect = 1;
                camera.allowHDR = true; camera.allowMSAA = false; camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                // Register lights before the first cull, then vary only their shader settings.
                var sun = new GameObject("Glass sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = .7f; RenderSettings.sun = sun;
                var sunControl = sun.gameObject.AddComponent<SlimaPuzzle.Rendering.SlimaToonLight>();
                var lamp = new GameObject("Glass lamp").AddComponent<Light>();
                lamp.transform.position = new Vector3(0, 0, -1); lamp.range = 4; lamp.intensity = 2;
                var lampControl = lamp.gameObject.AddComponent<SlimaPuzzle.Rendering.SlimaToonLight>();
                foreach (var control in new[] { sunControl, lampControl })
                {
                    control.mode = SlimaPuzzle.Rendering.SlimaToonLight.LightingMode.Smooth;
                    control.overrideMaterialSettings = true; control.brightness = 0;
                }
                Color[] Capture(string name = null)
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var previous = RenderTexture.active; RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply(); RenderTexture.active = previous;
                    Color[] result = pixels.GetPixels();
                    if (name != null)
                    {
                        var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                        var gamma = new Color[result.Length];
                        for (int i = 0; i < gamma.Length; i++) gamma[i] = result[i].gamma;
                        image.SetPixels(gamma); image.Apply();
                        File.WriteAllBytes("Logs/GlassRendering/" + name + ".png", image.EncodeToPNG());
                        Object.DestroyImmediate(image);
                    }
                    return result;
                }
                Color Center(string name = null) => Capture(name)[128 * 256 + 128];
                Directory.CreateDirectory("Logs/GlassRendering");
                foreach (RenderingMode mode in new[] { RenderingMode.Forward, RenderingMode.ForwardPlus })
                {
                    data.renderingMode = mode;
                    sunControl.brightness = lampControl.brightness = 0;
                    AmbientProbe(Color.black);
                    material.SetColor("_AmbientColor", Color.clear);
                    Capture();
                    Color dark = Center();
                    AmbientProbe(new Color(.1f, .2f, .5f));
                    Color ambient = Center(mode + "-ambient");
                    Require(ambient.b > dark.b + .1f, mode + " glass ignores ambient SH: " + dark + " -> " + ambient);
                    AmbientProbe(Color.black);
                    quad.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.BlendProbes;
                    quad.GetComponent<Renderer>().SetPropertyBlock(null);
                    sunControl.brightness = 1;
                    Color direct = Center(mode + "-directional");
                    Require(direct.r > dark.r + .05f, mode + " glass ignores directional light: " + dark + " -> " + direct +
                        "; main color=" + Shader.GetGlobalVector("_MainLightColor") + "; direction=" + Shader.GetGlobalVector("_MainLightPosition") +
                        "; mesh normal=" + mesh.normals[0] + "; settings=" + Shader.GetGlobalVector("_SlimaMainLightSettings"));
                    sunControl.brightness = 0; lampControl.brightness = 1;
                    Require(Center().r > dark.r + .05f, mode + " glass ignores point light");
                }
                sunControl.brightness = lampControl.brightness = 0;
                material.SetColor("_AmbientColor", Color.white);
                camera.backgroundColor = new Color(.05f, .1f, .2f);
                quad.SetActive(false); Color background = Center(); quad.SetActive(true);
                TextureColor(new Color(1, 0, 0, 0));
                Require(Error(Center(), background) < .02f, "Transparent pixels hide background");
                TextureColor(new Color(1, 0, 0, .5f));
                Color expected = background * (1 - 128f / 255) + Color.red * (128f / 255);
                Require(Error(Center(), expected) < .03f, "Half-alpha blending changed");
                quad.SetActive(false);
                BlockLibrary.Reload();
                var root = new GameObject("Glass terrain");
                var world = root.AddComponent<BlockWorld>();
                var renderer = root.AddComponent<BlockChunkRenderer>();
                void UnlitTestLighting()
                {
                    foreach (var item in root.GetComponentsInChildren<Renderer>())
                    foreach (var mat in item.sharedMaterials)
                    {
                        mat.SetColor("_AmbientColor", Color.white); mat.SetColor("_ShadowColor", Color.black);
                        mat.SetFloat("_FallbackLight", 0);
                    }
                }
                foreach (Vector3Int direction in new[] { Vector3Int.up, Vector3Int.back })
                {
                    Vector3Int cell = Vector3Int.one;
                    world.Clear();
                    world.Set(cell, BlockLibrary.IndexOf("Lab_Floor_01")); renderer.RebuildAll();
                    camera.transform.position = (Vector3)cell + Vector3.one * .5f + (Vector3)direction * 4;
                    camera.transform.LookAt((Vector3)cell + Vector3.one * .5f);
                    UnlitTestLighting(); Color floor = Center();
                    world.Set(cell + direction, BlockLibrary.IndexOf("Glass_Wall"));
                    renderer.RebuildAll(); UnlitTestLighting();
                    Color through = Center(direction == Vector3Int.up ? "floor-through-glass" : "wall-through-glass");
                    Require(Error(floor, through) < .03f, "Glass removes the touching opaque face: " + direction);
                }
                Require(!ShaderUtil.ShaderHasError(material.shader), "Glass shader compilation failed");
                File.WriteAllText("Logs/GlassRendering/validation.txt", "PASS: Forward/Forward+ ambient SH, directional/point lights; zero/half alpha; touching floor/wall textures preserved.");
                Debug.Log("[Glass Rendering] PASS: ambient/direct lighting, alpha and floor/wall visibility.");
                Object.DestroyImmediate(mesh);
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics; QualitySettings.renderPipeline = oldQuality;
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(material); Object.DestroyImmediate(texture);
                Object.DestroyImmediate(pipeline); Object.DestroyImmediate(data);
            }
        }

        static float Error(Color a, Color b) => Mathf.Abs(a.r-b.r) + Mathf.Abs(a.g-b.g) + Mathf.Abs(a.b-b.b);
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
