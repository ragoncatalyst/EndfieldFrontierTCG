using UnityEngine;
using EndfieldFrontierTCG.CA;

namespace EndfieldFrontierTCG.Board
{
    public enum SlotType { Unit, Support }

    [DisallowMultipleComponent]
    public class SlotBehaviour : MonoBehaviour
    {
        public SlotType slotType = SlotType.Unit;
        public int slotId = -1;
        public bool acceptOnlyEmpty = true;
        [Header("Renderers")]
        public Renderer unitPlate;
        public Renderer supportPlate;
        [Header("Colors")]
        public Color unitColor = new Color(0.2f, 0.5f, 1f, 0.18f);
        public Color unitHighlight = new Color(0.4f, 0.7f, 1f, 0.35f);
        public Color supportColor = new Color(1f, 0.8f, 0.1f, 0.18f);
        public Color supportHighlight = new Color(1f, 0.9f, 0.2f, 0.35f);
        [Header("Sizes (meters)")]
        public Vector2 unitCellSize = new Vector2(1.25f, 2.0f);
        public Vector2 supportCellSize = new Vector2(1.0f, 1.0f);
        [Header("Visibility")]
        public bool forceRuntimeVisible = true;

        [System.NonSerialized] public CardView3D CurrentCard;

        public bool IsOccupied => CurrentCard != null;

        public bool Attach(CardView3D card, float aheadZ, float t1, float t2)
        {
            if (card == null) return false;
            if (acceptOnlyEmpty && IsOccupied) return false;
            CurrentCard = card;
            card.SetHomePose(transform.position, transform.rotation);
            // 若 t1/t2 传入为 0，退回到卡牌自己的 returnPhase1Duration/returnPhase2Duration
            float p1 = (t1 > 0f ? t1 : card.returnPhase1Duration);
            float p2 = (t2 > 0f ? t2 : card.returnPhase2Duration);
            card.BeginSmoothReturnToHome(aheadZ, p1, p2);
            return true;
        }

        public void Detach(CardView3D card)
        {
            if (card != null && CurrentCard == card) CurrentCard = null;
        }

        public void SetHighlight(bool on)
        {
            SetColor(unitPlate, on ? unitHighlight : unitColor);
            SetColor(supportPlate, on ? supportHighlight : supportColor);
        }

        private void SetColor(Renderer r, Color c)
        {
            if (r == null) return; var m = r.sharedMaterial; if (m == null) return; if (m.HasProperty("_Color")) m.color = c;
        }

        // Ensure visible plates for unit and support
        public void EnsureVisual()
        {
            EnsurePlate(ref unitPlate, "UnitPlate", unitCellSize, unitColor);
            EnsurePlate(ref supportPlate, "SupportPlate", supportCellSize, supportColor);
        }

        private void EnsurePlate(ref Renderer target, string name, Vector2 size, Color color)
        {
            if (target == null)
            {
                Transform plate = transform.Find(name);
                if (plate == null)
                {
                    // 使用 Quad 与桌面平行
                    var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    go.name = name;
                    go.transform.SetParent(transform, false);
                    go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    go.transform.localPosition = (name == "SupportPlate") ? new Vector3(0f, 0.001f, -supportCellSize.y * 1.05f) : new Vector3(0f, 0.001f, 0f);
                    target = go.GetComponent<Renderer>();
                }
                var col = target.GetComponent<Collider>(); if (col is BoxCollider bc) bc.isTrigger = false;
            }
            if (target.sharedMaterial == null)
            {
                Shader shader;
                if (forceRuntimeVisible)
                    shader = Shader.Find("Unlit/Color");
                else
                    shader = Shader.Find("Unlit/Transparent");
                if (shader == null) shader = Shader.Find("Standard");
                var mat = new Material(shader); mat.color = color; target.sharedMaterial = mat;
            }
            target.enabled = true;
            // Quad 缩放：x=宽度，y=高度
            target.transform.localScale = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), 1f);
        }
    }
}


