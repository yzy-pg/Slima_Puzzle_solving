using System;
using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>没有美术 Prefab 时创建可辨认的灰盒；同类机关始终使用相同基础几何。</summary>
    public static class GameplayPrototypeFactory
    {
        public static GameplayBlockView Create(
            BlockDefinition definition,
            GameplayBlockProfile profile,
            Vector3Int cell,
            int direction,
            Transform parent)
        {
            Quaternion rotation = Quaternion.Euler(0f, direction * 90f, 0f);
            GameObject root;

            if (definition != null && definition.previewPrefab != null)
            {
                // 包一层逻辑根：根的本地 +Z 永远是发射方向；美术 Prefab 可以保留自己的修正旋转。
                root = new GameObject(profile.id);
                root.transform.SetParent(parent, false);
                root.transform.position = CellCenter(cell);
                root.transform.rotation = rotation;

                GameObject visual = UnityEngine.Object.Instantiate(definition.previewPrefab, root.transform, false);
                visual.name = definition.previewPrefab.name;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = definition.previewPrefab.transform.localRotation;
                visual.transform.localScale = definition.previewPrefab.transform.localScale;
            }
            else
            {
                root = new GameObject(profile.id);
                root.transform.SetParent(parent, false);
                root.transform.position = CellCenter(cell);
                root.transform.rotation = rotation;
                BuildShape(root.transform, profile, definition);
            }

            if (profile.Kind == GameplayBlockKind.BeamEmitter &&
                root.GetComponentInChildren<GameplayMechanismArt>(true) == null &&
                BlockGameplaySettings.Current.showEmitterDirectionIndicator)
            {
                BuildEmitterDirectionIndicator(root.transform, profile.BeamColor);
            }

            root.name = $"Gameplay_{profile.id}_{cell.x}_{cell.y}_{cell.z}";
            if (profile.Kind == GameplayBlockKind.Door)
            {
                BlockMapDoor door = root.GetComponentInChildren<BlockMapDoor>(true);
                if (door == null) door = root.AddComponent<BlockMapDoor>();
                if (door.hinge == null) CreateDoorSlide(root.transform, door);
                door.toggleKey = KeyCode.None;
                door.clickToToggle = false;
                door.duration = BlockGameplaySettings.Current.doorAnimationDuration;
            }

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            GameplayBlockView view = root.GetComponent<GameplayBlockView>();
            if (view == null) view = root.AddComponent<GameplayBlockView>();
            view.Initialize(cell, profile);
            return view;
        }

        static void CreateDoorSlide(Transform root, BlockMapDoor door)
        {
            Bounds bounds = default;
            bool first = true;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                Bounds local = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = root.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            var hinge = new GameObject("DoorSlide").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = Vector3.zero;
            door.slideDistance = Mathf.Max(.1f, bounds.size.x + .06f);
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child != hinge) child.SetParent(hinge, true);
            }
            door.SetHinge(hinge);
        }

        public static GameObject CreateMarker(string name, Vector3Int cell, Color color, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);
            AddPrimitive(root.transform, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.02f, 0f), new Vector3(0.45f, 0.02f, 0.45f), color, null);
            AddPrimitive(root.transform, PrimitiveType.Cylinder, "Pole", new Vector3(0f, 0.5f, 0f), new Vector3(0.08f, 0.5f, 0.08f), color, null);
            return root;
        }

        static void BuildShape(Transform root, GameplayBlockProfile profile, BlockDefinition definition)
        {
            Texture texture = definition != null ? definition.GetTexture() : null;
            Color color = profile.Tint;

            switch (profile.shape?.ToLowerInvariant())
            {
                case "floor":
                    AddPrimitive(root, PrimitiveType.Cube, "Floor", new Vector3(0f, 0.06f, 0f), new Vector3(0.92f, 0.12f, 0.92f), color, texture,
                        emissionDefinition: definition);
                    break;
                case "control":
                    AddPrimitive(root, PrimitiveType.Cylinder, "ControlBase", new Vector3(0f, 0.08f, 0f), new Vector3(0.42f, 0.08f, 0.42f), color, texture);
                    if (profile.Kind == GameplayBlockKind.Lever)
                        AddPrimitive(root, PrimitiveType.Cube, "LeverHandle", new Vector3(0f, 0.42f, 0f), new Vector3(0.10f, 0.65f, 0.10f), color, null);
                    break;
                case "optic":
                    AddPrimitive(root, PrimitiveType.Cylinder, "OpticBody", new Vector3(0f, 0.45f, 0f), new Vector3(0.36f, 0.45f, 0.36f), color, texture);
                    AddPrimitive(root, PrimitiveType.Cube, "Direction", new Vector3(0f, 0.45f, 0.38f), new Vector3(0.16f, 0.16f, 0.36f), color, null);
                    break;
                case "machine":
                    AddPrimitive(root, PrimitiveType.Cylinder, "Machine", new Vector3(0f, 0.35f, 0f), new Vector3(0.42f, 0.35f, 0.42f), color, texture);
                    break;
                case "door":
                    AddPrimitive(root, PrimitiveType.Cube, "Door", new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 1f, 0.18f), color, texture);
                    break;
                default:
                    AddPrimitive(root, PrimitiveType.Cube, "Obstacle", new Vector3(0f, 0.5f, 0f), new Vector3(0.88f, 0.88f, 0.88f), color, texture);
                    break;
            }
        }

        /// <summary>
        /// 原型期发射方向标记。箭头固定朝本地 +Z，根物体旋转后会自然对应地图的四方向。
        /// 美术资源就绪后可在 GameplaySettings 中统一关闭。
        /// </summary>
        static void BuildEmitterDirectionIndicator(Transform root, BeamColor beamColor)
        {
            const string indicatorName = "EmitterDirectionIndicator";
            if (root.Find(indicatorName) != null) return;

            BlockGameplaySettings.Data settings = BlockGameplaySettings.Current;
            float height = settings.emitterIndicatorHeight;
            float length = Mathf.Clamp(settings.emitterIndicatorLength, 0.35f, 0.85f);
            float width = Mathf.Clamp(settings.emitterIndicatorWidth, 0.04f, 0.20f);
            Color color = BlockOpticsSystem.ToColor(beamColor);

            var indicator = new GameObject(indicatorName);
            indicator.transform.SetParent(root, false);

            float shaftLength = length * 0.62f;
            float headLength = length * 0.42f;
            AddPrimitive(
                indicator.transform,
                PrimitiveType.Cube,
                "ArrowShaft",
                new Vector3(0f, height, shaftLength * 0.5f - length * 0.18f),
                new Vector3(width, width * 0.55f, shaftLength),
                color,
                null,
                Vector3.zero,
                true);

            float tipZ = length * 0.5f;
            float wingOffset = headLength * 0.30f;
            AddPrimitive(
                indicator.transform,
                PrimitiveType.Cube,
                "ArrowHeadLeft",
                new Vector3(-wingOffset, height, tipZ - wingOffset),
                new Vector3(width, width * 0.55f, headLength),
                color,
                null,
                new Vector3(0f, 45f, 0f),
                true);
            AddPrimitive(
                indicator.transform,
                PrimitiveType.Cube,
                "ArrowHeadRight",
                new Vector3(wingOffset, height, tipZ - wingOffset),
                new Vector3(width, width * 0.55f, headLength),
                color,
                null,
                new Vector3(0f, -45f, 0f),
                true);
        }

        static GameObject AddPrimitive(
            Transform parent,
            PrimitiveType type,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            Texture texture,
            Vector3 localEuler = default,
            bool unlit = false,
            BlockDefinition emissionDefinition = null)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localRotation = Quaternion.Euler(localEuler);
            item.transform.localScale = localScale;
            Collider collider = item.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
                else UnityEngine.Object.DestroyImmediate(collider);
            }

            Renderer renderer = item.GetComponent<Renderer>();
            if (renderer != null)
            {
                bool emissiveFloor = emissionDefinition != null && emissionDefinition.emissionStrength > 0;
                Shader shader = emissiveFloor ? Shader.Find("Slima/Toon (Cel + Pixel)") : unlit
                    ? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")
                    : Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var material = new Material(shader) { color = color, mainTexture = texture };
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (emissiveFloor)
                {
                    material.SetTexture("_EmissionMap", emissionDefinition.emissionMap);
                    material.SetColor("_EmissionColor", emissionDefinition.emissionColor);
                    material.SetFloat("_EmissionStrength", emissionDefinition.emissionStrength);
                    material.SetFloat("_OutlineWidth", 0f);
                    material.SetFloat("_PixelSnap", 0f);
                }
                else if (material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", color * 1.4f);
                }
                renderer.sharedMaterial = material;
            }
            return item;
        }

        static Vector3 CellCenter(Vector3Int cell) =>
            new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);
    }
}
