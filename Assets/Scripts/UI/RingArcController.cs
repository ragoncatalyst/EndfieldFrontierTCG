using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 在 searchRoot 下递归寻找 Button（或特定 prefab 的 Button），
/// 为这些按钮生成环上的“投射弧”。弧厚=按钮高度；弧长≈按钮宽映射到圆周角度。
/// </summary>
[ExecuteAlways]
public class RingArcController : MonoBehaviour
{
    [Header("搜索范围")]
    public RectTransform searchRoot;     // 一般填 RingRoot（放所有菜单项的父物体）
    public bool includeInactive = false; // 是否包含隐藏按钮

    [Tooltip("只对这个 Prefab 的实例生成弧（可选，不填则对所有 Button 生效）")]
    public GameObject prefabFilter;      // 拖入按钮的 prefab 资产本体（不是场景实例）

    [Header("环参数（与视觉底环对齐）")]
    public float ringRadius = 300f;      // 弧段所在半径（落在环中线）
    public Color arcColor = new Color(1f, 0.9f, 0.2f, 1f);
    [Range(8, 256)] public int arcSegments = 64;

    [Header("生成到此处")]
    public RectTransform arcsParent;     // 用来装实例化出来的 UIRingArc
    public UIRingArc arcPrefab;          // 预制：挂了 UIRingArc

    readonly List<UIRingArc> _arcs = new();
    readonly List<Button> _buttons = new();

    void OnEnable() => Rebuild();
    void OnValidate() => Rebuild();
    void Update()    // 运行时可实时更新
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) Rebuild();
#endif
    }

    public void Rebuild()
    {
        if (searchRoot == null || arcsParent == null || arcPrefab == null) return;

        // 1) 找到所有 Button
        CollectButtons();

        // 2) 同步弧段数量
        EnsureArcCount(_buttons.Count);

        // 3) 根据按钮位置/尺寸计算弧段
        float circumference = 2f * Mathf.PI * ringRadius;

        for (int i = 0; i < _buttons.Count; i++)
        {
            var btn = _buttons[i];
            var arc = _arcs[i];
            var brt = btn.transform as RectTransform;

            // 3.1 把按钮中心位置换算到 searchRoot 的本地坐标系
            Vector2 centerLocal = WorldToLocal(searchRoot, brt.position);

            // 3.2 用世界空间大小估算 UI 宽高（考虑缩放）
            Vector2 sizeWorld = GetWorldSize(brt);
            float width  = sizeWorld.x;
            float height = sizeWorld.y;

            // 3.3 角度与角宽
            float centerDeg = Mathf.Atan2(centerLocal.y, centerLocal.x) * Mathf.Rad2Deg;
            float spanDeg   = (width / circumference) * 360f;
            float start     = centerDeg - spanDeg * 0.5f;
            float end       = centerDeg + spanDeg * 0.5f;

            // 3.4 厚度取按钮高度
            float thickness = Mathf.Max(1f, height);

            // 3.5 应用
            arc.color     = arcColor;
            arc.segments  = arcSegments;
            arc.SetArc(start, end, ringRadius, thickness);
            arc.raycastTarget = false; // 不挡住按钮
        }
    }

    void CollectButtons()
    {
        _buttons.Clear();
        if (searchRoot == null) return;

        var all = searchRoot.GetComponentsInChildren<Button>(includeInactive);
        foreach (var b in all)
        {
            if (!IsInPrefabFilter(b.gameObject)) continue;
            _buttons.Add(b);
        }
    }

    bool IsInPrefabFilter(GameObject go)
    {
        if (prefabFilter == null) return true; // 不筛选

#if UNITY_EDITOR
        // Editor 下用 PrefabUtility 判断此对象来源
        var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (source == null) return false;
        return source == prefabFilter;
#else
        // 运行时无法用 PrefabUtility：退化为名称对比（可改为用自定义标记组件）
        return go.name.StartsWith(prefabFilter.name);
#endif
    }

    void EnsureArcCount(int count)
    {
        // 删除多余
        for (int i = _arcs.Count - 1; i >= count; i--)
        {
            if (_arcs[i] != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(_arcs[i].gameObject);
                else Destroy(_arcs[i].gameObject);
#else
                Destroy(_arcs[i].gameObject);
#endif
            }
        }
        if (_arcs.Count > count) _arcs.RemoveRange(count, _arcs.Count - count);

        // 补足
        while (_arcs.Count < count)
        {
            var inst = Instantiate(arcPrefab, arcsParent);
            inst.raycastTarget = false;
            _arcs.Add(inst);
        }
    }

    // —— 工具：把世界坐标换到某 RectTransform 的本地（二维）——
    static Vector2 WorldToLocal(RectTransform parent, Vector3 worldPos)
    {
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent, RectTransformUtility.WorldToScreenPoint(null, worldPos),
            null, out localPoint);
        return localPoint;
    }

    // —— 工具：得到 RectTransform 的“世界尺度”宽高（考虑 lossyScale）——
    static Vector2 GetWorldSize(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        float w = Vector3.Distance(corners[3], corners[0]); // 左下到右下
        float h = Vector3.Distance(corners[1], corners[0]); // 左下到左上
        return new Vector2(w, h);
    }
}