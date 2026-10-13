using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.Rendering.EditorTools
{
    // GPU comparisons use cloned textures/materials and never save a user's scene.
    public static class SlimaTextureSamplingValidation
    {
        const int Width = 1280, Height = 720;
        const string Output = "Logs/TextureSampling";

        public static void Diagnose() => Run(false);
        public static void Validate() => Run(true);

        static void Run(bool expectFixed)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use Unity batch mode.");
            Directory.CreateDirectory(Output);
            var oldGraphics = GraphicsSettings.renderPipelineAsset;
            var oldQuality = QualitySettings.renderPipeline;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var renderer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/Settings/URP-Balanced-Renderer.asset"));
            renderer.rendererFeatures.Clear();
            var outline = ScriptableObject.CreateInstance<SlimaToonOutlineFeature>();
            outline.settings.pixelateAmount = 0;
            outline.settings.crispSpecials = false;
            outline.settings.protectNameFilter = "";
            outline.settings.protectMaterialFilter = "";
            foreach (var group in outline.settings.maskGroups) group.thickness = 0;
            renderer.rendererFeatures.Add(outline);
            outline.SetActive(false);
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            var material = new Material(Shader.Find("Slima/Toon (Cel + Pixel)"));
            var reference = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var textures = new List<Texture2D>();
            try
            {
                GraphicsSettings.renderPipelineAsset = pipeline;
                QualitySettings.renderPipeline = pipeline;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientIntensity = 0;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2();
                RenderSettings.fog = false;
                material.SetColor("_ShadowColor", Color.black);
                material.SetColor("_AmbientColor", Color.white);
                material.SetFloat("_FallbackLight", 0);
                material.SetFloat("_OutlineWidth", 0);
                material.SetFloat("_PixelSnap", 0);
                material.SetFloat("_NoiseStrength", 0);
                var floor = new GameObject("Sampling floor");
                Mesh mesh = CreateFloor();
                floor.AddComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = floor.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                var camera = new GameObject("Sampling camera").AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 4.5f;
                camera.aspect = (float)Width / Height;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.enabled = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                var points = new List<Vector3>();
                for (int z = 0; z < 25; z++)
                    for (int x = 0; x < 25; x++) points.Add(new Vector3(-.86f + x * .07231f, 0, -.81f + z * .06457f));

                Texture2D Load(FilterMode mode, bool mipmaps)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps, false);
                    texture.LoadImage(File.ReadAllBytes("Assets/BlockMapEditor/Built/Textures/Lab_Floor_02_atlas.png"));
                    texture.filterMode = mode;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.Apply(mipmaps, false);
                    textures.Add(texture);
                    return texture;
                }

                var point = Load(FilterMode.Point, false);
                var bilinear = Load(FilterMode.Bilinear, false);
                var mip = Load(FilterMode.Trilinear, true);
                Color[] Capture()
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    readback.Apply();
                    RenderTexture.active = previous;
                    return readback.GetPixels();
                }

                Color[] firstPoint = null, firstReference = null, firstPost = null;
                double Compare(string name, Texture2D texture, Material selected, bool post)
                {
                    meshRenderer.sharedMaterial = selected;
                    selected.SetTexture("_BaseMap", texture);
                    outline.SetActive(post);
                    var sums = new double[points.Count];
                    var squares = new double[points.Count];
                    const int frames = 16;
                    for (int f = 0; f < frames; f++)
                    {
                        var focus = new Vector3(0, 0, f * .0041f);
                        camera.transform.position = focus + new Vector3(0, 18, -16);
                        camera.transform.LookAt(focus);
                        var pixels = Capture();
                        if (f == 0)
                        {
                            if (name == "toon-point") firstPoint = pixels;
                            if (name == "unlit-point") firstReference = pixels;
                            if (name == "toon-point-post") firstPost = pixels;
                            WritePreview(name, pixels);
                        }
                        for (int p = 0; p < points.Count; p++)
                        {
                            Vector3 screen = camera.WorldToViewportPoint(points[p]);
                            float value = Sample(pixels, screen.x * Width - .5f, screen.y * Height - .5f).grayscale;
                            sums[p] += value;
                            squares[p] += value * value;
                        }
                    }
                    double deviation = 0;
                    for (int p = 0; p < points.Count; p++)
                        deviation += Math.Sqrt(Math.Max(0, squares[p] / frames - Math.Pow(sums[p] / frames, 2)));
                    deviation /= points.Count;
                    Debug.Log($"[Sampling] {name}: motion deviation={deviation:F6}");
                    return deviation;
                }

                Compare("unlit-point", point, reference, false);
                double noisy = Compare("toon-point", point, material, false);
                Compare("toon-point-post", point, material, true);
                double smooth = Compare("toon-bilinear", bilinear, material, false);
                Compare("toon-mip", mip, material, false);
                float Difference(Color[] a, Color[] b)
                {
                    float total = 0;
                    for (int y = 220; y < 500; y++)
                        for (int x = 360; x < 920; x++) total += Mathf.Abs(a[y * Width + x].r - b[y * Width + x].r);
                    return total / (280 * 560);
                }
                float shaderDifference = Difference(firstPoint, firstReference);
                float postDifference = Difference(firstPoint, firstPost);
                Debug.Log($"[Sampling] toon vs unlit mean error={shaderDifference:F6}; post on vs off={postDifference:F6}; bilinear motion ratio={smooth / noisy:F3}");
                if (shaderDifference > .005f) throw new Exception("Toon shader changed the base texture UV sampling.");
                if (smooth >= noisy * .8) throw new Exception("Filtering failed to reduce motion shimmer.");
                CheckCompositeUV(expectFixed);
                if (expectFixed)
                {
                    foreach (string path in Directory.GetFiles("Assets/BlockMapEditor/Built/Textures", "Lab_Floor*_atlas.png"))
                    {
                        var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                        if (importer.filterMode != FilterMode.Bilinear || importer.mipmapEnabled)
                            throw new Exception("Floor importer does not use atlas-safe bilinear filtering: " + path);
                    }
                    var definition = BlockMap.BlockLibrary.Get(BlockMap.BlockLibrary.IndexOf("Lab_Floor_02"));
                    if (definition.GetTexture().filterMode != FilterMode.Bilinear)
                        throw new Exception("Runtime floor atlas reverted to Point filtering.");
                }
                Debug.Log("[Sampling] " + (expectFixed ? "VALIDATION PASSED" : "DIAGNOSIS COMPLETE"));
                Object.DestroyImmediate(mesh);
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics;
                QualitySettings.renderPipeline = oldQuality;
                foreach (Texture2D texture in textures) Object.DestroyImmediate(texture);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(reference);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(pipeline);
                Object.DestroyImmediate(outline);
                Object.DestroyImmediate(renderer);
            }
        }

        static Mesh CreateFloor()
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            // Interior tile 27 has no border; all quads use the exact baked half-texel inset.
            float min = 3f / 8f + .5f / 512f, max = 4f / 8f - .5f / 512f;
            for (int z = -8; z < 8; z++)
                for (int x = -8; x < 8; x++)
                {
                    int i = vertices.Count;
                    vertices.Add(new Vector3(x, 0, z)); vertices.Add(new Vector3(x, 0, z + 1));
                    vertices.Add(new Vector3(x + 1, 0, z + 1)); vertices.Add(new Vector3(x + 1, 0, z));
                    uv.Add(new Vector2(min, min)); uv.Add(new Vector2(min, max));
                    uv.Add(new Vector2(max, max)); uv.Add(new Vector2(max, min));
                    for (int c = 0; c < 4; c++) colors.Add(Color.white);
                    triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static Color Sample(Color[] pixels, float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            return Color.Lerp(Color.Lerp(pixels[iy * Width + ix], pixels[iy * Width + ix + 1], x - ix),
                Color.Lerp(pixels[(iy + 1) * Width + ix], pixels[(iy + 1) * Width + ix + 1], x - ix), y - iy);
        }

        static void WritePreview(string name, Color[] pixels)
        {
            var preview = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
            preview.SetPixels(pixels); preview.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", preview.EncodeToPNG());
            Object.DestroyImmediate(preview);
            // Restore linear values for the numerical comparison.
            for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].linear;
        }

        static void CheckCompositeUV(bool expectFixed)
        {
            const int size = 128;
            var gradient = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) pixels[y * size + x] = new Color((x + .5f) / size, 0, 0, 1);
            gradient.SetPixels(pixels); gradient.Apply(); gradient.wrapMode = TextureWrapMode.Clamp;
            var material = new Material(Shader.Find("Slima/Outline Composite"));
            material.SetFloat("_PixelatePass", 1); material.SetFloat("_PixelateAmount", 0);
            material.SetTexture("_SlimaCrispMask", Texture2D.blackTexture);
            material.SetFloat("_CrispDilate", 0);
            var target = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            var readback = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            var command = new CommandBuffer();
            command.SetRenderTarget(target); command.SetGlobalTexture("_BlitTexture", gradient);
            command.SetGlobalVector("_BlitScaleBias", new Vector4(.75f, .75f, 0, 0));
            command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(command);
            var previous = RenderTexture.active;
            RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, size, size), 0, 0); readback.Apply();
            RenderTexture.active = previous;
            float error = 0;
            for (int x = 12; x < 116; x++) error = Mathf.Max(error, Mathf.Abs(readback.GetPixel(x, 64).r - (x + .5f) / size * .75f));
            Debug.Log($"[Sampling] composite scaled UV max error={error:F6}");
            command.Release();
            Object.DestroyImmediate(gradient); Object.DestroyImmediate(material);
            Object.DestroyImmediate(target); Object.DestroyImmediate(readback);
            if (expectFixed && error > .002f) throw new Exception("Composite applies RTHandle scaling more than once.");
        }
    }
}
