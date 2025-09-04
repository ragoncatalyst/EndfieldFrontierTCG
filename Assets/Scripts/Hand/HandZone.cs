using System.Collections.Generic;
using UnityEngine;

namespace EndfieldFrontierTCG.Hand
{
    // 简单的手牌区：定义一条曲线（或直线）上的若干停靠位
    public class HandZone : MonoBehaviour
    {
        public static readonly List<HandZone> Instances = new List<HandZone>();

        [Header("Layout")]
        public int slots = 10;
        public float radius = 1.2f;       // 弧形半径
        public float arcDeg = 40f;        // 弧形角度
        public float y = 0.5f;            // 托盘高度
        public float snapDistance = 0.25f;

        [Header("Gizmos (Editor)")]
        public bool showGizmos = true;
        public Color gizmoColor = new Color(0.2f, 0.7f, 1f, 0.7f);
        public float slotMarkerSize = 0.08f;
        public float arrowLength = 0.18f;

        private Vector3[] _slotWorldPos;
        private Quaternion[] _slotWorldRot;

        private void OnEnable()
        {
            if (!Instances.Contains(this)) Instances.Add(this);
            Rebuild();
        }
        private void OnDisable()
        {
            Instances.Remove(this);
        }

        public void Rebuild()
        {
            _slotWorldPos = new Vector3[slots];
            _slotWorldRot = new Quaternion[slots];
            Vector3 center = transform.position; // 使用 TransformPoint 处理平移/缩放
            float start = -arcDeg * 0.5f;
            for (int i = 0; i < slots; i++)
            {
                float t = slots == 1 ? 0.5f : (float)i / (slots - 1);
                float a = Mathf.Lerp(start, start + arcDeg, t);
                Quaternion localYaw = Quaternion.Euler(0f, a, 0f);
                Vector3 localPos = (Vector3.up * y) + (localYaw * Vector3.back * radius);
                Vector3 p = transform.TransformPoint(localPos);
                _slotWorldPos[i] = p;
                Vector3 toCenter = (transform.TransformPoint(Vector3.up * y) - p).normalized;
                _slotWorldRot[i] = Quaternion.LookRotation(toCenter, transform.up);
            }
        }

        public int SlotCount
        {
            get
            {
                if (_slotWorldPos == null || _slotWorldPos.Length != slots) Rebuild();
                return _slotWorldPos != null ? _slotWorldPos.Length : slots;
            }
        }

        public bool TryGetSlotPose(int index, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (_slotWorldPos == null || _slotWorldPos.Length != slots) Rebuild();
            if (_slotWorldPos == null || index < 0 || index >= _slotWorldPos.Length) return false;
            pos = _slotWorldPos[index];
            rot = _slotWorldRot[index];
            return true;
        }

        // 绑定：把一张卡直接吸附到指定槽位
        public bool AssignCardToSlot(CardView3D card, int index)
        {
            if (card == null) return false;
            Rebuild();
            if (!TryGetSlotPose(index, out var p, out var r)) return false;
            card.SnapTo(p, r);
            return true;
        }

        public bool TrySnap(CardView3D card)
        {
            if (_slotWorldPos == null || card == null) return false;
            // 找最近槽位
            float best = float.MaxValue; int bestIdx = -1;
            for (int i = 0; i < _slotWorldPos.Length; i++)
            {
                float d = Vector3.Distance(card.transform.position, _slotWorldPos[i]);
                if (d < best)
                {
                    best = d; bestIdx = i;
                }
            }
            if (bestIdx >= 0 && best <= snapDistance)
            {
                card.SnapTo(_slotWorldPos[bestIdx], _slotWorldRot[bestIdx]);
                return true;
            }
            return false;
        }

        // 静态入口：查询场景中所有手牌区，尝试吸附
        public static bool TrySnapIntoAny(CardView3D card)
        {
            foreach (var hz in Instances)
            {
                if (hz != null && hz.TrySnap(card)) return true;
            }
            return false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos) return;
            // 保证在编辑器拖动时也能看到正确位置
            if (_slotWorldPos == null || _slotWorldPos.Length != slots) Rebuild();
            if (_slotWorldPos == null) return;
            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Color c = gizmoColor;
            for (int i = 0; i < _slotWorldPos.Length; i++)
            {
                UnityEditor.Handles.color = c;
                // 画圆点
                UnityEditor.Handles.SphereHandleCap(0, _slotWorldPos[i], Quaternion.identity, slotMarkerSize, EventType.Repaint);
                // 画朝向箭头
                Vector3 fwd = _slotWorldRot[i] * Vector3.forward;
                UnityEditor.Handles.ArrowHandleCap(0, _slotWorldPos[i], Quaternion.LookRotation(fwd, Vector3.up), arrowLength, EventType.Repaint);
            }
            // 画弧中心与轮廓
            UnityEditor.Handles.color = new Color(c.r, c.g, c.b, 0.35f);
            Vector3 center2 = transform.TransformPoint(Vector3.up * y);
            Vector3 from = transform.TransformDirection(Quaternion.Euler(0, -arcDeg * 0.5f, 0) * Vector3.forward);
            UnityEditor.Handles.DrawWireArc(center2, transform.up, from, arcDeg, radius);
        }
#endif
    }
}


