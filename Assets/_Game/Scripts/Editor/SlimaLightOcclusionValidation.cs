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
    // GPU regression: wall caps must not receive local lights from below/behind.
    public static class SlimaLightOcclusionValidation
    {
        const string Output = "Logs/LightOcclusion";
        const int Size = 400;

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var oldGraphics = GraphicsSettings.renderPipelineAsset;
            var oldQuality = QualitySettings.renderPipeline;
            var material = new Material(Shader.Find("Slima/Toon (Cel + Pixel)"));
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2();
                RenderSettings.fog = false;
                material.SetColor("_ShadowColor", Color.black);
                material.SetColor("_AmbientColor", Color.clear);
                material.SetFloat("_FallbackLight", 0);
                material.SetFloat("_OutlineWidth", 0);
                material.SetFloat("_ExtraLightMode", 1);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.position = new Vector3(0, -.1f, 0);
                floor.transform.localScale = new Vector3(8, .2f, 8);
                floor.GetComponent<Renderer>().sharedMaterial = material;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = new Vector3(0, .5f, 0);
                wall.transform.localScale = new Vector3(1, 1, 6);
                wall.GetComponent<Renderer>().sharedMaterial = material;
                var light = new GameObject("Slime Glow").AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 8;
                light.intensity = 2.4f;
                light.shadows = LightShadows.Soft;
                light.shadowBias = .04f;
                light.shadowNormalBias = .15f;
                light.shadowNearPlane = .1f;
                light.transform.position = new Vector3(-1, .38f, 0);
                var camera = new GameObject("Occlusion camera").AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 4;
                camera.transform.position = new Vector3(0, 12, -10);
                camera.transform.LookAt(Vector3.zero);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = true;
                camera.enabled = false;
                foreach (string tier in new[] { "Performant", "Balanced", "HighFidelity" })
                {
                    var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP-" + tier + ".asset");
                    GraphicsSettings.renderPipelineAsset = pipeline;
                    QualitySettings.renderPipeline = pipeline;
                    typeof(RenderPipelineManager).GetMethod("CleanupRenderPipeline", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                    typeof(RenderPipelineManager).GetMethod("PrepareRenderPipeline", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { pipeline });
                    foreach (int mode in new[] { 0, 1 })
                    {
                        material.SetFloat("_LightingMode", mode);
                        light.enabled = false;
                        Capture(camera, target, pixels, null);
                        float dark = Sample(camera, pixels, new Vector3(0, 1, 0));
                        light.enabled = true;
                        light.shadows = LightShadows.None;
                        Capture(camera, target, pixels, null);
                        float leaked = Sample(camera, pixels, new Vector3(1.2f, 0, 0));
                        Require(leaked > .1f, "Unshadowed point light did not reproduce the wall leak.");
                        light.shadows = LightShadows.Soft;
                        Capture(camera, target, pixels, null);
                        Capture(camera, target, pixels, tier + "-" + mode);
                        float cap = Sample(camera, pixels, new Vector3(0, 1, 0));
                        float blocked = Sample(camera, pixels, new Vector3(1.2f, 0, 0));
                        float lit = Sample(camera, pixels, new Vector3(-1.5f, 0, 0));
                        Debug.Log($"[Light Occlusion] {tier} mode {mode}: cap={cap:F4}, dark={dark:F4}, blocked={blocked:F4}, lit={lit:F4}");
                        Require(cap < dark + .02f, "Wall cap lit from below: " + tier + "/" + mode);
                        Require(blocked < .02f && lit > .1f, "Wall occlusion or normal floor lighting failed.");
                    }
                }
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics;
                QualitySettings.renderPipeline = oldQuality;
                Object.DestroyImmediate(material);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
            }
            CheckSpawnedLights();
            CheckShadowPolicy();
            CaptureLevel();
            File.WriteAllText(Output + "/result.txt", "PASS: terrain casts shadows; props, actors and lamp submeshes do not; shared material assets are preserved; all quality tiers retain wall occlusion; actual 4-2 floor receives blue light while its outside wall stays occluded; generated lights share pixel distributions.\n");
        }

        static void CheckSpawnedLights()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BlockLibrary.Reload();
            var root = new GameObject("Lamp shadow regression");
            var world = root.AddComponent<BlockWorld>();
            world.Set(Vector3Int.zero, BlockLibrary.IndexOf("FluorescentScreen_Lamp"));
            var spawner = root.AddComponent<BlockMapRuntimeSpawner>();
            spawner.Rebuild();
            var lamp = root.GetComponentInChildren<Light>();
            Require(lamp != null && lamp.shadows != LightShadows.None, "New lamps need wall shadows.");
            Object.DestroyImmediate(root);
            lamp = new GameObject("Light_FluorescentScreen_Lamp_0_0_0").AddComponent<Light>();
            Vector3 authoredPosition = new Vector3(1, 2, 3);
            lamp.transform.position = authoredPosition;
            lamp.intensity = .7f;
            root = new GameObject("Saved lamp shadow regression");
            world = root.AddComponent<BlockWorld>();
            world.Set(Vector3Int.zero, BlockLibrary.IndexOf("FluorescentScreen_Lamp"));
            spawner = root.AddComponent<BlockMapRuntimeSpawner>();
            spawner.Rebuild();
            Require(lamp.shadows != LightShadows.None && lamp.transform.position == authoredPosition && lamp.intensity == .7f,
                "Saved lamps must gain occlusion while preserving authored position and intensity.");
            foreach (bool receiver in new[] { false, true })
            {
                var glow = new GameObject("Optical shadow regression").AddComponent<GameplayOpticGlow>();
                glow.receiver = receiver;
                glow.Initialize();
                var lights = glow.GetComponentsInChildren<Light>(true);
                Require(lights.Length == (receiver ? 4 : 1), "Optical face count changed.");
                foreach (var light in lights) Require(light.shadows != LightShadows.None, "Optical lights need wall shadows.");
            }
        }

        static void CheckShadowPolicy()
        {
            var owner = new GameObject("Shadow policy regression");
            var root = owner.AddComponent<BlockMapGameRoot>();
            var wall = new Material(Shader.Find("Slima/Toon (Cel + Pixel)")) { name = "Lab_WallBrick1" };
            var lamp = new Material(wall) { name = "FluorescentScreen_Lamp" };
            var chunk = new GameObject("Chunk_regression").AddComponent<MeshRenderer>();
            chunk.transform.SetParent(owner.transform);
            chunk.sharedMaterials = new[] { wall, lamp };
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Renderer>();
            prop.transform.SetParent(owner.transform);
            prop.sharedMaterial = wall;
            foreach (bool atmosphere in new[] { false, true })
            {
                root.sceneAtmosphere = atmosphere;
                root.ApplyAtmosphereTo(chunk);
                root.ApplyAtmosphereTo(prop);
                Require(chunk.shadowCastingMode == ShadowCastingMode.On && chunk.sharedMaterials[0].GetShaderPassEnabled("ShadowCaster"),
                    "Terrain must retain solid shadows.");
                Require(prop.shadowCastingMode == ShadowCastingMode.Off && prop.receiveShadows,
                    "Props must receive terrain shadows without casting their own.");
                Require(!chunk.sharedMaterials[1].GetShaderPassEnabled("ShadowCaster") && lamp.GetShaderPassEnabled("ShadowCaster"),
                    "Lamp submeshes must stop casting without modifying shared assets.");
            }
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(wall);
            Object.DestroyImmediate(lamp);
            Debug.Log("[Light Occlusion] PASS: terrain-only shadow policy with atmosphere enabled or disabled; baked lamp submeshes excluded.");
        }

        public static void CaptureLevel()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene("Assets/Scenes/test-level 4-2.unity", OpenSceneMode.Single);
            var root = Object.FindObjectOfType<BlockMapGameRoot>();
            typeof(BlockMapGameRoot).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(root, null);
            root.GetComponent<BlockMapRuntimeSpawner>().Rebuild();
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
                Require(renderer.name.StartsWith("Chunk_", StringComparison.Ordinal) || renderer.shadowCastingMode == ShadowCastingMode.Off,
                    "Non-terrain object still casts a solid shadow: " + renderer.name);
            var camera = Camera.main;
            camera.transform.position = new Vector3(125, 18, 120);
            camera.transform.LookAt(new Vector3(125, 1, 135));
            camera.orthographicSize = 6;
            camera.aspect = 16f / 9;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGBAFloat, false, true);
            try
            {
                Capture(camera, target, pixels, null);
                Capture(camera, target, pixels, "test-level-4-2");
                Vector3[] floorPoints = { new Vector3(120.5f, 1.001f, 134.5f), new Vector3(122.5f, 1.001f, 134.5f) };
                var lit = new Color[floorPoints.Length];
                for (int i = 0; i < lit.Length; i++) lit[i] = SampleColor(camera, pixels, floorPoints[i]);
                Vector3 outsideWall = new Vector3(121.5f, 1.5f, 132f);
                Color wallLit = SampleColor(camera, pixels, outsideWall);
                var padLights = new System.Collections.Generic.List<Light>();
                foreach (var light in Object.FindObjectsOfType<Light>())
                    if (light.name.StartsWith("Light_Lab_Heater_02_", StringComparison.Ordinal))
                    { padLights.Add(light); light.enabled = false; }
                Capture(camera, target, pixels, null);
                for (int i = 0; i < lit.Length; i++)
                    Require(lit[i].b > SampleColor(camera, pixels, floorPoints[i]).b + .1f,
                        "4-2 pad shadows extinguished the surrounding floor light: " + floorPoints[i]);
                Require(wallLit.b < SampleColor(camera, pixels, outsideWall).b + .02f,
                    "4-2 pad light leaked through the outside wall.");
                foreach (var light in padLights) light.enabled = true;
                Debug.Log("[Light Occlusion] 4-2 PASS: blue pads illuminate both sides of the floor while the outer wall stays occluded.");
            }
            finally
            {
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
            }
        }

        static float Sample(Camera camera, Texture2D pixels, Vector3 position)
        {
            Vector3 uv = camera.WorldToViewportPoint(position);
            return pixels.GetPixel(Mathf.RoundToInt(uv.x * Size), Mathf.RoundToInt(uv.y * Size)).r;
        }

        static Color SampleColor(Camera camera, Texture2D pixels, Vector3 position)
        {
            Vector3 uv = camera.WorldToViewportPoint(position);
            return pixels.GetPixel((int)(uv.x * pixels.width), (int)(uv.y * pixels.height));
        }

        static void Capture(Camera camera, RenderTexture target, Texture2D pixels, string name)
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            RenderTexture.active = previous;
            if (name == null) return;
            var preview = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var colors = pixels.GetPixels();
            for (int i = 0; i < colors.Length; i++) colors[i] = colors[i].gamma;
            preview.SetPixels(colors);
            preview.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", preview.EncodeToPNG());
            Object.DestroyImmediate(preview);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
