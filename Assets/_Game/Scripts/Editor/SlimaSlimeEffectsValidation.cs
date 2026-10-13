using System;
using System.IO;
using BlockMap.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaSlimeEffectsValidation
    {
        const string ResourcesPath = "Tools/BlockMapEditor/Resources/";
        const string ResourcesAssetPath = "Packages/com.gamejam.slima-blockmap-editor/Resources/";

        [MenuItem("Tools/Slima/验证/史莱姆元素特效")]
        public static void Run()
        {
            BuildAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = Resources.Load<GameObject>("Player/BlockMapPlayer");
            Require(prefab != null, "Missing slime prefab.");
            var root = Object.Instantiate(prefab);
            var actor = root.AddComponent<SlimeActor>();
            actor.Initialize(new SlimeState(), null, 1f, 0f);
            actor.Mover.Teleport(Vector3Int.zero, 0);
            int objects = root.GetComponentsInChildren<Transform>(true).Length;
            actor.Initialize(new SlimeState(), null, 1f, 0f);
            Require(root.GetComponentsInChildren<Transform>(true).Length == objects, "Repeated initialization duplicated effects.");
            var particles = root.GetComponentInChildren<ParticleSystem>();
            Require(particles != null && particles.emission.rateOverTime.constant == 10f, "Particle rate is not 10/s.");
            Require(particles.noise.enabled && particles.noise.strength.constant > 0, "Particles do not wander.");
            var halo = root.transform.Find("Visual/Flowing Fire Glow").GetComponent<Renderer>();
            var flames = root.transform.Find("Visual/Pixel Flames").GetComponent<Renderer>();
            var body = root.transform.Find("Visual/Body").GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            var glow = root.GetComponentInChildren<Light>();
            foreach (SlimeElement element in new[] { SlimeElement.Red, SlimeElement.Blue, SlimeElement.Neutral, SlimeElement.Red })
            {
                actor.SetState(new SlimeState { element = element });
                body.GetPropertyBlock(properties);
                Require(properties.GetFloat("_SlimeEyes") == 1 && properties.GetTexture("_SlimeEyeMap") != null, "Eyes are missing.");
                Require(properties.GetFloat("_SlimeFireFlow") == (element == SlimeElement.Red ? 1 : 0), "Wrong body flow state.");
                Require(halo.enabled == (element == SlimeElement.Red), "Wrong halo state.");
                Require(flames.enabled == (element == SlimeElement.Red), "Wrong external flame state.");
                Require(properties.GetFloat("_SlimeSnowCap") == (element == SlimeElement.Blue ? 1 : 0), "Wrong snow covering state.");
                if (element == SlimeElement.Blue)
                    Require(glow.color.b > glow.color.r * 4, "Ice light is not blue.");
                if (element == SlimeElement.Red)
                    Require(glow.color.r > glow.color.g && glow.color.g > glow.color.b, "Fire light is not orange.");
                particles.Simulate(1.2f, true, true, true);
                actor.SetState(new SlimeState { element = element, form = SlimeForm.Gas });
                body.GetPropertyBlock(properties);
                Require(!halo.enabled && !flames.enabled && properties.GetFloat("_SlimeSnowCap") == 0
                    && particles.particleCount == 0, "Gas retained elemental effects.");
                Require(root.GetComponentsInChildren<Transform>(true).Length == objects, "State switch created duplicate effects.");
            }
            actor.SetState(new SlimeState { element = SlimeElement.Red, size = SlimeSize.Small });
            halo.GetPropertyBlock(properties);
            Require(Mathf.Abs(properties.GetFloat("_Size") - 1.7f * 0.72f) < 0.001f, "Small slime halo scale is wrong.");
            flames.GetPropertyBlock(properties);
            Require(Mathf.Abs(properties.GetFloat("_Height") - 1.25f * 0.72f) < 0.001f, "Small external flame scale is wrong.");
            Require(flames.sharedMaterial.GetTexture("_NoiseTex").filterMode == FilterMode.Point, "Flame noise is not point sampled.");
            var eyes = Resources.Load<Texture2D>("SlimeEye");
            Require(eyes.width == 16 && eyes.height == 32 && eyes.filterMode == FilterMode.Point, "Eyes are not upright pixel bars.");
            var eyePixels = new Texture2D(2,2);
            eyePixels.LoadImage(File.ReadAllBytes(ResourcesPath + "SlimeEye.png"));
            Require(eyePixels.GetPixel(4,2).a == 1 && eyePixels.GetPixel(11,29).a == 1
                && eyePixels.GetPixel(0,0).a == 0, "Tall eye mask is wrong.");
            Color eyeDark = eyePixels.GetPixel(8,12), eyeHighlight = eyePixels.GetPixel(6,23);
            Require(eyeDark.r < 0.1f && eyeDark.g < 0.1f && eyeDark.b < 0.1f
                && eyeHighlight == Color.white, "Black eye or upper-left white highlight is wrong.");
            Object.DestroyImmediate(eyePixels);
            foreach (string name in new[] { "SlimeElementHalo", "SlimeFireParticle", "SlimeSnowParticle", "SlimeFlame" })
                Require(!ShaderUtil.ShaderHasError(Resources.Load<Material>(name).shader), "Shader error: " + name);
            Require(!ShaderUtil.ShaderHasError(body.sharedMaterial.shader), "Toon shader failed.");
            Object.DestroyImmediate(root);
            CapturePreview(prefab);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/SlimeEffects-validation.txt",
                "PASS: 10 particles/s, wandering noise, fire/ice/neutral/gas transitions, particle clearing, blue/orange lights, small halo/flame scale, no duplicates, upright black pixel eyes with upper-left white highlights, point-sampled noise texture, snow covering, shader compilation, GPU four-direction captures and animated flame differences.\n");
            Debug.Log("[Slime Effects] Validation PASS.");
        }

        static void BuildAssets()
        {
            var texture = new Texture2D(16, 32, TextureFormat.RGBA32, false);
            var pixels = new Color[16 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 16; x++)
                {
                    bool inside = x >= 3 && x <= 12 && y >= 2 && y <= 29
                        && !((x == 3 || x == 12) && (y == 2 || y == 29));
                    bool shine = x >= 5 && x <= 7 && y >= 22 && y <= 24;
                    pixels[y * 16 + x] = inside ? (shine ? Color.white : new Color(0.025f,0.04f,0.07f)) : Color.clear;
                }
            texture.SetPixels(pixels); texture.Apply();
            File.WriteAllBytes(ResourcesPath + "SlimeEye.png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(ResourcesAssetPath + "SlimeEye.png", ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(ResourcesAssetPath + "SlimeEye.png");
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Point;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            var eye = AssetDatabase.LoadAssetAtPath<Texture2D>(ResourcesAssetPath + "SlimeEye.png");
            CreateMaterial("SlimeElementHalo", "BlockMap/Slime Flowing Halo", 0);
            CreateMaterial("SlimeFireParticle", "BlockMap/Slime Element Particle", 0);
            CreateMaterial("SlimeSnowParticle", "BlockMap/Slime Element Particle", 1);
            BuildFlameNoise();
            var playerMaterial = Resources.Load<Material>("Player/BlockMapPlayer");
            playerMaterial.SetTexture("_SlimeEyeMap", eye);
            playerMaterial.SetFloat("_SlimeEyes", 1);
            EditorUtility.SetDirty(playerMaterial);
            const string prefabPath = "Assets/BlockMapEditor/Resources/Player/BlockMapPlayer.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var facing = prefab.transform.Find("Visual/Body/Facing");
                if (facing != null) Object.DestroyImmediate(facing.gameObject);
                PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
        }

        static void CreateMaterial(string name, string shaderName, float snow)
        {
            string path = ResourcesAssetPath + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shaderName));
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_Snow")) material.SetFloat("_Snow", snow);
            EditorUtility.SetDirty(material);
        }

        static void BuildFlameNoise()
        {
            const int size = 64;
            var noise = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var colors = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float sum = 0, amplitude = 0.57f;
                    for (int octave = 0; octave < 3; octave++)
                    {
                        float period = 4 << octave;
                        float a = Mathf.PerlinNoise(u * period + 17, v * period + 31);
                        float b = Mathf.PerlinNoise((u-1) * period + 17, v * period + 31);
                        float c = Mathf.PerlinNoise(u * period + 17, (v-1) * period + 31);
                        float d = Mathf.PerlinNoise((u-1) * period + 17, (v-1) * period + 31);
                        sum += Mathf.Lerp(Mathf.Lerp(a,b,u), Mathf.Lerp(c,d,u), v) * amplitude;
                        amplitude *= 0.5f;
                    }
                    float value = Mathf.Clamp01((sum - 0.5f) * 2.4f + 0.5f);
                    colors[y * size + x] = new Color(value, value, value, 1);
                }
            noise.SetPixels(colors); noise.Apply();
            File.WriteAllBytes(ResourcesPath + "SlimeFlameNoise.png", noise.EncodeToPNG());
            Object.DestroyImmediate(noise);
            string path = ResourcesAssetPath + "SlimeFlameNoise.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
            CreateMaterial("SlimeFlame", "BlockMap/Slime Pixel Flames", 0);
            var material = AssetDatabase.LoadAssetAtPath<Material>(ResourcesAssetPath + "SlimeFlame.mat");
            material.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            EditorUtility.SetDirty(material);
        }

        static void CapturePreview(GameObject prefab)
        {
            var camera = new GameObject("Slime preview camera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.06f, 0.09f);
            camera.allowHDR = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            camera.transform.position = new Vector3(0, 2.4f, 5);
            camera.transform.LookAt(new Vector3(0,0.5f,0));
            var light = new GameObject("Preview key").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 0.8f;
            light.transform.rotation = Quaternion.Euler(45, -25, 0);
            RenderSettings.ambientLight = new Color(0.2f, 0.25f, 0.3f); RenderSettings.fog = false;
            var rt = new RenderTexture(384, 384, 24);
            rt.Create();
            var image = new Texture2D(384, 384, TextureFormat.RGBA32, false);
            var sheet = new Texture2D(1536, 1152, TextureFormat.RGBA32, false);
            Directory.CreateDirectory("Docs/images");
            try
            {
                for (int row = 0; row < 3; row++)
                {
                    var instance = Object.Instantiate(prefab);
                    var actor = instance.AddComponent<SlimeActor>();
                    actor.Initialize(new SlimeState { element = (SlimeElement)row }, null, 1, 0);
                    for (int column = 0; column < 4; column++)
                    {
                        instance.transform.rotation = Quaternion.Euler(0, column * 90, 0);
                        instance.GetComponentInChildren<ParticleSystem>().Simulate(1.2f, true, true, true);
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                        var old = RenderTexture.active; RenderTexture.active = rt;
                        image.ReadPixels(new Rect(0,0,384,384),0,0); image.Apply(); RenderTexture.active = old;
                        sheet.SetPixels(column * 384, (2-row) * 384, 384, 384, image.GetPixels());
                        if (row == 1 && column == 0)
                            CaptureFlameAnimation(instance, camera, rt, image);
                    }
                    Object.DestroyImmediate(instance);
                }
                sheet.Apply();
                File.WriteAllBytes("Docs/images/SlimeElements.png", sheet.EncodeToPNG());
                foreach (string name in new[] { "Player/BlockMapPlayer", "SlimeFlame" })
                    Require(!ShaderUtil.ShaderHasError(Resources.Load<Material>(name).shader), "GPU compile failed: " + name);
            }
            finally { rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image); Object.DestroyImmediate(sheet); }
        }

        static void CaptureFlameAnimation(GameObject instance, Camera camera, RenderTexture rt, Texture2D image)
        {
            var flames = instance.transform.Find("Visual/Pixel Flames").GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            flames.GetPropertyBlock(properties);
            Color[] first = null, last = null;
            Directory.CreateDirectory("Logs/SlimeFlameFrames");
            for (int frame = 0; frame < 12; frame++)
            {
                properties.SetFloat("_FlowTime", frame * 0.1f);
                flames.SetPropertyBlock(properties);
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                var old = RenderTexture.active; RenderTexture.active = rt;
                image.ReadPixels(new Rect(0,0,384,384),0,0); image.Apply(); RenderTexture.active = old;
                if (frame == 0) first = image.GetPixels();
                if (frame == 11) last = image.GetPixels();
                File.WriteAllBytes("Logs/SlimeFlameFrames/" + frame.ToString("D2") + ".png", image.EncodeToPNG());
            }
            properties.SetFloat("_FlowTime", -1); flames.SetPropertyBlock(properties);
            int changed = 0;
            for (int i = 0; i < first.Length; i++)
                if (Mathf.Abs(first[i].r-last[i].r) + Mathf.Abs(first[i].g-last[i].g) + Mathf.Abs(first[i].b-last[i].b) > 0.08f) changed++;
            Require(changed > 100, "External pixel flames did not animate on the GPU.");
        }

        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
