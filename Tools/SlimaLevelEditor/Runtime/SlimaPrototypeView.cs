using System.Collections.Generic;
using UnityEngine;

namespace SlimaPrototype
{
    public sealed class SlimaPrototypeView : MonoBehaviour
    {
        private readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        private SlimaGameModel model;
        private SlimaVisualCatalog catalog;
        private float cellSize;
        private Transform staticRoot;
        private Transform dynamicRoot;

        public void Initialize(SlimaGameModel gameModel, SlimaVisualCatalog visualCatalog, float size)
        {
            model = gameModel;
            catalog = visualCatalog;
            cellSize = size;

            staticRoot = new GameObject("StaticLevel").transform;
            staticRoot.SetParent(transform, false);
            dynamicRoot = new GameObject("DynamicState").transform;
            dynamicRoot.SetParent(transform, false);

            ConfigureCamera();
            ConfigureLighting();
            BuildStaticLevel();
            RebuildDynamicState();
            model.Changed += RebuildDynamicState;
        }

        private void OnDestroy()
        {
            if (model != null) model.Changed -= RebuildDynamicState;
            foreach (Material material in materials.Values)
            {
                if (material != null) Destroy(material);
            }
        }

        private void BuildStaticLevel()
        {
            for (int z = 0; z < model.Level.height; z++)
            {
                for (int x = 0; x < model.Level.width; x++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    SlimaTerrainType terrain = model.Level.GetTerrain(cell);
                    GameObject prefab = catalog != null ? catalog.GetTerrainPrefab(terrain) : null;
                    if (prefab != null)
                    {
                        GameObject instance = Instantiate(prefab, World(cell, 0f), Quaternion.identity, staticRoot);
                        instance.name = $"Terrain_{terrain}_{x}_{z}";
                    }
                    else
                    {
                        BuildTerrainPlaceholder(cell, terrain);
                    }
                }
            }

            for (int i = 0; i < model.Level.entities.Count; i++)
            {
                SlimaEntityData entity = model.Level.entities[i];
                if (entity.type == SlimaEntityType.Door ||
                    entity.type == SlimaEntityType.LightReceiver ||
                    entity.type == SlimaEntityType.PressurePlate ||
                    entity.type == SlimaEntityType.Button ||
                    entity.type == SlimaEntityType.Lever ||
                    entity.type == SlimaEntityType.GelPotion ||
                    entity.type == SlimaEntityType.GelPill) continue;
                BuildEntity(entity, staticRoot, false);
            }
        }

        private void RebuildDynamicState()
        {
            if (dynamicRoot == null || model == null) return;
            ClearChildren(dynamicRoot);

            for (int i = 0; i < model.Level.entities.Count; i++)
            {
                SlimaEntityData entity = model.Level.entities[i];
                if (entity.type == SlimaEntityType.Door)
                {
                    BuildEntity(entity, dynamicRoot, model.IsDoorOpen(entity));
                }
                else if (entity.type == SlimaEntityType.LightReceiver)
                {
                    BuildEntity(entity, dynamicRoot, model.IsReceiverActive(entity.instanceId));
                }
                else if (entity.type == SlimaEntityType.PressurePlate ||
                         entity.type == SlimaEntityType.Button ||
                         entity.type == SlimaEntityType.Lever)
                {
                    BuildEntity(entity, dynamicRoot, model.IsEntityActive(entity.instanceId));
                }
                else if ((entity.type == SlimaEntityType.GelPotion || entity.type == SlimaEntityType.GelPill) && !model.IsConsumed(entity.instanceId))
                {
                    BuildEntity(entity, dynamicRoot, false);
                }
            }

            for (int i = 0; i < model.Slimes.Count; i++) BuildSlime(model.Slimes[i], i == model.ActiveSlimeIndex);
            BuildLight();
        }

        private void BuildTerrainPlaceholder(Vector2Int cell, SlimaTerrainType terrain)
        {
            Color color;
            float height;
            switch (terrain)
            {
                case SlimaTerrainType.Wall: color = new Color(0.22f, 0.26f, 0.31f); height = 1f; break;
                case SlimaTerrainType.Acid: color = new Color(0.45f, 0.9f, 0.2f); height = 0.16f; break;
                case SlimaTerrainType.Heat: color = new Color(1f, 0.3f, 0.08f); height = 0.16f; break;
                case SlimaTerrainType.Cold: color = new Color(0.2f, 0.7f, 1f); height = 0.16f; break;
                default: color = (cell.x + cell.y) % 2 == 0 ? new Color(0.29f, 0.34f, 0.38f) : new Color(0.25f, 0.3f, 0.34f); height = 0.12f; break;
            }

            Transform tile = Primitive(PrimitiveType.Cube, $"Terrain_{terrain}_{cell.x}_{cell.y}", staticRoot, color);
            tile.position = World(cell, terrain == SlimaTerrainType.Wall ? 0.5f : -0.08f);
            tile.localScale = terrain == SlimaTerrainType.Wall
                ? Vector3.one * cellSize
                : new Vector3(cellSize * 0.94f, height, cellSize * 0.94f);
        }

        private void BuildEntity(SlimaEntityData entity, Transform parent, bool activeState)
        {
            GameObject prefab = catalog != null ? catalog.GetEntityPrefab(entity.type) : null;
            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab, World(entity.Cell, 0f), Quaternion.Euler(0f, entity.direction * 90f, 0f), parent);
                instance.name = $"Entity_{entity.instanceId}";
                return;
            }

            PrimitiveType shape = PrimitiveType.Cube;
            Color color = new Color(0.7f, 0.7f, 0.7f);
            Vector3 scale = new Vector3(0.62f, 0.25f, 0.62f) * cellSize;
            float y = 0.15f;

            switch (entity.type)
            {
                case SlimaEntityType.Spawn: color = new Color(0.2f, 0.85f, 1f); shape = PrimitiveType.Cylinder; scale = new Vector3(0.18f, 0.72f, 0.18f); y = 0.72f; break;
                case SlimaEntityType.Exit: color = new Color(1f, 0.35f, 0.18f); shape = PrimitiveType.Cylinder; scale = new Vector3(0.18f, 0.72f, 0.18f); y = 0.72f; break;
                case SlimaEntityType.PaintStation: color = RecipeColor(entity.recipe); shape = PrimitiveType.Sphere; scale = Vector3.one * 0.42f; y = 0.34f; break;
                case SlimaEntityType.Splitter: color = new Color(0.75f, 0.4f, 1f); shape = PrimitiveType.Cube; scale = new Vector3(0.56f, 0.32f, 0.56f); y = 0.22f; break;
                case SlimaEntityType.LightEmitter: color = new Color(0.95f, 0.95f, 0.8f); shape = PrimitiveType.Cylinder; scale = new Vector3(0.5f, 0.16f, 0.5f); y = 0.18f; break;
                case SlimaEntityType.LightReceiver: color = activeState ? Color.white : RecipeColor(entity.recipe); shape = PrimitiveType.Cylinder; scale = new Vector3(0.54f, 0.18f, 0.54f); y = 0.2f; break;
                case SlimaEntityType.PressurePlate: color = model.IsEntityActive(entity.instanceId) ? Color.green : new Color(0.9f, 0.75f, 0.15f); shape = PrimitiveType.Cylinder; scale = new Vector3(0.58f, 0.06f, 0.58f); y = 0.06f; break;
                case SlimaEntityType.Button: color = model.IsEntityActive(entity.instanceId) ? Color.green : new Color(0.9f, 0.25f, 0.2f); shape = PrimitiveType.Cylinder; scale = new Vector3(0.48f, 0.14f, 0.48f); y = 0.16f; break;
                case SlimaEntityType.Lever: color = model.IsEntityActive(entity.instanceId) ? Color.green : new Color(0.65f, 0.5f, 0.25f); shape = PrimitiveType.Cylinder; scale = new Vector3(0.48f, 0.14f, 0.48f); y = 0.16f; break;
                case SlimaEntityType.Door: color = activeState ? new Color(0.2f, 0.8f, 0.35f) : new Color(0.8f, 0.25f, 0.2f); scale = activeState ? new Vector3(0.8f, 0.08f, 0.8f) : new Vector3(0.85f, 1.1f, 0.85f); y = activeState ? 0.05f : 0.55f; break;
                case SlimaEntityType.GelPotion: color = new Color(0.25f, 1f, 0.75f); shape = PrimitiveType.Capsule; scale = new Vector3(0.24f, 0.24f, 0.24f); y = 0.35f; break;
                case SlimaEntityType.GelPill: color = new Color(1f, 0.45f, 0.8f); shape = PrimitiveType.Capsule; scale = new Vector3(0.24f, 0.24f, 0.24f); y = 0.35f; break;
                case SlimaEntityType.TransferPipe: color = new Color(0.2f, 0.95f, 0.9f); shape = PrimitiveType.Capsule; scale = new Vector3(0.22f, 0.38f, 0.22f); y = 0.25f; break;
                case SlimaEntityType.LaserSplitter: color = RecipeColor(entity.recipe); shape = PrimitiveType.Cube; scale = new Vector3(0.56f, 0.32f, 0.56f); y = 0.22f; break;
                case SlimaEntityType.CatapultSplitter: color = new Color(1f, 0.55f, 0.1f); shape = PrimitiveType.Cube; scale = new Vector3(0.56f, 0.32f, 0.56f); y = 0.22f; break;
            }

            Transform instanceTransform = Primitive(shape, $"Entity_{entity.instanceId}", parent, color);
            instanceTransform.position = World(entity.Cell, y);
            instanceTransform.localScale = scale;
            instanceTransform.rotation = Quaternion.Euler(0f, entity.direction * 90f, 0f);

            if (entity.type == SlimaEntityType.LightEmitter)
            {
                Transform arrow = Primitive(PrimitiveType.Cube, "DirectionMarker", instanceTransform, Color.white);
                arrow.localPosition = new Vector3(0f, 0.75f, 0.35f);
                arrow.localScale = new Vector3(0.16f, 0.15f, 0.55f);
            }
            else if (entity.type == SlimaEntityType.LightReceiver)
            {
                Transform core = Primitive(PrimitiveType.Sphere, "ReceiverCore", instanceTransform, activeState ? Color.green : Color.black);
                core.localPosition = new Vector3(0f, 0.75f, 0f);
                core.localScale = Vector3.one * 0.42f;
            }
        }

        private void BuildSlime(SlimaState slime, bool active)
        {
            if (catalog != null && catalog.slimePrefab != null)
            {
                GameObject instance = Instantiate(catalog.slimePrefab, World(slime.cell, 0f), Quaternion.identity, dynamicRoot);
                instance.name = slime.id;
                if (active) instance.transform.localScale *= 1.12f;
                return;
            }

            Transform body = Primitive(PrimitiveType.Sphere, slime.id, dynamicRoot, RecipeColor(slime.recipe));
            body.position = World(slime.cell, active ? 0.48f : 0.42f);
            float scale = active ? 0.72f : 0.62f;
            body.localScale = new Vector3(scale, scale * 0.78f, scale) * cellSize;

            if (active)
            {
                Transform marker = Primitive(PrimitiveType.Cylinder, "ActiveMarker", body, Color.white);
                marker.localPosition = new Vector3(0f, 0.85f, 0f);
                marker.localScale = new Vector3(0.24f, 0.04f, 0.24f);
            }
        }

        private void BuildLight()
        {
            for (int i = 0; i < model.LightSegments.Count; i++)
            {
                SlimaLightSegment segment = model.LightSegments[i];
                Vector3 a = World(segment.from, 0.42f);
                Vector3 b = World(segment.to, 0.42f);
                Transform beam = Primitive(PrimitiveType.Cube, "LightBeam", dynamicRoot,
                    segment.recipe == SlimaRecipe.None ? Color.white : RecipeColor(segment.recipe));
                beam.position = (a + b) * 0.5f;
                Vector3 delta = b - a;
                beam.localScale = new Vector3(Mathf.Abs(delta.x) > 0f ? cellSize : 0.07f, 0.07f, Mathf.Abs(delta.z) > 0f ? cellSize : 0.07f);
            }
        }

        private Transform Primitive(PrimitiveType type, string objectName, Transform parent, Color color)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = objectName;
            instance.transform.SetParent(parent, false);
            Collider collider = instance.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = instance.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = MaterialFor(color);
            return instance.transform;
        }

        private Material MaterialFor(Color color)
        {
            Color32 color32 = color;
            int key = color32.r | color32.g << 8 | color32.b << 16 | color32.a << 24;
            if (materials.TryGetValue(key, out Material existing) && existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            materials[key] = material;
            return material;
        }

        private Vector3 World(Vector2Int cell, float y)
        {
            return new Vector3(cell.x * cellSize, y, cell.y * cellSize);
        }

        private void ConfigureCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            Vector3 center = new Vector3((model.Level.width - 1) * cellSize * 0.5f, 0f, (model.Level.height - 1) * cellSize * 0.5f);
            camera.transform.position = center + new Vector3(0f, 18f, -16f);
            camera.transform.LookAt(center);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(model.Level.height * 0.68f, 6f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.08f, 0.12f);
        }

        private static void ConfigureLighting()
        {
            if (FindObjectsOfType<Light>().Length > 0) return;
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        public static Color RecipeColor(SlimaRecipe recipe)
        {
            switch (recipe)
            {
                case SlimaRecipe.Red: return new Color(0.95f, 0.18f, 0.18f);
                case SlimaRecipe.Yellow: return new Color(1f, 0.82f, 0.1f);
                case SlimaRecipe.Blue: return new Color(0.15f, 0.45f, 1f);
                case SlimaRecipe.Red | SlimaRecipe.Yellow: return new Color(1f, 0.45f, 0.08f);
                case SlimaRecipe.Yellow | SlimaRecipe.Blue: return new Color(0.2f, 0.8f, 0.3f);
                case SlimaRecipe.Red | SlimaRecipe.Blue: return new Color(0.65f, 0.25f, 0.9f);
                case SlimaRecipe.All: return new Color(0.35f, 0.38f, 0.42f);
                default: return new Color(0.92f, 0.95f, 1f);
            }
        }

        private void OnGUI()
        {
            if (model == null) return;
            GUI.Box(new Rect(16f, 16f, 355f, 120f + model.Slimes.Count * 24f), GUIContent.none);
            GUI.Label(new Rect(30f, 26f, 320f, 24f), $"{model.Level.displayName}   步数 {model.StepCount}");
            for (int i = 0; i < model.Slimes.Count; i++)
            {
                SlimaState slime = model.Slimes[i];
                string marker = i == model.ActiveSlimeIndex ? "▶" : " ";
                GUI.Label(new Rect(30f, 52f + i * 24f, 320f, 22f), $"{marker} {slime.id}  凝胶 {slime.gel}  配方 {slime.recipe}");
            }
            GUI.Label(new Rect(30f, 60f + model.Slimes.Count * 24f, 325f, 42f), "WASD移动  Tab切换  E交互\nF融合  Z撤回  R重置");

            if (!model.Won && !model.Lost) return;
            string message = model.Won ? "接收器匹配  关卡完成" : "凝胶耗尽  关卡失败";
            GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width * 0.5f - 230f, Screen.height * 0.5f - 70f, 460f, 140f), message + "\n按 R 重新开始", style);
        }
    }
}
