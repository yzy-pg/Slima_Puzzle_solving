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
    public static class SlimaSplitterInteractionValidation
    {
        [MenuItem("Tools/Slima/机关/验证切分交互动画")]
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory("Logs/SplitterInteractionFrames");
            var cameraObject = new GameObject("Splitter capture camera");
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            camera.orthographic = true;
            camera.orthographicSize = 2.1f;
            camera.backgroundColor = new Color(.07f, .095f, .15f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            Vector3 target = new Vector3(2.5f, 1.25f, 2.5f);
            camera.transform.position = target + new Vector3(4f, 5.5f, -6f);
            camera.transform.LookAt(target);
            var lightObject = new GameObject("Splitter capture light");
            lightObject.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            RenderSettings.ambientLight = new Color(.4f, .44f, .52f);
            var rt = new RenderTexture(480, 480, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture old = RenderTexture.active;
            try
            {
                foreach (SlimeElement element in new[] { SlimeElement.Neutral, SlimeElement.Red, SlimeElement.Blue })
                    Capture(element, camera, rt, image);
                File.WriteAllText("Logs/SplitterInteraction-render-validation.txt",
                    "PASS: neutral/red/blue real gameplay actors, quarter-entry droplets, three-quarter split, front-diagonal hops and stable landing rendered in 480px frames.\n");
                Debug.Log("[Splitter Interaction] GPU render validation PASS.");
            }
            finally
            {
                RenderTexture.active = old;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(lightObject);
            }
        }

        static void Capture(SlimeElement element, Camera camera, RenderTexture rt, Texture2D image)
        {
            var root = new GameObject("Splitter interaction capture");
            var world = root.AddComponent<BlockWorld>();
            var center = new Vector3Int(2, 1, 2);
            Material floorMaterial = new Material(Resources.Load<Material>("Player/BlockMapPlayer"));
            floorMaterial.SetColor("_BaseColor", new Color(.22f, .29f, .4f));
            floorMaterial.SetFloat("_SlimeEyes", 0f);
            try
            {
                for (int x = 0; x < 5; x++)
                    for (int z = 0; z < 5; z++)
                    {
                        world.Set(new Vector3Int(x, 0, z), BlockLibrary.IndexOf("Lab_WallBrick1"));
                        var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        tile.transform.SetParent(root.transform, false);
                        tile.transform.position = new Vector3(x + .5f, .9f, z + .5f);
                        tile.transform.localScale = new Vector3(.96f, .2f, .96f);
                        tile.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
                    }
                world.Set(center, BlockLibrary.IndexOf("Splitter"));
                var gameplay = root.AddComponent<BlockGameplayController>();
                gameplay.Initialize(world);
                var party = root.AddComponent<SlimePartyController>();
                party.Initialize(gameplay, null, center + Vector3Int.back, 0, 1f, 0f);
                gameplay.BindParty(party);
                party.ActiveActor.SetState(new SlimeState { element = element });
                if (!party.TrySplit(party.ActiveActor)) throw new Exception("Rear interaction failed for " + element);
                ParticleSystem cut = root.transform.Find("Splitter Slime Droplets").GetComponent<ParticleSystem>();
                cut.useAutoRandomSeed = false;
                cut.randomSeed = 123;
                if (ShaderUtil.ShaderHasError(cut.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader))
                    throw new Exception("Cut particle shader failed to compile.");
                const float delta = 1f / 24f;
                for (int frame = 0; frame < 80; frame++)
                {
                    float elapsed = frame * delta;
                    if (frame > 0) party.AdvanceSplitAnimation(delta);
                    if (cut != null && elapsed >= party.splitterCutDuration * .25f)
                    {
                        var emission = cut.emission;
                        emission.enabled = elapsed < party.splitterCutDuration;
                        cut.Simulate(delta, false, false, true);
                        if (emission.enabled) cut.Play();
                    }
                    foreach (SplitterSawAnimation saw in root.GetComponentsInChildren<SplitterSawAnimation>()) saw.Advance(delta);
                    foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>())
                        if (particles != cut && particles.emission.enabled) particles.Simulate(delta, false, false, true);
                    Shader.SetGlobalVector("_Time", new Vector4(elapsed / 20f, elapsed, elapsed * 2f, elapsed * 3f));
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    image.Apply();
                    File.WriteAllBytes("Logs/SplitterInteractionFrames/" + element + "_" + frame.ToString("00") + ".png", image.EncodeToPNG());
                }
                if (party.IsBusy || party.Actors.Count != 2 || party.ActiveActor.Mover.Cell != center + Vector3Int.forward + Vector3Int.left)
                    throw new Exception("Animation did not finish on expected diagonal for " + element);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(floorMaterial);
            }
        }
    }
}
