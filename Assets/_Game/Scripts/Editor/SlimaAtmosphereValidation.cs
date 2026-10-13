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
    // Render real point-light occlusion on every quality tier without saving scenes.
    [InitializeOnLoad]
    public static class SlimaAtmosphereValidation
    {
        const int Width = 960, Height = 540;
        const string Output = "Logs/Atmosphere";
        const string PlayKey = "SlimaAtmosphereValidation.Play";

        static SlimaAtmosphereValidation()
        {
            if (SessionState.GetBool(PlayKey, false)) EditorApplication.update += ValidatePlay;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use Unity batch mode.");
            Directory.CreateDirectory(Output);
            var shader = Shader.Find("Slima/Toon (Cel + Pixel)");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Toon shader import failed.");
            var oldGraphics = GraphicsSettings.renderPipelineAsset;
            var oldQuality = QualitySettings.renderPipeline;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave };
            var readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave };
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2();
                RenderSettings.fog = false;
                material.SetColor("_ShadowColor", Color.black);
                material.SetColor("_AmbientColor", Color.clear);
                material.SetFloat("_FallbackLight", 0);
                material.SetFloat("_LightingMode", 1);
                material.SetFloat("_OutlineWidth", 0);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.position = new Vector3(0, -.1f, 0);
                floor.transform.localScale = new Vector3(12, .2f, 12);
                floor.GetComponent<Renderer>().sharedMaterial = material;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = new Vector3(0, .5f, 0);
                wall.transform.localScale = new Vector3(.4f, 1, 5);
                wall.GetComponent<Renderer>().sharedMaterial = material;
                var light = new GameObject("Moving light").AddComponent<Light>();
                light.type = LightType.Point;
                light.transform.position = new Vector3(-3, .65f, 0);
                light.range = 12;
                light.intensity = 5;
                light.shadowBias = .04f;
                light.shadowNormalBias = .15f;
                var camera = new GameObject("Validation camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 12, -10);
                camera.transform.LookAt(Vector3.zero);
                camera.orthographic = true;
                camera.orthographicSize = 6;
                camera.aspect = (float)Width / Height;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = true;
                camera.enabled = false;

                foreach (string tier in new[] { "Performant", "Balanced", "HighFidelity" })
                {
                    var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP-" + tier + ".asset");
                    Check(pipeline.supportsMainLightShadows && pipeline.supportsAdditionalLightShadows,
                        tier + " has shadows disabled.");
                    Check(pipeline.supportsHDR, tier + " cannot preserve HDR highlights for Bloom.");
                    GraphicsSettings.renderPipelineAsset = pipeline;
                    QualitySettings.renderPipeline = pipeline;
                    light.shadows = LightShadows.None;
                    Capture(camera, target, readback, null);
                    Capture(camera, target, readback, tier + "-unoccluded");
                    float unoccluded = Sample(camera, readback, new Vector3(2, .01f, 0));
                    light.shadows = LightShadows.Soft;
                    Capture(camera, target, readback, tier + "-shadow");
                    float blocked = Sample(camera, readback, new Vector3(2, .01f, 0));
                    Check(unoccluded > .08f && blocked < unoccluded * .6f,
                        tier + " wall did not block point light: " + blocked + " / " + unoccluded);
                    light.transform.position = new Vector3(3, .65f, 0);
                    Capture(camera, target, readback, tier + "-moved");
                    Check(Sample(camera, readback, new Vector3(2, .01f, 0)) > blocked + .08f,
                        tier + " shadow did not move with the light.");
                    light.transform.position = new Vector3(-3, .65f, 0);
                }

                Debug.Log("[Slima Atmosphere Validation] GPU PASS: all three quality tiers block point light and move shadows.");
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics;
                QualitySettings.renderPipeline = oldQuality;
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(readback);
                target.Release();
                Object.DestroyImmediate(target);
            }
            EditorSceneManager.OpenScene("Assets/Scenes/map01.unity");
            SessionState.SetBool(PlayKey, true);
            SessionState.SetInt(PlayKey + ".Stage", 0);
            SessionState.SetFloat(PlayKey + ".Deadline", (float)EditorApplication.timeSinceStartup + 90f);
            EditorApplication.update -= ValidatePlay;
            EditorApplication.update += ValidatePlay;
            EditorApplication.isPlaying = true;
        }

        static void ValidatePlay()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(PlayKey + ".Deadline", 0f))
                    throw new Exception("Play validation timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                var root = Object.FindObjectOfType<BlockMapGameRoot>();
                if (root == null || root.Party == null || Time.timeSinceLevelLoad < 1f) return;
                var actor = root.Party.ActiveActor;
                Check(actor != null, "Level did not create its player.");
                var glow = actor.GetComponentInChildren<Light>();
                Check(glow != null && glow.type == LightType.Point && glow.shadows != LightShadows.None,
                    "Player is missing its shadow-casting light.");
                if (SessionState.GetInt(PlayKey + ".Stage", 0) == 1)
                {
                    float low = Mathf.Min(SessionState.GetFloat(PlayKey + ".MinIntensity", float.MaxValue), glow.intensity);
                    float high = Mathf.Max(SessionState.GetFloat(PlayKey + ".MaxIntensity", 0f), glow.intensity);
                    float lowRange = Mathf.Min(SessionState.GetFloat(PlayKey + ".MinRange", float.MaxValue), glow.range);
                    float highRange = Mathf.Max(SessionState.GetFloat(PlayKey + ".MaxRange", 0f), glow.range);
                    SessionState.SetFloat(PlayKey + ".MinIntensity", low);
                    SessionState.SetFloat(PlayKey + ".MaxIntensity", high);
                    SessionState.SetFloat(PlayKey + ".MinRange", lowRange);
                    SessionState.SetFloat(PlayKey + ".MaxRange", highRange);
                    if (EditorApplication.timeSinceStartup < SessionState.GetFloat(PlayKey + ".BreathEnd", 0f)) return;
                    Check(high - low > actor.glowIntensity * .15f, "Breathing did not change light intensity.");
                    Check(highRange - lowRange > actor.glowRange * .04f, "Breathing did not change light range.");
                    Debug.Log("[Slima Atmosphere Validation] PLAY PASS: low player light follows movement/hop and stays below one-cell walls; reduced emission; breathing intensity/range; pale blue ambient; HDR/Bloom image difference; map01 rendered.");
                    Finish(0);
                    return;
                }
                Vector3 before = glow.transform.position;
                actor.transform.position += Vector3.right;
                actor.Mover.visual.localPosition += Vector3.up * .2f;
                actor.SendMessage("LateUpdate");
                Check(glow.transform.position.x > before.x + .9f && glow.transform.position.y > before.y + .1f,
                    "Player light did not follow movement and hop.");
                Check(glow.transform.position.y < actor.transform.position.y + actor.Mover.cellSize,
                    "Hopping light rose above a one-cell wall.");
                actor.SetState(new SlimeState { form = SlimeForm.Gas });
                actor.SendMessage("LateUpdate");
                Check(glow.transform.position.y <= actor.transform.position.y + .751f * actor.Mover.cellSize,
                    "Gas-form light rose above its height cap.");
                Check(actor.emissionStrength < .3f, "Player self-emission was not reduced.");
                actor.SetState(new SlimeState { element = SlimeElement.Red });
                Check(glow.color.r > glow.color.b, "Red slime did not recolor its light.");
                actor.SetState(new SlimeState { element = SlimeElement.Blue });
                Check(glow.color.b > glow.color.r, "Blue slime did not recolor its light.");
                actor.SetState(new SlimeState());
                actor.Mover.Teleport(actor.Mover.Cell, actor.Mover.Facing);
                actor.SendMessage("LateUpdate");
                foreach (Renderer item in root.GetComponentsInChildren<Renderer>())
                    if (item.name.StartsWith("Chunk_"))
                        Check(item.shadowCastingMode != ShadowCastingMode.Off && item.receiveShadows,
                            "Old baked terrain still has shadows disabled.");
                root.SendMessage("LateUpdate");
                Camera camera = Camera.main;
                Check(camera.allowHDR && camera.GetUniversalAdditionalCameraData().renderPostProcessing,
                    "Camera did not enable HDR/post-processing.");
                Check(RenderSettings.ambientLight.b > RenderSettings.ambientLight.r &&
                    RenderSettings.ambientLight.grayscale > .2f, "Pale blue scene ambient is missing.");
                var volume = root.GetComponentInChildren<Volume>();
                Bloom bloom = null;
                Check(volume != null && volume.sharedProfile.TryGet<Bloom>(out bloom) && bloom.IsActive(),
                    "Gameplay Bloom volume is missing.");
                camera.aspect = (float)Width / Height;
                var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                var readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
                Capture(camera, target, readback, null);
                Capture(camera, target, readback, "map01-atmosphere");
                Color[] withBloom = readback.GetPixels();
                bloom.active = false;
                Capture(camera, target, readback, "map01-no-bloom");
                Color[] withoutBloom = readback.GetPixels();
                int changes = 0;
                for (int i = 0; i < withBloom.Length; i++)
                    if (Mathf.Abs(withBloom[i].grayscale - withoutBloom[i].grayscale) > .002f) changes++;
                Check(changes > 100, "Bloom produced no visible highlight halo.");
                bloom.active = true;
                Object.Destroy(readback);
                target.Release();
                Object.Destroy(target);
                SessionState.SetInt(PlayKey + ".Stage", 1);
                SessionState.SetFloat(PlayKey + ".MinIntensity", glow.intensity);
                SessionState.SetFloat(PlayKey + ".MaxIntensity", glow.intensity);
                SessionState.SetFloat(PlayKey + ".MinRange", glow.range);
                SessionState.SetFloat(PlayKey + ".MaxRange", glow.range);
                SessionState.SetFloat(PlayKey + ".BreathEnd", (float)EditorApplication.timeSinceStartup + actor.glowBreathPeriod + .4f);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Finish(1);
            }
        }

        static void Finish(int code)
        {
            SessionState.SetBool(PlayKey, false);
            EditorApplication.update -= ValidatePlay;
            EditorApplication.Exit(code);
        }

        static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        static float Sample(Camera camera, Texture2D texture, Vector3 point)
        {
            Vector3 view = camera.WorldToViewportPoint(point);
            return texture.GetPixel(Mathf.Clamp((int)(view.x * Width), 0, Width - 1),
                Mathf.Clamp((int)(view.y * Height), 0, Height - 1)).grayscale;
        }

        static void Capture(Camera camera, RenderTexture target, Texture2D readback, string name)
        {
            // StandardRequest updates the camera's volume stack, including Bloom toggles.
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.StandardRequest { destination = target });
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            if (name == null) return;
            var preview = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            Color[] colors = readback.GetPixels();
            for (int i = 0; i < colors.Length; i++) colors[i] = colors[i].gamma;
            preview.SetPixels(colors);
            preview.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", preview.EncodeToPNG());
            Object.DestroyImmediate(preview);
        }
    }
}
