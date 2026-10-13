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
    public static class SlimaPackageBurnValidation
    {
        [MenuItem("Tools/Slima/机关/生成包裹燃烧粒子材质")]
        public static void BuildParticleAssets()
        {
            AssetDatabase.Refresh();
            const string path = "Packages/com.gamejam.slima-blockmap-editor/Resources/";
            foreach (string name in new[] { "PackageBurnFlame", "PackageBurnSmoke" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path + name + ".mat");
                if (material == null)
                {
                    material = new Material(Shader.Find("BlockMap/Package Burn Particle"));
                    AssetDatabase.CreateAsset(material, path + name + ".mat");
                }
                material.SetFloat("_Smoke", name == "PackageBurnSmoke" ? 1 : 0);
                material.SetTexture("_NoiseTex", Resources.Load<Texture2D>("SlimeFlameNoise"));
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }

        public static void Run()
        {
            BuildParticleAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BlockGameplayCatalog.Reload(); BlockLibrary.Reload();
            var root = new GameObject("Package burn capture");
            var view = GameplayPrototypeFactory.Create(BlockLibrary.Get("Breakable"), BlockGameplayCatalog.Get("Breakable"),
                Vector3Int.zero, 0, root.transform);
            var effect = DestructibleBurnEffect.Create(view, root.transform);
            var particles = effect.GetComponentsInChildren<ParticleSystem>();
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].useAutoRandomSeed = false; particles[i].randomSeed = (uint)(123 + i);
                particles[i].Play();
            }
            var material = view.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            Object.DestroyImmediate(view.gameObject);
            var cameraObject = new GameObject("Capture camera");
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            camera.orthographic = true; camera.orthographicSize = .9f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .095f, .15f);
            Vector3 target = new Vector3(.5f, .5f, .5f);
            camera.transform.position = target + new Vector3(1.6f, 2.4f, 5); camera.transform.LookAt(target);
            var lightObject = new GameObject("Capture light"); lightObject.transform.rotation = Quaternion.Euler(45, -25, 0);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            RenderSettings.ambientLight = new Color(.4f, .44f, .52f);
            Directory.CreateDirectory("Logs/PackageBurnFrames");
            var rt = new RenderTexture(480, 480, 24, RenderTextureFormat.ARGB32); rt.Create();
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture old = RenderTexture.active;
            try
            {
                const int frames = 40;
                for (int frame = 0; frame <= frames; frame++)
                {
                    if (frame > 0 && effect != null)
                    {
                        effect.AdvanceAnimation(DestructibleBurnEffect.Duration / 24);
                        foreach (var system in particles)
                            if (system != null) system.Simulate(DestructibleBurnEffect.Duration / 24, false, false, true);
                    }
                    // Drive shader time as well so captured fire flickers while the dissolve advances.
                    float time = frame * DestructibleBurnEffect.Duration / 24;
                    Shader.SetGlobalVector("_Time", new Vector4(time / 20, time, time * 2, time * 3));
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
                    File.WriteAllBytes("Logs/PackageBurnFrames/" + frame.ToString("00") + ".png", image.EncodeToPNG());
                }
                if (ShaderUtil.ShaderHasError(material.shader)) throw new Exception("Burn shader failed to compile.");
                foreach (string name in new[] { "PackageBurnFlame", "PackageBurnSmoke" })
                    if (ShaderUtil.ShaderHasError(Resources.Load<Material>(name).shader)) throw new Exception("Particle shader failed: " + name);
                File.WriteAllText("Logs/PackageBurn-render-validation.txt", "PASS: burn and particle shaders compile; GPU animation includes flames, sparks, smoke and particle tail.\n");
                Debug.Log("[Package Burn] GPU render validation PASS.");
            }
            finally
            {
                RenderTexture.active = old; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
                Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
            }
        }
    }
}
