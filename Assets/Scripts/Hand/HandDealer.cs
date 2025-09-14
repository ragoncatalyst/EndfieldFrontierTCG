using UnityEngine;
using EndfieldFrontierTCG.CA;
using EndfieldFrontierTCG.Hand;

namespace EndfieldFrontierTCG.Hand
{
    // 简单发牌器：开场发三张（ID:1,2,3）到最近的 HandZone 的中间槽位左右排列
    [DefaultExecutionOrder(200)]
    public class HandDealer : MonoBehaviour
    {
        public CA_TypeManager typeManager;
        public Transform parentForCards;
        public int[] firstHandIds = new int[] { 1, 2, 3 };
        public bool clearBeforeDeal = true;
        public HandSplineZone targetSplineZone; // 指定 HandSplineZone（推荐）
        [Tooltip("中心两侧的槽位间隔（>1 更分散）")]
        public float centerSpacing = 1f;
        [Header("Visual Tuning")]
        [Tooltip("新发出的卡牌统一缩放。1 为预制体原始大小。")]
        public float defaultCardScale = 1.0f;
        [Tooltip("将手牌区域定位到主相机前方的距离（米），用于初始居中")] public float handDistanceFromCamera = 3.0f;
        [Tooltip("手牌区域在世界 Y 高度")] public float handZoneY = 0.6f;

        private HandSplineZone _activeZone;
        private float _lastSpacing;

        private void Start()
        {
            if (typeManager == null)
            {
                Debug.LogError("HandDealer: typeManager is null");
                return;
            }
            // 不再强制需要 HandZone；后续会优先使用 HandSplineZone，其次 HandZone
            // 关闭可能同时存在的 CardSpawner，避免它在我们之后再刷中心卡
            var spawners = GameObject.FindObjectsOfType<EndfieldFrontierTCG.DevTools.CardSpawner3D>(true);
            foreach (var s in spawners) s.enabled = false;

            StartCoroutine(DealAfterFrame());
        }

        private System.Collections.IEnumerator DealAfterFrame()
        {
            yield return null; // 等待所有 Start 运行完，避免其他脚本改位置

            _activeZone = targetSplineZone != null ? targetSplineZone : ResolveHandSplineZone();
            if (_activeZone == null)
            {
                Debug.LogError("HandDealer: no HandSplineZone in scene");
                yield break;
            }
            // 只有当目标是HandSplineZone时才设置位置
            if (_activeZone.GetType() == typeof(HandSplineZone))
            {
                // 将手牌区域移动到相机正前方，确保初始在视野中央
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 basePos = cam.transform.position + cam.transform.forward * handDistanceFromCamera;
                    basePos.y = handZoneY;
                    _activeZone.transform.position = basePos;
                    // 让手牌区域的朝向与相机水平一致，便于展开在屏幕左右
                    var e = _activeZone.transform.eulerAngles; e.y = cam.transform.eulerAngles.y; e.x = 0f; e.z = 0f;
                    _activeZone.transform.eulerAngles = e;
                }
            }
            _activeZone.slots = Mathf.Max(3, _activeZone.slots);
            if (clearBeforeDeal)
            {
                var oldAll = GameObject.FindObjectsOfType<CardView3D>(true);
                foreach (var c in oldAll) Destroy(c.gameObject);
            }
            // 中央起始槽位（优先使用曲线）
            int n = Mathf.Max(3, _activeZone.slots);
            float[] offsetsUnits = new float[] { -centerSpacing, 0f, centerSpacing };

            CardDatabase.EnsureLoaded();
            var views = new System.Collections.Generic.List<CardView3D>();
            for (int i = 0; i < firstHandIds.Length; i++)
            {
                int id = firstHandIds[i];
                if (!CardDatabase.TryGet(id, out var data)) continue;
                var parent = parentForCards != null ? parentForCards : _activeZone.transform;
                var go = typeManager.CreateCardByType(data.CA_Type, parent);
                if (go == null) { Debug.LogError($"HandDealer: prefab for id={id} null"); continue; }
                // 可见性与交互保底：层、缩放、渲染器、碰撞体
                go.layer = LayerMask.NameToLayer("Default");
                go.transform.localScale = Vector3.one * Mathf.Max(0.001f, defaultCardScale);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) { col.enabled = true; col.isTrigger = false; }
                var view = go.GetComponent<CardView3D>();
                if (view != null) { view.Bind(data); views.Add(view); }
                float units = i < offsetsUnits.Length ? offsetsUnits[i] : 0f;
                bool ok = _activeZone.AssignCardAtUnits(view, units);
                if (!ok) Debug.LogError($"HandDealer: assign failed units={units}");
                else if (view != null)
                {
                    Debug.Log($"[HandDealer] Dealt id={id} to units={units} pos={view.transform.position} layer={go.layer}");
                }
            }
            // 再等一帧后再次强制对齐，避免其他脚本后置改动导致初始未展开
            if (views.Count > 0)
            {
                _activeZone.RegisterCards(views.ToArray());
                StartCoroutine(ForceSnapNextFrame(views, offsetsUnits, n));
            }
            _lastSpacing = centerSpacing;
        }

        private System.Collections.IEnumerator ForceSnapNextFrame(System.Collections.Generic.List<CardView3D> views, float[] targetUnits, int n)
        {
            yield return null;
            // 旧的在左，新卡在右：如果三张固定为 1,2,3，则按 cardId 升序；否则按 createId 升序
            if (views.TrueForAll(v => v != null && v.cardId > 0))
                views.Sort((a, b) => a.cardId.CompareTo(b.cardId));
            else
                views.Sort((a, b) => a.createId.CompareTo(b.createId));
            // 生成以 0 为中心的单位偏移序列：0, -cs, +cs, -2cs, +2cs ...
            var candidateUnits = new System.Collections.Generic.List<float>(views.Count);
            candidateUnits.Add(0f);
            for (int step = 1; candidateUnits.Count < views.Count; step++)
            {
                float left = -step * centerSpacing;
                float right = +step * centerSpacing;
                if (candidateUnits.Count < views.Count) candidateUnits.Add(left);
                if (candidateUnits.Count < views.Count) candidateUnits.Add(right);
            }
            // 将这些位置按“屏幕从左到右”的顺序排序
            var cam = Camera.main;
            System.Comparison<float> compareByScreenX = (a, b) =>
            {
                Vector3 pa, pb; Quaternion ra, rb;
                _activeZone.TryGetPoseByUnits(a, out pa, out ra);
                _activeZone.TryGetPoseByUnits(b, out pb, out rb);
                Vector3 raDir = cam ? cam.transform.right : Vector3.right;
                float xa = Vector3.Dot(pa, raDir);
                float xb = Vector3.Dot(pb, raDir);
                return xa.CompareTo(xb);
            };
            candidateUnits.Sort(compareByScreenX);
            // 视图按 cardId/createId 升序，依次放到从左到右的槽位
            for (int i = 0; i < views.Count && i < candidateUnits.Count; i++)
            {
                var v = views[i]; if (v == null) continue;
                float units = candidateUnits[i];
                _activeZone.AssignCardAtUnits(v, units);
            }
            _activeZone.ForceRelayoutExistingCards();
        }

        private HandSplineZone ResolveHandSplineZone()
        {
            var zones = GameObject.FindObjectsOfType<HandSplineZone>(true);
            if (zones.Length == 0) return null;
            // 选离本物体最近的 HandSplineZone
            float best = float.MaxValue; HandSplineZone bestZ = null;
            foreach (var z in zones)
            {
                if (z == null) continue;
                float d = Vector3.SqrMagnitude(z.transform.position - transform.position);
                if (d < best) { best = d; bestZ = z; }
            }
            return bestZ;
        }

        private void Update()
        {
            if (!Application.isPlaying || _activeZone == null) return;
            if (_lastSpacing != centerSpacing)
            {
                _lastSpacing = centerSpacing;
                RelayoutExistingCards();
            }
        }

        private void RelayoutExistingCards()
        {
            var list = parentForCards != null ? parentForCards.GetComponentsInChildren<CardView3D>(true) : _activeZone.GetComponentsInChildren<CardView3D>(true);
            int count = list.Length;
            if (count == 0) return;
            // 对称单位偏移：0, -cs, +cs, -2cs, +2cs ...
            var units = new System.Collections.Generic.List<float>(count);
            units.Add(0f);
            for (int step = 1; units.Count < count; step++)
            {
                units.Add(-step * centerSpacing);
                if (units.Count < count) units.Add(+step * centerSpacing);
            }
            for (int i = 0; i < count; i++)
            {
                _activeZone.AssignCardAtUnits(list[i], units[i]);
            }
        }
    }
}


