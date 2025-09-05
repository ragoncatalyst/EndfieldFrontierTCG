using UnityEngine;
using EndfieldFrontierTCG.CA;
using EndfieldFrontierTCG.Board;

namespace EndfieldFrontierTCG.Board
{
    [DefaultExecutionOrder(50)]
    public class BoardLayoutManager : MonoBehaviour
    {
        public UnitSlotZone unitZone;
        public SupportSlotZone supportZone;
        public Transform handZone; // 已存在的 HandSplineZone 节点
        public DeckPile deckPile;
        [Header("Snap")]
        public float snapPlaneY = 0f; // 与各 Zone yHeight 对齐
        public float pickReleaseMaxDist = 0.15f;

        public void SnapCardToUnit(CardView3D card, int row, int col)
        {
            if (card == null || unitZone == null) return;
            Vector3 p = unitZone.GetSlotPosition(row, col);
            Quaternion r = unitZone.GetSlotRotation();
            card.SnapTo(p, r);
        }

        public void SnapCardToSupport(CardView3D card, int row, int col)
        {
            if (card == null || supportZone == null) return;
            Vector3 p = supportZone.GetSlotPosition(row, col);
            Quaternion r = supportZone.GetSlotRotation();
            card.SnapTo(p, r);
        }
    }
}


