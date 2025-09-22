using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class RadialLayoutGroup : MonoBehaviour
{
    public enum RotationMode { KeepUpright, Tangent, FaceCenter }

    [Header("布局")]
    public float radius = 300f;
    public float startAngle = 90f;     // 度
    public float endAngle   = -90f;    // 度（=start+360 做整圆）
    public bool  evenDistribution = true;
    public float spacingDegrees   = 10f;   // 不均分时的固定角距

    [Header("旋转")]
    public RotationMode rotationMode = RotationMode.KeepUpright;
    public float rotationOffset = 0f;      // 在最终旋转上再加一个偏移

    void OnEnable() { Arrange(); }
    void OnValidate() { Arrange(); }
    void OnTransformChildrenChanged() { Arrange(); }

    void Arrange()
    {
        // 只取直系且激活的子物体
        var all = GetComponentsInChildren<RectTransform>(false);
        var items = new List<RectTransform>();
        foreach (var rt in all)
            if (rt.parent == transform && rt.gameObject.activeInHierarchy)
                items.Add(rt);

        int n = items.Count;
        if (n == 0) return;

        float step = evenDistribution
            ? (n > 1 ? (endAngle - startAngle) / (n - 1) : 0f)
            : spacingDegrees;

        for (int i = 0; i < n; i++)
        {
            var child = items[i];

            // 读取每个条目的个性化覆盖
            var per = child.GetComponent<RadialItem>();
            float angleDeg = (evenDistribution ? startAngle + i * step : startAngle + i * step);
            float r = radius;

            if (per != null)
            {
                angleDeg += per.angleOffsetDeg;
                r        += per.radiusOffset;
            }

            float rad = angleDeg * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r;
            child.anchoredPosition = pos;

            // 计算旋转
            Quaternion rot = Quaternion.identity;
            RotationMode mode = rotationMode;

            if (per != null && per.rotationOverride != RadialItem.RotationOverride.Inherit)
            {
                mode = (RotationMode)per.rotationOverride; // 枚举值顺序做了映射
            }

            switch (mode)
            {
                case RotationMode.KeepUpright:
                    rot = Quaternion.identity;
                    break;
                case RotationMode.Tangent:
                    rot = Quaternion.Euler(0, 0, angleDeg + 90f);
                    break;
                case RotationMode.FaceCenter:
                    rot = Quaternion.Euler(0, 0, angleDeg + 180f);
                    break;
            }

            if (per != null && per.rotationOverride == RadialItem.RotationOverride.Fixed)
            {
                rot = Quaternion.Euler(0, 0, per.fixedZRotation);
            }

            // 统一再加一个全局偏移
            child.localRotation = rot * Quaternion.Euler(0, 0, rotationOffset);
        }
    }
}