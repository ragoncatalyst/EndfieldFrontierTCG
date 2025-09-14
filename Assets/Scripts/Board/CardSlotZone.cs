using UnityEngine;
using System.Collections.Generic;
using EndfieldFrontierTCG.CA;

namespace EndfieldFrontierTCG.Board
{
    [DisallowMultipleComponent]
    public class CardSlotZone : MonoBehaviour
    {
        [Header("Zone Settings")]
        [Tooltip("区域名称")] 
        public string zoneName = "Card Zone";
        
        [Tooltip("行数")] 
        public int rows = 2;
        
        [Tooltip("每行列数")] 
        public int columns = 5;
        
        [Tooltip("槽位间距")] 
        public Vector2 slotSpacing = new Vector2(1f, 1f);
        
        [Tooltip("槽位预制体")] 
        public GameObject slotPrefab;


        [Header("Interaction Settings")]
        [Tooltip("是否允许拖放")] 
        public bool allowDragDrop = true;
        
        [Tooltip("是否检查放置有效性")] 
        public bool checkPlacementValidity = true;
        
        [Tooltip("射线检测层级")] 
        public LayerMask raycastLayers = -1;

        // 槽位数组
        private CardSlotBehaviour[,] _slots;
        private CardSlotBehaviour _hoveredSlot;
        private CardSlotBehaviour _lastHoveredSlot;

        public void ForceResetScale()
        {
            // 只重置缩放，保持位置和旋转不变
            transform.localScale = Vector3.one;
        }

        private void Reset()
        {
            // 在编辑器中重置组件时只重置缩放
            ForceResetScale();
        }

        private void Awake()
        {
            ForceResetScale();
        }

        private void Start()
        {
            ForceResetScale();
            CreateCardGrid();
        }

        private void OnEnable()
        {
            ForceResetScale();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 在编辑器中修改值时也重置缩放
            ForceResetScale();
        }
#endif

        private void CreateCardGrid()
        {
            if (slotPrefab == null)
            {
                Debug.LogError($"[{zoneName}] Slot prefab is not assigned!");
                return;
            }

            _slots = new CardSlotBehaviour[rows, columns];

            // 使用Inspector中设置的行列数
            
            // 计算网格的总大小（包括卡槽本身的大小）
            float totalWidth = columns * slotSpacing.x;  // 不再减1，包括所有卡槽的宽度
            float totalDepth = rows * slotSpacing.y;     // 不再减1，包括所有卡槽的深度

            // 计算起始位置，从总大小的一半开始，再加上半个卡槽的大小
            float startX = -totalWidth * 0.5f + slotSpacing.x * 0.5f;
            float startZ = -totalDepth * 0.5f + slotSpacing.y * 0.5f;

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    // 计算位置（相对于父物体中心点）
                    Vector3 position = new Vector3(
                        startX + col * slotSpacing.x,  // X坐标（左右）
                        0,                             // Y坐标（高度）
                        startZ + row * slotSpacing.y   // Z坐标（前后）
                    );

                    // 创建槽位，只设置位置，完全不碰旋转
                    GameObject slotObj = Instantiate(slotPrefab, transform);
                    slotObj.name = $"{zoneName}_Slot_{row}_{col}";
                    slotObj.transform.localPosition = position;

                    // 添加Rigidbody并锁定所有移动和旋转
                    Rigidbody rb = slotObj.GetComponent<Rigidbody>();
                    if (rb == null)
                    {
                        rb = slotObj.AddComponent<Rigidbody>();
                    }
                    rb.isKinematic = true;
                    rb.constraints = RigidbodyConstraints.FreezeAll;

                    var slot = slotObj.GetComponent<CardSlotBehaviour>();
                    if (slot == null)
                    {
                        Debug.LogError($"[{zoneName}] Slot prefab must have CardSlotBehaviour component!");
                        continue;
                    }

                    _slots[row, col] = slot;
                }
            }
        }

        private void Update()
        {
            if (!allowDragDrop) return;

            // 更新hover状态
            _lastHoveredSlot = _hoveredSlot;
            _hoveredSlot = GetHoveredSlot();

            // 处理hover状态变化
            if (_hoveredSlot != _lastHoveredSlot)
            {
                if (_lastHoveredSlot != null)
                    _lastHoveredSlot.OnHoverExit();
                
                if (_hoveredSlot != null)
                    _hoveredSlot.OnHoverEnter();
            }
        }

        public CardSlotBehaviour GetHoveredSlot()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, raycastLayers);

            // 找到最上面的有效槽位
            CardSlotBehaviour bestSlot = null;
            float bestDistance = float.MaxValue;

            foreach (var hit in hits)
            {
                // 检查是否击中了属于这个区域的槽位
                var slot = hit.collider.GetComponent<CardSlotBehaviour>();
                if (slot != null && slot.transform.IsChildOf(transform))
                {
                    float distance = hit.distance;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestSlot = slot;
                    }
                }
            }

            // 如果槽位变化了，打印调试信息
            if (bestSlot != _hoveredSlot)
            {
                if (bestSlot != null)
                {
                    Debug.Log($"[CardSlotZone] 检测到新的槽位: {bestSlot.name}, 距离: {bestDistance}");
                }
                else if (_hoveredSlot != null)
                {
                    Debug.Log($"[CardSlotZone] 离开槽位: {_hoveredSlot.name}");
                }
            }

            return bestSlot;
        }

        public bool TryPlaceCard(CardView3D card, int row, int col)
        {
            if (!IsValidPosition(row, col)) return false;
            
            var slot = _slots[row, col];
            if (slot == null) return false;

            return slot.TryPlaceCard(card);
        }

        public CardView3D RemoveCard(int row, int col)
        {
            if (!IsValidPosition(row, col)) return null;
            
            var slot = _slots[row, col];
            if (slot == null) return null;

            return slot.RemoveCard();
        }

        public bool IsValidPosition(int row, int col)
        {
            return row >= 0 && row < rows && col >= 0 && col < columns;
        }

        public void GetSlotPosition(int row, int col, out Vector3 position)
        {
            position = Vector3.zero;
            if (!IsValidPosition(row, col)) return;

            var slot = _slots[row, col];
            if (slot != null)
            {
                position = slot.transform.position;
            }
        }

        public bool TryGetSlotIndices(Vector3 worldPosition, out int row, out int col)
        {
            row = -1;
            col = -1;

            float minDistance = float.MaxValue;
            bool found = false;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    var slot = _slots[r, c];
                    if (slot == null) continue;

                    float distance = Vector3.Distance(worldPosition, slot.transform.position);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        row = r;
                        col = c;
                        found = true;
                    }
                }
            }

            return found;
        }
    }
}
