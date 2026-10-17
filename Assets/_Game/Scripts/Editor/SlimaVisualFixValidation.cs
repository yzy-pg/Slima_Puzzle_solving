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

namespace SlimaPuzzle.Rendering.EditorTools
{
    [InitializeOnLoad]
    public static class SlimaVisualFixValidation
    {
        const string Key = "SlimaVisualFixValidation";
        const string Output = "Logs/VisualFixes";
        const int Width = 960, Height = 600;

        static SlimaVisualFixValidation()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += ValidatePlay;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use Unity batch mode without -quit.");
            Directory.CreateDirectory(Output);
            SlimaLampHaloValidation.Run();
            ValidateSubsurface();
            EditorSceneManager.OpenScene("Assets/Scenes/map03.unity");
            var root = Object.FindObjectOfType<BlockMapGameRoot>();
            var preview = root.GetComponentInChildren<BlockMapDoor>();
            SessionState.SetFloat(Key + ".Heading", preview != null ? preview.transform.eulerAngles.y : 0f);
            SessionState.SetString(Key + ".DoorName", preview != null ? preview.GetComponentInParent<GameplayBlockView>().name : "");
            SessionState.SetBool(Key, true);
            SessionState.SetFloat(Key + ".Deadline", (float)EditorApplication.timeSinceStartup + 90f);
            EditorApplication.update -= ValidatePlay;
            EditorApplication.update += ValidatePlay;
            EditorApplication.isPlaying = true;
        }

        static void ValidateSubsurface()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var oldGraphics = GraphicsSettings.renderPipelineAsset;
            var oldQuality = QualitySettings.renderPipeline;
            var renderer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP-Balanced-Renderer.asset"));
            renderer.rendererFeatures.Clear();
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            var settings = new SerializedObject(pipeline);
            settings.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1;
            settings.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            settings.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 2048;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var material = new Material(Shader.Find("Slima/Toon (Cel + Pixel)"));
            try
            {
                GraphicsSettings.renderPipelineAsset = pipeline;
                QualitySettings.renderPipeline = pipeline;
                Shader.SetGlobalFloat("_SlimaLightSettingsEnabled", 0);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2();
                material.SetColor("_BaseColor", new Color(.4f, .9f, .6f));
                material.SetColor("_ShadowColor", Color.black);
                material.SetColor("_AmbientColor", Color.clear);
                material.SetFloat("_FallbackLight", 0);
                material.SetFloat("_OutlineWidth", 0);
                material.SetFloat("_LightWrap", 0);
                material.SetFloat("_ExtraLightMode", 0);
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.GetComponent<Renderer>().sharedMaterial = material;
                body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var light = new GameObject("Back light").AddComponent<Light>();
                light.type = LightType.Point;
                light.transform.position = new Vector3(0, 0, 2);
                light.range = 5; light.intensity = 2;
                light.shadows = LightShadows.Soft;
                light.shadowBias = .03f; light.shadowNormalBias = .1f;
                var camera = new GameObject("Transmission camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -4);
                camera.orthographic = true; camera.orthographicSize = 1;
                camera.aspect = (float)Width / Height;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.enabled = false;
                Color[] opaque = Capture(camera, "slime-opaque");
                material.SetFloat("_SubsurfaceStrength", .7f);
                material.SetFloat("_SubsurfaceThickness", .2f);
                Color[] scattering = Capture(camera, "slime-scattering");
                int center = Height / 2 * Width + Width / 2;
                Check(scattering[center].grayscale > opaque[center].grayscale + .025f,
                    "Back light did not transmit through the slime material.");
                var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.transform.position = new Vector3(0, 0, 1);
                blocker.transform.localScale = new Vector3(3, 3, .2f);
                Color[] blocked = Capture(camera, "slime-scattering-occluded");
                Check(blocked[center].grayscale < scattering[center].grayscale * .5f,
                    "Subsurface light leaked through an occluding wall.");
                Check(!ShaderUtil.ShaderHasError(material.shader), "Subsurface shader failed to compile.");
                Debug.Log("[Slima Visual Fix Validation] GPU PASS: backlit transmission and shadow occlusion.");
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics;
                QualitySettings.renderPipeline = oldQuality;
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(pipeline);
                Object.DestroyImmediate(renderer);
            }
        }

        static void ValidatePlay()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".Deadline", 0))
                    throw new Exception("Play validation timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.timeSinceLevelLoad < 1) return;
                var root = Object.FindObjectOfType<BlockMapGameRoot>();
                if (root == null || root.Party == null) return;
                var world = root.GetComponent<BlockWorld>();
                int expected = 0;
                foreach (var item in world.Blocks)
                    if (BlockGameplayCatalog.Get(BlockLibrary.Get(item.Value).id) != null) expected++;
                var views = root.GetComponentsInChildren<GameplayBlockView>();
                Check(views.Length == expected, "Baked and live models still overlap: " + views.Length + " / " + expected);
                GameplayBlockView doorView = null;
                foreach (var view in views)
                    if (view.name == SessionState.GetString(Key + ".DoorName", "")) doorView = view;
                Check(doorView != null, "Scene door did not spawn.");
                Check(Mathf.Abs(Mathf.DeltaAngle(doorView.transform.eulerAngles.y, SessionState.GetFloat(Key + ".Heading", 0))) < .01f,
                    "Door closed orientation changed on entering Play.");
                var door = doorView.GetComponentInChildren<BlockMapDoor>();
                Check(door.hinge != null, "Sliding door panel is missing.");
                door.Close(); door.AdvanceAnimation(door.duration);
                Check(!door.IsOpen && Quaternion.Angle(door.hinge.localRotation, Quaternion.identity) < .01f &&
                    doorView.GetComponentInChildren<Renderer>().enabled,"Closed door is not visible in its authored pose.");
                Vector3 pivot = door.hinge.position;
                Camera camera = Camera.main;
                // Frame the gameplay door; this map also contains distant disconnected chunks.
                Vector3 focus = doorView.transform.position;
                camera.transform.position = focus + root.cameraOffset;
                camera.transform.LookAt(focus);
                camera.orthographicSize = 7.5f;
                camera.aspect = (float)Width / Height;
                Capture(camera, "map03-closed");
                door.Open(); door.AdvanceAnimation(door.duration);
                Check(door.IsOpen && Quaternion.Angle(door.hinge.localRotation, Quaternion.identity) < .01f,
                    "Open door did not use the corrected angle.");
                Check(Vector3.Distance(door.hinge.position-pivot,doorView.transform.right*door.slideDistance) < .0001f &&
                    !door.hinge.GetComponentInChildren<Renderer>(true).enabled,"Opened door did not slide right and hide.");
                Check(root.GetComponentsInChildren<GameplayBlockView>().Length == expected, "Animation left a duplicate model.");
                Capture(camera, "map03-open");
                var properties = new MaterialPropertyBlock();
                root.Party.ActiveActor.GetComponentInChildren<Renderer>().GetPropertyBlock(properties);
                Check(properties.GetFloat("_SubsurfaceStrength") > 0, "Player did not enable its scattering material.");
                Check(Mathf.Abs(properties.GetFloat("_NoiseWorldSize") - .04f) < .00001f,
                    "Player did not apply the finer sampling grid.");
                Debug.Log("[Slima Visual Fix Validation] PLAY PASS: one live view per cell, authored door heading, sliding/hiding panel and player scattering; map03 screenshots saved.");
                Finish(0);
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }

        static void Finish(int code)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= ValidatePlay;
            EditorApplication.Exit(code);
        }

        static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        static Color[] Capture(Camera camera, string name)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.StandardRequest { destination = target });
            var previous = RenderTexture.active; RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); readback.Apply();
            RenderTexture.active = previous;
            Color[] pixels = readback.GetPixels();
            if (name.StartsWith("map03"))
            {
                int visible = 0;
                foreach (Color pixel in pixels) if (pixel.grayscale > .025f) visible++;
                Check(visible > 1000, "Scene screenshot did not frame the visible gameplay area.");
            }
            var preview = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var gamma = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) gamma[i] = pixels[i].gamma;
            preview.SetPixels(gamma); preview.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", preview.EncodeToPNG());
            Object.DestroyImmediate(preview); Object.DestroyImmediate(readback);
            target.Release(); Object.DestroyImmediate(target);
            return pixels;
        }
    }
}
