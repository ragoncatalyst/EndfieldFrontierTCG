using UnityEngine;

namespace EndfieldFrontierTCG.Board
{
    public abstract class SlotZone : MonoBehaviour
    {
        [Header("Raycast Settings")]
        [Tooltip("射线检测的层级")]
        public LayerMask raycastLayers = -1;
        
        [Tooltip("可接受的最大放置距离")]
        public float maxPlaceDistance = 1.0f;

        // 获取射线命中的槽位位置
        public bool TryGetHoveredPosition(Vector3 worldPosition, out Vector3 snapPosition, out Quaternion snapRotation)
        {
            snapPosition = Vector3.zero;
            snapRotation = Quaternion.identity;

            // 遍历所有槽位，找到指针所在的槽位
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 slotPos = GetSlotPosition(r, c);
                    
                    // 转换到局部空间来判断是否在槽位范围内
                    Vector3 localPoint = transform.InverseTransformPoint(worldPosition);
                    Vector3 localSlotPos = transform.InverseTransformPoint(slotPos);
                    
                    // 计算槽位的边界
                    float halfWidth = cellSize.x * 0.5f;
                    float halfHeight = cellSize.y * 0.5f;
                    
                    // 检查指针是否在槽位矩形范围内
                    if (localPoint.x >= localSlotPos.x - halfWidth &&
                        localPoint.x <= localSlotPos.x + halfWidth &&
                        localPoint.z >= localSlotPos.z - halfHeight &&
                        localPoint.z <= localSlotPos.z + halfHeight)
                    {
                        snapPosition = slotPos;
                        snapRotation = GetSlotRotation();
                        return true;
                    }
                }
            }

            return false;
        }
        [Header("Grid")]
        public int rows = 2;
        public int cols = 5;
        public Vector2 cellSize = new Vector2(1f, 1f);
        public Vector2 spacing = new Vector2(0f, 0f);
        
        // 所有位置相关的变量默认为0
        protected Vector3 originOffset = Vector3.zero;
        protected float yawDeg = 0f;
        protected float yHeight = 0.01f; // 略微抬高以避免Z-fighting

        public virtual Vector3 GetSlotPosition(int r, int c)
        {
            Vector2 step = cellSize + spacing;
            Vector3 right = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.right;
            Vector3 forward = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            Vector3 basePos = transform.position + originOffset + Vector3.up * yHeight;
            return basePos + right * (c * step.x) + forward * (r * step.y);
        }

        public virtual Quaternion GetSlotRotation()
        {
            // 槽位根与桌面平行：仅绕 Y（朝向），不再倾斜 X
            return Quaternion.Euler(0f, yawDeg, 0f);
        }

        public virtual bool TryFindSnapPose(Vector3 worldPos, out Vector3 snapPos, out Quaternion snapRot)
        {
            snapPos = Vector3.zero; snapRot = Quaternion.identity;
            float best = float.PositiveInfinity; bool found = false;
            Vector2 step = cellSize + spacing;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 p = GetSlotPosition(r, c);
                    // 仅在平面内测距
                    Vector2 a = new Vector2(p.x, p.z);
                    Vector2 b = new Vector2(worldPos.x, worldPos.z);
                    float d = Vector2.Distance(a, b);
                    // 允许在单元格矩形范围内或其对角线长度的一半内吸附
                    float accept = Mathf.Max(step.magnitude * 0.5f, Mathf.Max(cellSize.x, cellSize.y) * 0.6f);
                    if (d < accept && d < best)
                    {
                        best = d; found = true; snapPos = p; snapRot = GetSlotRotation();
                    }
                }
            }
            return found;
        }

        protected virtual void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.65f, 0f, 0.35f);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 p = GetSlotPosition(r, c);
                    Vector3 sz = new Vector3(cellSize.x, 0.001f, cellSize.y);
                    Gizmos.DrawCube(p, sz);
                }
            }
        }
    }
}


