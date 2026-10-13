using System;
using System.IO;
using BlockMap;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.Rendering.EditorTools
{
    // Import the approved raster artwork at the existing BlockMap tile resolution.
    // Keep resource paths/metas intact so baked and runtime materials both update.
    public static class SlimaLampStripeTextures
    {
        static readonly string[] Ids = { "FluorescentScreen_Lamp", "FloorLight_Lamp", "YellowLight_Lamp", "RedLight_Lamp", "StandingLight_Lamp" };

        [MenuItem("Tools/Slima/灯具/替换为现有灯色横纹面板")]
        public static void Apply()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-lampSourceProject") root = Path.GetFullPath(args[i + 1]);
            var sheet = new Texture2D(1280, 256, TextureFormat.RGB24, false);
            var black = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var blackPixels = new Color[4096];
            for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
            black.SetPixels(blackPixels); black.Apply();
            try
            {
                for (int i = 0; i < Ids.Length; i++)
                {
                    string id = Ids[i];
                    string folder = Path.Combine(root, "Assets/BlockMapEditor/Resources/LampBlocks", id);
                    string panelPath = Path.Combine(folder, id + "_panel.png");
                    string framePath = Path.Combine(folder, id + "_frame.png");
                    string atlasPath = Path.Combine(folder, id + "_atlas.png");
                    string emissionPath = Path.Combine(folder, id + "_emission.png");
                    string emissionAtlasPath = Path.Combine(folder, id + "_emission_atlas.png");
                    string[] paths = { panelPath, framePath, atlasPath, emissionPath, emissionAtlasPath };
                    var metas = new string[paths.Length];
                    for (int m = 0; m < paths.Length; m++) metas[m] = File.ReadAllText(paths[m] + ".meta");
                    var source = Read(Path.Combine(root, "output/imagegen/LampStripes", id + ".png"));
                    var frame = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                    var panel = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                    var emission = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                    var pixels = new Color[4096]; var mask = new Color[4096];
                    for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                        pixels[y * 64 + x] = source.GetPixel(Mathf.FloorToInt((x + .5f) * source.width / 64),
                            Mathf.FloorToInt((y + .5f) * source.height / 64));
                    float low = 1, high = 0;
                    for (int y = 10; y <= 54; y++)
                    {
                        float value = Peak(pixels[y * 64 + 32]); low = Mathf.Min(low, value); high = Mathf.Max(high, value);
                    }
                    int bands = 0; bool previous = false;
                    for (int y = 10; y <= 54; y++)
                    {
                        bool bright = Peak(pixels[y * 64 + 32]) > (low + high) * .5f;
                        if (y == 10 || bright != previous) bands++;
                        previous = bright;
                    }
                    if (bands != 18) throw new Exception(id + " must preserve 18 horizontal bands, found " + bands);
                    for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                    {
                        // Stepped inner window and the two small lower indicator lenses.
                        bool inset = x >= 9 && x <= 54 && y >= 10 && y <= 54 &&
                            ((y >= 14 && y <= 50) || (x >= 13 && x <= 50));
                        bool indicator = y >= 7 && y <= 8 && ((x >= 22 && x <= 28) || (x >= 35 && x <= 42)) &&
                            Peak(pixels[y * 64 + x]) > .6f;
                        float energy = Mathf.Lerp(.35f, 1, Mathf.Clamp01((Peak(pixels[y * 64 + x]) - low) / Mathf.Max(.01f, high - low)));
                        mask[y * 64 + x] = inset || indicator ? Color.white * energy : Color.black;
                    }
                    panel.SetPixels(pixels); panel.Apply(); emission.SetPixels(mask); emission.Apply();
                    // AutoTileAtlas only reads the edge bands/corners of the frame texture.
                    frame.SetPixels(pixels); frame.Apply();
                    var atlas = AutoTileAtlas.Build(panel, frame);
                    var emissionAtlas = AutoTileAtlas.Build(emission, black);
                    if (frame.width != 64 || atlas.width != 512 || emissionAtlas.width != 512)
                        throw new Exception("BlockMap atlas size changed: " + id);
                    File.WriteAllBytes(panelPath, panel.EncodeToPNG());
                    File.WriteAllBytes(framePath, frame.EncodeToPNG());
                    File.WriteAllBytes(emissionPath, emission.EncodeToPNG());
                    File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
                    File.WriteAllBytes(emissionAtlasPath, emissionAtlas.EncodeToPNG());
                    for (int m = 0; m < paths.Length; m++)
                        if (File.ReadAllText(paths[m] + ".meta") != metas[m]) throw new Exception("Resource GUID/import settings changed.");
                    // Verify persisted files, not just the in-memory images.
                    var saved = Read(panelPath);
                    for (int p = 0; p < pixels.Length; p++)
                        if (((Color32)saved.GetPixel(p % 64, p / 64)).Equals((Color32)pixels[p]) == false)
                            throw new Exception("Saved panel differs from the approved artwork: " + id);
                    for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                        sheet.SetPixel(i * 256 + x, y, saved.GetPixel(x / 4, y / 4));
                    Object.DestroyImmediate(source); Object.DestroyImmediate(frame); Object.DestroyImmediate(panel);
                    Object.DestroyImmediate(emission); Object.DestroyImmediate(atlas); Object.DestroyImmediate(emissionAtlas); Object.DestroyImmediate(saved);
                    Debug.Log("[Lamp Stripe Textures] " + id + ": 18 bands, 64px panel, 512px color/emission atlases, GUIDs preserved.");
                }
                sheet.Apply(); Directory.CreateDirectory(Path.Combine(root, "Docs/images"));
                File.WriteAllBytes(Path.Combine(root, "Docs/images/LampTiles.png"), sheet.EncodeToPNG());
                if (root == Directory.GetParent(Application.dataPath).FullName) AssetDatabase.Refresh();
                Debug.Log("[Lamp Stripe Textures] PASS: all five existing lamp textures replaced, emission atlases synchronized.");
            }
            finally { Object.DestroyImmediate(sheet); Object.DestroyImmediate(black); }
        }

        static Texture2D Read(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path))) throw new Exception("Cannot read texture: " + path);
            return texture;
        }

        static float Peak(Color color) => Mathf.Max(color.r, Mathf.Max(color.g, color.b));
    }
}
