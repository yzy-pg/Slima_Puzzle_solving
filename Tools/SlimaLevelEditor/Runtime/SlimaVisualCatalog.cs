using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimaPrototype
{
    [Serializable]
    public sealed class SlimaTerrainVisual
    {
        public SlimaTerrainType type;
        public GameObject prefab;
    }

    [Serializable]
    public sealed class SlimaEntityVisual
    {
        public SlimaEntityType type;
        public GameObject prefab;
    }

    [CreateAssetMenu(menuName = "Slima/Visual Catalog", fileName = "SlimaVisualCatalog")]
    public sealed class SlimaVisualCatalog : ScriptableObject
    {
        public GameObject slimePrefab;
        public List<SlimaTerrainVisual> terrainPrefabs = new List<SlimaTerrainVisual>();
        public List<SlimaEntityVisual> entityPrefabs = new List<SlimaEntityVisual>();

        public GameObject GetTerrainPrefab(SlimaTerrainType type)
        {
            for (int i = 0; i < terrainPrefabs.Count; i++)
            {
                if (terrainPrefabs[i].type == type) return terrainPrefabs[i].prefab;
            }
            return null;
        }

        public GameObject GetEntityPrefab(SlimaEntityType type)
        {
            for (int i = 0; i < entityPrefabs.Count; i++)
            {
                if (entityPrefabs[i].type == type) return entityPrefabs[i].prefab;
            }
            return null;
        }
    }
}
