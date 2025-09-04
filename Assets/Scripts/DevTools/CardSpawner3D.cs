using EndfieldFrontierTCG.CA;
using UnityEngine;

namespace EndfieldFrontierTCG.DevTools
{
    public class CardSpawner3D : MonoBehaviour
    {
        public CA_TypeManager TypeManager;
        public Transform SpawnParent;
        public int[] SpawnIDs = { 1, 3, 4 };

        public Vector3 StartPos = new Vector3(-0.5f, 0.6f, -0.5f);
        public Vector3 Offset = new Vector3(0.3f, 0f, 0.3f);

        private void Start()
        {
            if (TypeManager == null)
            {
                Debug.LogError("CardSpawner3D: TypeManager is not assigned.");
                return;
            }
            CardDatabase.EnsureLoaded();
            Vector3 pos = StartPos;
            foreach (var id in SpawnIDs)
            {
                if (CardDatabase.TryGet(id, out var data))
                {
                    var go = TypeManager.CreateCardByType(data.CA_Type, SpawnParent);
                    go.transform.position = pos;
                    var view3d = go.GetComponent<CardView3D>();
                    if (view3d != null)
                    {
                        // 仅绑定 CSV 内容（名称、HP/ATK、主图）。特效延迟到后续真实触发时再绑定
                        view3d.Bind(data);
                    }
                    pos += Offset;
                }
            }
        }
    }
}


