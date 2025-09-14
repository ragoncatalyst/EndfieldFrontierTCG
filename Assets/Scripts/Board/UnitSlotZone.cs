using UnityEngine;

namespace EndfieldFrontierTCG.Board
{
    public class UnitSlotZone : SlotZone
    {
        [Header("Unit Zone")]
        public string zoneName = "Unit";

        // 覆盖基类方法，使用当前transform位置
        public override Vector3 GetSlotPosition(int r, int c)
        {
            Vector2 step = cellSize + spacing;
            return transform.position + 
                   Vector3.right * (c * step.x) + 
                   Vector3.forward * (r * step.y);
        }
    }
}


