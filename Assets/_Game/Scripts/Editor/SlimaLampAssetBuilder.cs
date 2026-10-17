using System;
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
    // Same unit cube, 64px panel/frame and 8x8 auto-tile atlas as the original Lamp.
    public static class SlimaLampAssetBuilder
    {
        const string Definitions = "Assets/BlockMapEditor/Resources/BlockDefinitions/Lamps";
        const string Textures = "Assets/BlockMapEditor/Resources/LampBlocks";
        const string PreviewAssets = "Assets/BlockMapEditor/Art/Lamps/Showcase";
        const string Showcase = "Assets/Scenes/LampShowcase.unity";
        static readonly string[] Ids = { "FluorescentScreen_Lamp", "FloorLight_Lamp", "YellowLight_Lamp", "RedLight_Lamp", "StandingLight_Lamp" };
        static readonly string[] Names = { "荧光屏幕", "地板灯", "黄灯", "红灯", "立式灯" };
        static readonly Color[] Colors = { new Color(.2f, 1f, .78f), new Color(.55f, .9f, 1f), new Color(1f, .65f, .12f), new Color(1f, .12f, .08f), new Color(1f, .86f, .57f) };

        [MenuItem("Tools/Slima/灯具/生成五种 _Lamp 方块（补齐缺失）")]
        public static void BuildAssets()
        {
            Directory.CreateDirectory(Definitions); Directory.CreateDirectory(Textures);
            AssetDatabase.Refresh();
            var template = ReadOriginal("Lamp_panel.png"); var frame = ReadOriginal("Lamp_frame.png");
            var black = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var blackPixels = new Color[4096];
            for (int p = 0; p < blackPixels.Length; p++) blackPixels[p] = Color.black;
            black.SetPixels(blackPixels); black.Apply();
            try
            {
                for (int i = 0; i < Ids.Length; i++)
                {
                    // Otherwise Resource loading silently replaces the terrain with the old prop.
                    string oldPrefab = "Assets/BlockMapEditor/Resources/BlockModels/Lamps/" + Ids[i] + ".prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(oldPrefab) != null) AssetDatabase.DeleteAsset(oldPrefab);
                    string folder = Textures + "/" + Ids[i]; Directory.CreateDirectory(folder);
                    MakePanel(template, i, out var panel, out var mask);
                    var panelAsset = SaveTexture(folder + "/" + Ids[i] + "_panel.png", panel);
                    var frameAsset = SaveTexture(folder + "/" + Ids[i] + "_frame.png", frame);
                    var colorAtlas = AutoTileAtlas.Build(panelAsset, frameAsset);
                    var colorAsset = SaveTexture(folder + "/" + Ids[i] + "_atlas.png", colorAtlas, true);
                    var maskAsset = SaveTexture(folder + "/" + Ids[i] + "_emission.png", mask);
                    var emissionAtlas = AutoTileAtlas.Build(maskAsset, black);
                    var emissionAsset = SaveTexture(folder + "/" + Ids[i] + "_emission_atlas.png", emissionAtlas, true);
                    string path = Definitions + "/" + Ids[i] + ".asset";
                    var def = AssetDatabase.LoadAssetAtPath<BlockDefinition>(path);
                    bool initialize = def == null || def.previewPrefab != null || def.simpleTexture != null;
                    if (def == null) { def = ScriptableObject.CreateInstance<BlockDefinition>(); AssetDatabase.CreateAsset(def, path); }
                    def.id = Ids[i]; def.displayName = Names[i] + " _Lamp";
                    def.previewPrefab = null; def.interactive = false; def.mountOnWall = false;
                    def.simpleTexture = null; def.atlasOverride = colorAsset;
                    def.panelTexture = panelAsset; def.frameTexture = frameAsset; def.emissionMap = emissionAsset;
                    if (initialize)
                    {
                        def.renderMode = BlockRenderMode.Opaque; def.tint = Color.white;
                        def.emissionColor = Colors[i]; def.emissionStrength = 1.6f;
                        def.emitLight = true; def.lightColor = Colors[i]; def.lightRange = 4f;
                        def.lightIntensity = 1.6f; def.useLocalLightPosition = true;
                        def.lightLocalPosition = new Vector3(0, .6f, 0);
                    }
                    EditorUtility.SetDirty(def);
                    Object.DestroyImmediate(panel); Object.DestroyImmediate(mask);
                    Object.DestroyImmediate(colorAtlas); Object.DestroyImmediate(emissionAtlas);
                }
            }
            finally { Object.DestroyImmediate(template); Object.DestroyImmediate(frame); Object.DestroyImmediate(black); }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); BlockLibrary.Reload();
            Debug.Log("[Slima Lamps] Five 64px auto-tile lamp CUBES ready in BlockMap.");
        }

        static Texture2D ReadOriginal(string name)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes("Tools/BlockMapEditor/Resources/Lamp/" + name));
            if (tex.width != 64 || tex.height != 64) throw new Exception("Original Lamp tile size changed.");
            return tex;
        }

        static void MakePanel(Texture2D original, int kind, out Texture2D panel, out Texture2D mask)
        {
            panel = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            mask = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = original.GetPixels(); var emission = new Color[4096];
            Color casing = new Color(.21f, .24f, .28f), dark = new Color(.09f, .11f, .14f);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                int p = y * 64 + x; emission[p] = Color.black;
                if (kind == 2 || kind == 3)
                {
                    Color src = pixels[p];
                    bool lens = src.r > src.b + .08f && src.r > .35f && x > 12 && x < 51 && y > 12 && y < 51;
                    if (lens && kind == 3) pixels[p] = Color.Lerp(Colors[kind] * src.r, new Color(1, .72f, .5f), Mathf.Clamp01((src.g - .65f) * 2));
                    if (lens) emission[p] = Color.white * Mathf.Clamp01((src.g - .35f) * 1.6f);
                    continue;
                }
                if (x < 8 || x > 55 || y < 8 || y > 55) continue; // Original bevel and bolts.
                pixels[p] = casing * (((x / 2 + y / 2) % 2 == 0) ? 1f : .95f);
                bool lit = false; float energy = .55f;
                if (kind == 0)
                {
                    if (x >= 14 && x <= 49 && y >= 17 && y <= 46) pixels[p] = dark;
                    lit = x >= 17 && x <= 46 && y >= 20 && y <= 43;
                    if (lit) energy = y % 3 == 0 ? .42f : .62f;
                    if (lit && (((y == 25 || y == 26) && x >= 21 && x <= 37) || ((y == 31 || y == 32) && x >= 21 && x <= 42) || ((y == 37 || y == 38) && x >= 21 && x <= 31))) energy = 1;
                    if (y >= 13 && y <= 14 && x >= 18 && x <= 37 && x % 5 < 3) pixels[p] = dark;
                }
                else if (kind == 1)
                {
                    if (x >= 16 && x <= 47 && y >= 16 && y <= 47) pixels[p] = dark;
                    lit = x >= 19 && x <= 44 && y >= 19 && y <= 44 && !(x >= 30 && x <= 33 || y >= 30 && y <= 33);
                    energy = (x + y) % 5 == 0 ? .65f : .9f;
                }
                else
                {
                    if (x >= 23 && x <= 40 && y >= 14 && y <= 49) pixels[p] = dark;
                    lit = x >= 28 && x <= 35 && y >= 18 && y <= 45;
                    energy = x >= 30 && x <= 33 ? 1 : .6f;
                    if (x >= 20 && x <= 43 && (y >= 12 && y <= 14 || y >= 49 && y <= 51)) pixels[p] = new Color(.39f, .43f, .46f);
                }
                if (lit) { pixels[p] = Color.Lerp(Colors[kind] * .65f, Color.white, energy * .45f); emission[p] = Color.white * energy; }
            }
            panel.SetPixels(pixels); panel.Apply(); mask.SetPixels(emission); mask.Apply();
        }

        static Texture2D SaveTexture(string path, Texture2D texture, bool rebuild = false)
        {
            if (rebuild || !File.Exists(path)) File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = true; importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = !path.Contains("_emission");
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static void BuildAndValidateBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch-only validation.");
            BuildAssets(); ValidatePack(); BuildShowcase(); SlimaToonLightingValidation.Run();
            Debug.Log("[Slima Lamps Validation] PASS: unit cubes, 64px panel/frame, atlas emission, shared-face culling, bake inclusion, JSON, five lights and GPU lighting.");
        }

        static void ValidatePack()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Lamp validation");
            try
            {
                var world = root.AddComponent<BlockWorld>();
                var renderer = root.AddComponent<BlockChunkRenderer>(); renderer.skipInteractiveBlocks = true;
                for (int i = 0; i < Ids.Length; i++)
                {
                    var def = BlockLibrary.Get(Ids[i]);
                    if (def == null || !def.IsValid || !def.IsLamp || def.previewPrefab != null || def.interactive || !def.UseAutoTile || !def.emitLight)
                        throw new Exception("Not a terrain lamp cube: " + Ids[i]);
                    if (def.panelTexture.width != 64 || def.frameTexture.width != 64 || def.GetTexture().width != 512 || def.emissionMap.width != 512)
                        throw new Exception("Wrong BlockMap tile dimensions: " + Ids[i]);
                    Material mat = renderer.GetMaterial(def);
                    if (mat.shader.name != "Slima/Toon (Cel + Pixel)" || mat.GetTexture("_EmissionMap") != def.emissionMap || mat.GetFloat("_EmissionStrength") <= 0)
                        throw new Exception("Chunk emission binding failed: " + Ids[i]);
                    world.Set(Vector3Int.zero, BlockLibrary.IndexOf(Ids[i])); renderer.RebuildAll();
                    Mesh mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
                    if (mesh.vertexCount != 24 || mesh.bounds.size != Vector3.one) throw new Exception("Lamp is not one full grid cube.");
                    if (mesh.normals.Length != 24) throw new Exception("Lamp cube has no lighting normals.");
                    foreach (Vector3 n in mesh.normals)
                        if (Mathf.Abs(n.sqrMagnitude - 1) > .001f) throw new Exception("Invalid cube lighting normal.");
                    world.Set(Vector3Int.right, BlockLibrary.IndexOf(Ids[i])); renderer.RebuildAll();
                    mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
                    if (mesh.vertexCount != 40) throw new Exception("Lamp shared face was not culled.");
                    world.Clear(); renderer.RebuildAll();
                }
                for (int i = 0; i < Ids.Length; i++) world.Set(new Vector3Int(i * 2, 0, 0), BlockLibrary.IndexOf(Ids[i]));
                string json = world.ToJson(); world.Clear(); world.FromJson(json);
                if (world.Count != 5) throw new Exception("Lamp JSON round trip lost blocks.");
                root.AddComponent<BlockMapRuntimeSpawner>().Rebuild();
                if (root.GetComponentsInChildren<Light>().Length != 5) throw new Exception("Expected five lamp lights.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void BuildShowcase()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // NewScene unloads transient definitions built from the original Resources textures.
            BlockLibrary.Reload();
            Directory.CreateDirectory(PreviewAssets); AssetDatabase.Refresh();
            var stage = new GameObject("LampShowcase_BlockMap");
            var world = stage.AddComponent<BlockWorld>(); world.maxSize = new Vector3Int(20, 8, 8);
            int floor = BlockLibrary.IndexOf("Lab_Floor_01");
            if (floor < 0) throw new Exception("Original floor tile missing.");
            for (int x = 0; x < 15; x++) for (int z = 0; z < 3; z++) world.Set(new Vector3Int(x, 0, z), floor);
            if (world.Count != 45) throw new Exception("Showcase floor placement failed.");
            for (int i = 0; i < Ids.Length; i++) world.Set(new Vector3Int(1 + i * 3, 1, 1), BlockLibrary.IndexOf(Ids[i]));
            var chunks = stage.AddComponent<BlockChunkRenderer>(); chunks.RebuildAll();
            foreach (MeshFilter mf in stage.GetComponentsInChildren<MeshFilter>())
            {
                string path = PreviewAssets + "/" + mf.name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) { existing = Object.Instantiate(mf.sharedMesh); AssetDatabase.CreateAsset(existing, path); }
                else
                {
                    // Update native Mesh buffers too; CopySerialized alone can retain stale GPU data.
                    Mesh source = mf.sharedMesh;
                    existing.Clear(); existing.indexFormat = source.indexFormat;
                    existing.vertices = source.vertices; existing.normals = source.normals;
                    existing.uv = source.uv; existing.colors = source.colors;
                    existing.subMeshCount = source.subMeshCount;
                    for (int s = 0; s < source.subMeshCount; s++) existing.SetTriangles(source.GetTriangles(s), s);
                    existing.RecalculateBounds(); EditorUtility.SetDirty(existing);
                }
                mf.sharedMesh = existing;
                var mr = mf.GetComponent<MeshRenderer>(); var mats = mr.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    string mp = PreviewAssets + "/" + mats[m].name + ".mat";
                    var saved = AssetDatabase.LoadAssetAtPath<Material>(mp);
                    if (saved == null) { saved = new Material(mats[m]); AssetDatabase.CreateAsset(saved, mp); }
                    else { saved.CopyPropertiesFromMaterial(mats[m]); EditorUtility.SetDirty(saved); }
                    // Persist runtime-generated atlases for original floor blocks as well.
                    if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mats[m].mainTexture)))
                    {
                        var atlas = SaveTexture(PreviewAssets + "/" + mats[m].name + "_atlas.png", (Texture2D)mats[m].mainTexture, true);
                        saved.SetTexture("_BaseMap", atlas);
                    }
                    saved.SetFloat("_NoiseWorldSize", .04f); saved.SetFloat("_NoiseStrength", .85f);
                    mats[m] = saved;
                }
                mr.sharedMaterials = mats;
            }
            stage.AddComponent<BlockMapRuntimeSpawner>().Rebuild();
            if (world.Count != 50) throw new Exception("Showcase must retain 45 floor cells and five lamp cubes.");
            foreach (Light l in stage.GetComponentsInChildren<Light>())
            {
                var c = l.gameObject.AddComponent<SlimaToonLight>(); c.overrideMaterialSettings = true;
                c.worldPixelSize = .04f; c.transitionWidth = .85f;
            }
            var key = new GameObject("Showcase key light").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = .25f; key.transform.rotation = Quaternion.Euler(45, -25, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.fog = false;
            var camera = new GameObject("MainCamera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(8, 7.5f, -12); camera.transform.LookAt(new Vector3(7.5f, 1, 1.5f));
            camera.orthographic = true; camera.orthographicSize = 3.4f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .033f, .05f);
            camera.allowHDR = true; camera.allowMSAA = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            var volume = new GameObject("Lamp showcase bloom").AddComponent<Volume>(); volume.isGlobal = true;
            string profilePath = PreviewAssets + "/ShowcaseBloom_Lamp.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, profilePath);
                var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.1f);
                bloom.intensity.Override(.15f); bloom.scatter.Override(.4f); AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            volume.sharedProfile = profile; AssetDatabase.SaveAssets();
            // Showcase is a baked view of the actual chunk geometry; avoid runtime duplicate chunks.
            Object.DestroyImmediate(chunks);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Showcase);
            var oldGraphics = GraphicsSettings.renderPipelineAsset; var oldQuality = QualitySettings.renderPipeline;
            int oldAA = QualitySettings.antiAliasing;
            // Render with the real High Fidelity renderer, including its installed lighting and outlines.
            var pipeline = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP-HighFidelity.asset"));
            pipeline.renderScale = 1; pipeline.msaaSampleCount = 4; pipeline.maxAdditionalLightsCount = 8;
            var so = new SerializedObject(pipeline); so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1; so.ApplyModifiedPropertiesWithoutUndo();
            var rt = new RenderTexture(1280, 480, 24); var tex = new Texture2D(1280, 480, TextureFormat.RGB24, false);
            try
            {
                GraphicsSettings.renderPipelineAsset = pipeline; QualitySettings.renderPipeline = pipeline; camera.aspect = 1280f / 480;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                var previous = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 480), 0, 0); tex.Apply(); RenderTexture.active = previous;
                Directory.CreateDirectory("Docs/images"); File.WriteAllBytes("Docs/images/LampShowcase.png", tex.EncodeToPNG());
                var sheet = new Texture2D(640, 128, TextureFormat.RGB24, false);
                for (int i = 0; i < 5; i++)
                {
                    var tile = BlockLibrary.Get(Ids[i]).panelTexture;
                    for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) sheet.SetPixel(i * 128 + x, y, tile.GetPixel(x / 2, y / 2));
                }
                sheet.Apply(); File.WriteAllBytes("Docs/images/LampTiles.png", sheet.EncodeToPNG()); Object.DestroyImmediate(sheet);
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics; QualitySettings.renderPipeline = oldQuality; QualitySettings.antiAliasing = oldAA;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(pipeline);
            }
        }
    }
}
