using System.Collections.Generic;
using UnityEngine;

namespace SlimaPrototype
{
    public sealed class SlimaGameBootstrap : MonoBehaviour
    {
        [SerializeField] private TextAsset levelJson;
        [SerializeField] private TextAsset globalConfigJson;
        [SerializeField] private SlimaVisualCatalog visualCatalog;
        [SerializeField] private float cellSize = 1f;

        private SlimaGameModel model;
        private SlimaPrototypeView view;

        public TextAsset LevelJson
        {
            get => levelJson;
            set => levelJson = value;
        }

        public SlimaVisualCatalog VisualCatalog
        {
            get => visualCatalog;
            set => visualCatalog = value;
        }

        public SlimaGameModel Model => model;

        public TextAsset GlobalConfigJson
        {
            get => globalConfigJson;
            set => globalConfigJson = value;
        }

        private void Awake()
        {
            if (levelJson == null)
            {
                Debug.LogError("[Slima] 未指定关卡 JSON", this);
                enabled = false;
                return;
            }

            SlimaLevelData level;
            SlimaGlobalGameplayConfig globalConfig;
            try
            {
                level = SlimaLevelJson.FromTextAsset(levelJson);
                globalConfig = globalConfigJson != null
                    ? SlimaGlobalConfigJson.FromTextAsset(globalConfigJson)
                    : SlimaGlobalGameplayConfig.CreateDefault();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
                enabled = false;
                return;
            }

            List<SlimaLevelIssue> issues = SlimaLevelValidator.Validate(level);
            bool hasError = false;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].severity == SlimaIssueSeverity.Error)
                {
                    hasError = true;
                    Debug.LogError("[Slima] " + issues[i].message, this);
                }
                else
                {
                    Debug.LogWarning("[Slima] " + issues[i].message, this);
                }
            }

            if (hasError)
            {
                enabled = false;
                return;
            }

            model = new SlimaGameModel();
            model.Initialize(level, globalConfig);
            view = gameObject.AddComponent<SlimaPrototypeView>();
            view.Initialize(model, visualCatalog, Mathf.Max(0.25f, cellSize));
        }

        private void Update()
        {
            if (model == null) return;

            if (Input.GetKeyDown(KeyCode.R))
            {
                model.Reset();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Z))
            {
                model.Undo();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                model.SwitchActive();
                return;
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                model.Interact();
                return;
            }

            if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Space))
            {
                model.MergeAdjacent();
                return;
            }

            Vector2Int direction = Vector2Int.zero;
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) direction = Vector2Int.up;
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) direction = Vector2Int.down;
            else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) direction = Vector2Int.left;
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) direction = Vector2Int.right;

            if (direction != Vector2Int.zero) model.Move(direction);
        }
    }
}
