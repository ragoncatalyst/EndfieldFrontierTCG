using UnityEngine;
using System.Collections.Generic;

namespace EndfieldFrontierTCG.Board
{
    [ExecuteAlways]
    public class SlotGridManager : MonoBehaviour
    {
        public SlotBehaviour slotPrefab;
        public SlotType slotType = SlotType.Unit;
        public int rows = 2;
        public int cols = 5;
        public Vector2 cellSize = new Vector2(1.25f, 2.0f);
        public Vector2 spacing = new Vector2(0.25f, 0.25f);
        public float yawDeg = 0f;
        public float yHeight = 0f; // 动态Y值，移除固定值
        public Transform origin;
        [Header("Auto Center to Camera")]
        public bool centerToCameraBottom = true;
        [Range(0f,0.5f)] public float viewportBottomMargin = 0.08f;

        [SerializeField] private List<SlotBehaviour> slots = new List<SlotBehaviour>();
        public IReadOnlyList<SlotBehaviour> Slots => slots;

        public bool autoBuild = true;

        private void Start()
        {
            TryAutoBuild();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) TryAutoBuild();
        }

        private void TryAutoBuild()
        {
            if (!autoBuild) return;
            if (slotPrefab == null) return;
            if (slots == null || slots.Count == 0)
            {
                BuildGrid();
            }
        }

        public void ClearGrid()
        {
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                var s = slots[i]; if (s != null) { if (Application.isEditor) GameObject.DestroyImmediate(s.gameObject); else GameObject.Destroy(s.gameObject); }
            }
            slots.Clear();
        }

        [ContextMenu("Build Grid")]
        public void BuildGrid()
        {
            if (slotPrefab == null) return;
            ClearGrid();
            Vector3 basePos;
            Vector3 right = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.right;
            Vector3 forward = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            Vector2 stepAll = cellSize + spacing;
            if (centerToCameraBottom && Camera.main != null)
            {
                var cam = Camera.main;
                Ray r = cam.ViewportPointToRay(new Vector3(0.5f, viewportBottomMargin, 0f));
                float t; Vector3 p = transform.position;
                if (Mathf.Abs(r.direction.y) > 1e-6f) { t = (yHeight + ((origin!=null)?origin.position.y:transform.position.y) - r.origin.y)/r.direction.y; p = r.GetPoint(t); }
                basePos = new Vector3(p.x, (origin!=null?origin.position.y:transform.position.y) + yHeight, p.z);
                // 使 2*5 网格整体居中：向左、向上偏移半个总尺寸
                basePos -= right * ((cols-1) * stepAll.x * 0.5f);
                basePos -= forward * ((rows-1) * stepAll.y * 0.5f);
            }
            else
            {
                basePos = (origin != null ? origin.position : transform.position) + Vector3.up * yHeight;
            }
            // 槽位根与桌面平行
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            int id = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    SlotBehaviour inst = null;
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        inst = (SlotBehaviour)UnityEditor.PrefabUtility.InstantiatePrefab(slotPrefab, transform);
                    }
                    else
                    {
                        inst = Instantiate(slotPrefab, transform);
                    }
#else
                    inst = Instantiate(slotPrefab, transform);
#endif
                    Vector3 pos = basePos + right * (c * stepAll.x) + forward * (r * stepAll.y);
                    inst.transform.position = pos;
                    inst.transform.rotation = rot;
                    inst.EnsureVisual();
                    inst.slotType = slotType; inst.slotId = id++;
                    slots.Add(inst);
                }
            }
            if (slots.Count > 0) Debug.Log($"[SlotGridManager] Built {slots.Count} slots on {name}");
        }

        public SlotBehaviour GetNearestFreeSlot(Vector3 worldPos)
        {
            // 将世界坐标转换到本地空间
            Vector3 localPos = transform.InverseTransformPoint(worldPos);
            Debug.Log($"[SlotGridManager] 检查位置: 世界坐标={worldPos}, 本地坐标={localPos}");
            
            // 计算每个槽位的大小（包括间距）
            Vector2 slotSize = cellSize;
            Debug.Log($"[SlotGridManager] 槽位大小: {slotSize}, 间距: {spacing}");
            
            // 遍历所有槽位
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) 
                {
                    Debug.LogWarning($"[SlotGridManager] 槽位 {i} 为空");
                    continue;
                }
                if (s.IsOccupied)
                {
                    Debug.Log($"[SlotGridManager] 槽位 {i} 已被占用");
                    continue;
                }
                
                // 获取槽位的本地位置
                Vector3 slotLocalPos = transform.InverseTransformPoint(s.transform.position);
                
                // 检查指针是否在槽位范围内
                float halfWidth = slotSize.x * 0.5f;
                float halfHeight = slotSize.y * 0.5f;
                
                bool canPlace = localPos.x >= slotLocalPos.x - halfWidth &&
                    localPos.x <= slotLocalPos.x + halfWidth &&
                    localPos.z >= slotLocalPos.z - halfHeight &&
                    localPos.z <= slotLocalPos.z + halfHeight;
                
                Debug.Log($"[SlotGridManager] 检查槽位 {s.slotId} (Row: {s.slotId / cols}, Col: {s.slotId % cols})" +
                    $"\n位置: {slotLocalPos}" +
                    $"\n范围: X[{slotLocalPos.x - halfWidth}, {slotLocalPos.x + halfWidth}], " +
                    $"Z[{slotLocalPos.z - halfHeight}, {slotLocalPos.z + halfHeight}]" +
                    $"\n结果: {(canPlace ? "可以放置" : "不在范围内")}");
                
                if (canPlace)
                {
                    return s;
                }
            }
            
            return null;
        }
    }
}


