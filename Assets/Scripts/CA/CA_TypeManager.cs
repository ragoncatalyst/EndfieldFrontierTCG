using System.Collections.Generic;
using UnityEngine;

namespace EndfieldFrontierTCG.CA
{
    public class CA_TypeManager : MonoBehaviour
    {
        [System.Serializable]
        public class TypePrefab
        {
            public string CA_Type;
            public GameObject Prefab;
        }

        public List<TypePrefab> TypePrefabs = new List<TypePrefab>();
        private readonly Dictionary<string, GameObject> _typeToPrefab = new Dictionary<string, GameObject>();

        private void Awake()
        {
            _typeToPrefab.Clear();
            foreach (var tp in TypePrefabs)
            {
                if (tp != null && tp.Prefab != null && !string.IsNullOrEmpty(tp.CA_Type))
                {
                    _typeToPrefab[tp.CA_Type] = tp.Prefab;
                }
            }
        }

        public GameObject CreateCardByType(string caType, Transform parent)
        {
            if (!_typeToPrefab.TryGetValue(caType, out var prefab))
            {
                Debug.LogError($"No prefab mapped for CA_Type={caType}");
                return null;
            }
            var go = Instantiate(prefab, parent);
            return go;
        }
    }
}


