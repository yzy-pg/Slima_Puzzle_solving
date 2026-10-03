using UnityEngine;

namespace SlimaPuzzle.Greybox
{
    public sealed class GreyboxLevel : MonoBehaviour
    {
        private const int BoardSize = 16;
        private const float StepDuration = 0.12f;

        private readonly Vector2Int startCell = new Vector2Int(0, 0);
        private readonly Vector2Int goalCell = new Vector2Int(BoardSize - 1, BoardSize - 1);

        private Vector2Int playerCell;
        private Transform playerRoot;
        private Vector3 moveStart;
        private Vector3 moveTarget;
        private float moveProgress;
        private bool isMoving;
        private bool hasWon;

        private Material grassA;
        private Material grassB;
        private Material borderMaterial;
        private Material spawnMaterial;
        private Material goalMaterial;
        private Material playerMaterial;
        private Material whiteMaterial;
        private Material blackMaterial;

        private void Awake()
        {
            BuildLevel();
            ResetLevel();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetLevel();
                return;
            }

            if (isMoving)
            {
                moveProgress += Time.deltaTime / StepDuration;
                playerRoot.position = Vector3.Lerp(moveStart, moveTarget, Mathf.SmoothStep(0f, 1f, moveProgress));

                if (moveProgress >= 1f)
                {
                    playerRoot.position = moveTarget;
                    isMoving = false;
                    CheckGoal();
                }

                return;
            }

            if (hasWon)
            {
                return;
            }

            Vector2Int direction = ReadDirection();
            if (direction != Vector2Int.zero)
            {
                TryMove(direction);
            }
        }

        private void BuildLevel()
        {
            Application.targetFrameRate = 60;
            CreateMaterials();

            Transform environment = new GameObject("Environment").transform;
            environment.SetParent(transform);

            for (int z = 0; z < BoardSize; z++)
            {
                for (int x = 0; x < BoardSize; x++)
                {
                    Material tileMaterial = (x + z) % 2 == 0 ? grassA : grassB;
                    if (x == startCell.x && z == startCell.y)
                    {
                        tileMaterial = spawnMaterial;
                    }
                    else if (x == goalCell.x && z == goalCell.y)
                    {
                        tileMaterial = goalMaterial;
                    }

                    CreatePrimitive(
                        PrimitiveType.Cube,
                        $"Tile_{x:00}_{z:00}",
                        new Vector3(x, -0.1f, z),
                        new Vector3(0.94f, 0.2f, 0.94f),
                        tileMaterial,
                        environment);
                }
            }

            BuildBorder(environment);
            BuildGoal(environment);
            BuildPlayer();
            ConfigureCamera();
            ConfigureLighting();
        }

        private void BuildBorder(Transform parent)
        {
            float center = (BoardSize - 1) * 0.5f;
            float outside = 0.65f;

            CreatePrimitive(PrimitiveType.Cube, "Border_South", new Vector3(center, 0.32f, -outside), new Vector3(BoardSize + 1.2f, 0.85f, 0.45f), borderMaterial, parent);
            CreatePrimitive(PrimitiveType.Cube, "Border_North", new Vector3(center, 0.32f, BoardSize - 1 + outside), new Vector3(BoardSize + 1.2f, 0.85f, 0.45f), borderMaterial, parent);
            CreatePrimitive(PrimitiveType.Cube, "Border_West", new Vector3(-outside, 0.32f, center), new Vector3(0.45f, 0.85f, BoardSize + 1.2f), borderMaterial, parent);
            CreatePrimitive(PrimitiveType.Cube, "Border_East", new Vector3(BoardSize - 1 + outside, 0.32f, center), new Vector3(0.45f, 0.85f, BoardSize + 1.2f), borderMaterial, parent);
        }

        private void BuildGoal(Transform parent)
        {
            Transform goalRoot = new GameObject("Goal").transform;
            goalRoot.SetParent(parent);
            goalRoot.position = CellToWorld(goalCell);

            CreatePrimitive(PrimitiveType.Cylinder, "Goal_Base", new Vector3(0f, 0.12f, 0f), new Vector3(0.72f, 0.06f, 0.72f), goalMaterial, goalRoot, true);

            Transform marker = CreatePrimitive(PrimitiveType.Cube, "Goal_Marker", new Vector3(0f, 1.05f, 0f), new Vector3(0.42f, 0.42f, 0.42f), goalMaterial, goalRoot, true);
            marker.rotation = Quaternion.Euler(0f, 45f, 0f);
        }

        private void BuildPlayer()
        {
            playerRoot = new GameObject("Player").transform;
            playerRoot.SetParent(transform);

            CreatePrimitive(PrimitiveType.Sphere, "Slime_Body", new Vector3(0f, 0.48f, 0f), new Vector3(0.72f, 0.58f, 0.72f), playerMaterial, playerRoot, true);
            CreatePrimitive(PrimitiveType.Sphere, "Eye_L", new Vector3(-0.18f, 0.6f, -0.29f), new Vector3(0.16f, 0.2f, 0.1f), whiteMaterial, playerRoot, true);
            CreatePrimitive(PrimitiveType.Sphere, "Eye_R", new Vector3(0.18f, 0.6f, -0.29f), new Vector3(0.16f, 0.2f, 0.1f), whiteMaterial, playerRoot, true);
            CreatePrimitive(PrimitiveType.Sphere, "Pupil_L", new Vector3(-0.18f, 0.6f, -0.35f), new Vector3(0.07f, 0.09f, 0.05f), blackMaterial, playerRoot, true);
            CreatePrimitive(PrimitiveType.Sphere, "Pupil_R", new Vector3(0.18f, 0.6f, -0.35f), new Vector3(0.07f, 0.09f, 0.05f), blackMaterial, playerRoot, true);
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

            Vector3 center = new Vector3((BoardSize - 1) * 0.5f, 0f, (BoardSize - 1) * 0.5f);
            camera.transform.position = center + new Vector3(0f, 18f, -16f);
            camera.transform.LookAt(center);
            camera.orthographic = true;
            camera.orthographicSize = 11.2f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.13f, 0.18f);
        }

        private void ConfigureLighting()
        {
            Light[] lights = FindObjectsOfType<Light>();
            if (lights.Length > 0)
            {
                return;
            }

            GameObject lightObject = new GameObject("Directional Light");
            Light lightComponent = lightObject.AddComponent<Light>();
            lightComponent.type = LightType.Directional;
            lightComponent.color = new Color(1f, 0.93f, 0.8f);
            lightComponent.intensity = 1.4f;
            lightComponent.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.58f);
        }

        private void CreateMaterials()
        {
            grassA = CreateMaterial(new Color(0.22f, 0.46f, 0.25f));
            grassB = CreateMaterial(new Color(0.19f, 0.4f, 0.22f));
            borderMaterial = CreateMaterial(new Color(0.28f, 0.31f, 0.35f), 0.15f);
            spawnMaterial = CreateMaterial(new Color(0.18f, 0.55f, 0.76f), 0.25f);
            goalMaterial = CreateMaterial(new Color(1f, 0.67f, 0.08f), 0.35f, true);
            playerMaterial = CreateMaterial(new Color(0.3f, 0.9f, 0.72f), 0.35f);
            whiteMaterial = CreateMaterial(new Color(0.95f, 0.97f, 1f), 0.2f);
            blackMaterial = CreateMaterial(new Color(0.03f, 0.04f, 0.06f));
        }

        private static Material CreateMaterial(Color color, float smoothness = 0f, bool emission = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.color = color;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.4f);
            }

            return material;
        }

        private static Transform CreatePrimitive(
            PrimitiveType primitiveType,
            string objectName,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Transform parent,
            bool removeCollider = true)
        {
            GameObject instance = GameObject.CreatePrimitive(primitiveType);
            instance.name = objectName;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localScale = localScale;
            instance.GetComponent<Renderer>().sharedMaterial = material;

            if (removeCollider)
            {
                Collider collider = instance.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }
            }

            return instance.transform;
        }

        private Vector2Int ReadDirection()
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                return Vector2Int.up;
            }

            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                return Vector2Int.down;
            }

            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                return Vector2Int.left;
            }

            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                return Vector2Int.right;
            }

            return Vector2Int.zero;
        }

        private void TryMove(Vector2Int direction)
        {
            Vector2Int nextCell = playerCell + direction;
            if (nextCell.x < 0 || nextCell.x >= BoardSize || nextCell.y < 0 || nextCell.y >= BoardSize)
            {
                return;
            }

            playerCell = nextCell;
            moveStart = playerRoot.position;
            moveTarget = CellToWorld(playerCell);
            moveProgress = 0f;
            isMoving = true;
        }

        private void CheckGoal()
        {
            if (playerCell == goalCell)
            {
                hasWon = true;
            }
        }

        private void ResetLevel()
        {
            playerCell = startCell;
            playerRoot.position = CellToWorld(startCell);
            moveStart = playerRoot.position;
            moveTarget = playerRoot.position;
            moveProgress = 0f;
            isMoving = false;
            hasWon = false;
        }

        private static Vector3 CellToWorld(Vector2Int cell)
        {
            return new Vector3(cell.x, 0f, cell.y);
        }

        private void OnGUI()
        {
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            GUIStyle infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                normal = { textColor = new Color(0.9f, 0.94f, 1f) }
            };

            GUI.Box(new Rect(18f, 18f, 330f, 92f), GUIContent.none);
            GUI.Label(new Rect(34f, 28f, 290f, 34f), "16 × 16 灰盒关卡", titleStyle);
            GUI.Label(new Rect(34f, 67f, 300f, 28f), "WASD / 方向键移动    R 重置", infoStyle);

            if (!hasWon)
            {
                return;
            }

            float width = 420f;
            float height = 150f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(panel, GUIContent.none);

            GUIStyle winStyle = new GUIStyle(titleStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 30
            };

            GUIStyle resetStyle = new GUIStyle(infoStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };

            GUI.Label(new Rect(panel.x, panel.y + 28f, panel.width, 44f), "到达终点，关卡完成！", winStyle);
            GUI.Label(new Rect(panel.x, panel.y + 88f, panel.width, 32f), "按 R 重新开始", resetStyle);
        }
    }
}
