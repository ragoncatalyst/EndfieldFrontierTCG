using UnityEngine;
using UnityEngine.UI;

/// 在 uGUI 里绘制一段圆环（扇形环带），无需 Shader。
/// 角度单位：度；0° 在 +X 方向，逆时针为正（与 Atan2 一致）。
[ExecuteAlways]
public class UIRingArc : MaskableGraphic
{
    [Min(0)] public float radius = 300f;      // 圆心到环“中线”的半径（像素/RectTransform 单位）
    [Min(0)] public float thickness = 40f;    // 环带厚度（外半径-内半径）
    [Range(-360, 360)] public float startAngle = 0f;   // 起始角（度）
    [Range(-360, 360)] public float endAngle   = 45f;  // 结束角（度）
    [Range(3, 256)] public int segments = 64;          // 细分数，越大越圆滑

    float InnerR => Mathf.Max(0f, radius - thickness * 0.5f);
    float OuterR => radius + thickness * 0.5f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        float a0  = startAngle * Mathf.Deg2Rad;
        float a1  = endAngle   * Mathf.Deg2Rad;
        float arc = a1 - a0;
        if (Mathf.Approximately(arc, 0f)) return;

        // 根据弧长自适应细分数量（至少 2 段）
        int steps = Mathf.Max(2, Mathf.CeilToInt(segments * Mathf.Abs(arc) / (2f * Mathf.PI)));

        for (int i = 0; i < steps; i++)
        {
            float t0 = (float)i / steps;
            float t1 = (float)(i + 1) / steps;
            float ang0 = a0 + arc * t0;
            float ang1 = a0 + arc * t1;

            Vector2 i0 = new Vector2(Mathf.Cos(ang0), Mathf.Sin(ang0)) * InnerR;
            Vector2 o0 = new Vector2(Mathf.Cos(ang0), Mathf.Sin(ang0)) * OuterR;
            Vector2 i1 = new Vector2(Mathf.Cos(ang1), Mathf.Sin(ang1)) * InnerR;
            Vector2 o1 = new Vector2(Mathf.Cos(ang1), Mathf.Sin(ang1)) * OuterR;

            int idx = vh.currentVertCount;
            vh.AddVert(i0, color, Vector2.zero);
            vh.AddVert(o0, color, Vector2.zero);
            vh.AddVert(o1, color, Vector2.zero);
            vh.AddVert(i1, color, Vector2.zero);

            vh.AddTriangle(idx + 0, idx + 1, idx + 2);
            vh.AddTriangle(idx + 0, idx + 2, idx + 3);
        }
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        SetVerticesDirty();
    }

    void OnValidate() => SetVerticesDirty();

    /// 便捷设置
    public void SetArc(float startDeg, float endDeg, float r, float thick)
    {
        startAngle = startDeg;
        endAngle   = endDeg;
        radius     = r;
        thickness  = thick;
        SetVerticesDirty();
    }
}