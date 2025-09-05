using System.Collections;
using UnityEngine;
using TMPro;
using EndfieldFrontierTCG.CA;

// CardView3D (clean rewrite)
// - Leaf-in-water follow: position SmoothDamp + rotation leaning into motion
// - Low-speed wobble with hysteresis
// - Clear lifecycle: Pickup -> Drag -> Release
public class CardView3D : MonoBehaviour
{
    [Header("Refs")]
    public Rigidbody body;
    public BoxCollider box;
    public Renderer MainRenderer; // 可选：展示卡图
    public TMP_Text NameText;
    public TMP_Text HPText;
    public TMP_Text ATKText;
    public Texture2D MainTextureCache;
    [Header("Debug")]
    public bool debugHoverLogs = true;
    [SerializeField] private string cardViewRevision = "cv_rev_2025-09-03_01";
    [Tooltip("用于物理射线兜底检测的层遮罩（-1 表示全部层）")]
    public LayerMask interactLayerMask = ~0;
    [HideInInspector] public int handIndex = -1; // 注册顺序索引（仅用于查找）
    [HideInInspector] public int slotIndex = -1; // 当前所在槽位索引（用于布局计算）
    [HideInInspector] public int createId = -1;  // 创建顺序：旧卡小，新卡大
    private static int s_nextCreateId = 0;
    [HideInInspector] public int cardId = -1;    // 绑定的数据ID

    [Header("Heights")]
    public float dragPlaneY = 0.75f;
    public float groundY = 0f;

    [Header("Follow (Leaf)")]
    public float followSmooth = 0.06f;
    public float followLeanMaxDeg = 18f;
    public float followLeanResponsiveness = 16f;
    public float followLeanSpeedScale = 0.06f;
    [Tooltip("每轴最大偏离基准角度（度）")]
    public float maxAxisDeviationDeg = 10f;
    [Header("Start Impulse")]
    [Tooltip("拖拽起始阶段的短促强化持续时间（秒）")]
    public float startBoostTime = 0.15f;
    [Tooltip("起始阶段在倾斜角度上的乘数（>1 更明显）")]
    public float startBoostMul = 1.6f;
    [Header("Lift (Rising)")]
    [Tooltip("速度映射到上升位移的比例（单位/秒 -> 米）")]
    public float followLiftScale = 0.02f;
    [Tooltip("上升位移的最大值（米）")]
    public float followLiftMax = 0.12f;

    [Header("Wobble (Low-speed)")]
    public float wobbleAmpDeg = 3f; // 降低 y 轴等整体摆动感
    public float wobblePeriod = 1.4f;
    public float wobbleEaseIn = 0.15f;
    public float wobbleEaseOut = 0.12f;
    public float speedThreshold = 1.0f;
    public float speedHysteresis = 0.3f;

    [Header("Release")]
    public float dropArc = 0.25f;
    public float releaseDuration = 0.25f;

    [Header("Click Filtering")]
    [Tooltip("按下至松开的最大时间（秒），低于该值且移动距离很小则视为点击，不触发回家动画")]
    public float clickMaxDuration = 0.15f;
    [Tooltip("按下至松开的最大位移（米），低于该值且时间很短则视为点击")]
    public float clickMaxDistance = 0.05f;

    [Header("Return Visuals")]
    [Tooltip("在二阶段归位时，沿相机方向的前置偏移（米），避免被相邻卡遮挡")] public float returnFrontBias = 0.05f;
    [Tooltip("二阶段期间临时提高排序顺序，避免被相邻卡挡住")] public int returnSortingBoost = 20;

    [Header("Drag Visuals")]
    [Tooltip("拖拽时朝向相机偏移的距离（米），避免与手牌同平面发生穿插")] public float dragFrontBias = 0.03f;
    [Tooltip("拖拽时提升的排序顺序，适用于透明材质")] public int dragSortingBoost = 40;
    [Header("Collision Elevation")]
    [Tooltip("被阻挡时是否临时抬升 Y，避免卡到其他卡牌下方")] public bool elevateWhenBlocked = true;
    [Tooltip("阻挡时抬升的最大高度（米）")] public float elevateMax = 0.12f;
    [Tooltip("抬升上行速度（米/秒）")] public float elevateUpSpeed = 2.0f;
    [Tooltip("回落速度（米/秒）")] public float elevateDownSpeed = 2.5f;
    private float _elevateY = 0f;
    [Tooltip("软碰撞修正的平滑时间（秒），越大越柔和")]
    public float collisionSmoothTime = 0.06f;
    private Vector3 _collisionAdjust = Vector3.zero;
    private Vector3 _collisionAdjustVel = Vector3.zero;

    [Header("Return Curves")]
    [Tooltip("阶段1：水平 XZ 位移的速度曲线（0→1）")] public AnimationCurve returnPhase1XZCurve = new AnimationCurve(new Keyframe(0f,0f), new Keyframe(1f,1f));
    [Tooltip("阶段1：Y 回归的速度曲线（0→1）")] public AnimationCurve returnPhase1YCurve = new AnimationCurve(new Keyframe(0f,0f), new Keyframe(1f,1f));
    [Tooltip("阶段2：回到槽位的速度曲线（0→1）")] public AnimationCurve returnPhase2Curve = new AnimationCurve(new Keyframe(0f,0f), new Keyframe(1f,1f));

    private enum DragState { Idle, Picking, Dragging, Releasing }
    private DragState _state = DragState.Idle;
    public bool IsDragging => _state == DragState.Dragging || _state == DragState.Picking;

    private Camera _cam;
    private bool _hovering;
    private bool _usingFallbackInput;
    private Quaternion _baseRot;
    private Vector3 _dragOffsetWS;
    private bool _dragOffsetInitialized;
    private bool _dragFirstFrame; // kept for future use
    [Header("Drag Follow")]
    public float followDragSmooth = 0.04f;
    private Vector3 _lastTarget;
    private Vector3 _smoothVel;
    private Vector3 _dragVel;
    private float _tiltWeight;
    private Coroutine _wobbleCo;
    private Coroutine _fadeCo;
    // 拖拽开始时由力矩决定的摇晃方向（+1 顺时针进相位，-1 逆时针），以及是否已确定
    private float _wobbleDir = 1f;
    private bool _wobbleDirSet = false;
    private float _startBoostT = 0f;
    private float _mouseDownTime = 0f;
    private Vector3 _mouseDownPos;

    // Home pose for hand return
    private Vector3 _homePos;
    private Quaternion _homeRot;
    private bool _homeSet;
    private Transform _homeParent;          // zone transform captured when snapping
    private Vector3 _homeLocalPos;
    private Quaternion _homeLocalRot;
    private Coroutine _returnHomeCo;
    public bool IsReturningHome { get; private set; }
    private Renderer[] _allRenderers;
    private int[] _origSortingOrders;
    // 移除临时材质/排序强制逻辑，避免停顿与回撤

    [Header("Hover Collider Extend")]
    [Tooltip("当被 hover 时，沿本地 -Z 方向额外延伸的碰撞箱距离（米）")]
    public float hoverColliderExtendZ = 0.06f;
    private bool _boxExtended = false;
    private Vector3 _boxOrigSize;
    private Vector3 _boxOrigCenter;

    [Header("Shadow Caster Helper")]
    [Tooltip("为保证贴近桌面时也有阴影，添加一个仅投影的辅助 Quad")]
    public bool addShadowCasterHelper = true;
    public float shadowCasterZOffset = -0.001f;
    public float shadowCasterScaleMul = 1.02f;
    private Renderer _shadowCasterRenderer;

    [Header("Input Mode")] public bool suppressOnMouseHandlers = true; // 由 HandSplineZone 集中驱动时开启
    private bool _allowInternalInvoke = false; // 外部桥调用时临时打开

    // === External drag bridge ===
    private bool _externalHolding = false;
    public void ExternalBeginDrag()
    {
        if (!IsDragging)
        {
            _externalHolding = true;
            _allowInternalInvoke = true; OnMouseDown(); _allowInternalInvoke = false;
        }
    }
    public void ExternalDrag()
    {
        if (_externalHolding)
        {
            _allowInternalInvoke = true; OnMouseDrag(); _allowInternalInvoke = false;
        }
    }
    public void ExternalEndDrag()
    {
        if (_externalHolding)
        {
            _allowInternalInvoke = true; OnMouseUp(); _allowInternalInvoke = false;
            _externalHolding = false;
        }
    }

    private void Awake()
    {
        _cam = Camera.main != null ? Camera.main : Camera.current;
        if (body == null) body = GetComponent<Rigidbody>();
        if (box == null) box = GetComponent<BoxCollider>();
        if (box == null) box = GetComponentInChildren<BoxCollider>(true);
        createId = s_nextCreateId++;
        _baseRot = Quaternion.Euler(90f, 0f, 0f);
        if (body != null)
        {
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.constraints = RigidbodyConstraints.FreezeAll;
        }
        if (box != null)
        {
            box.enabled = true;
            box.isTrigger = false; // 默认非触发器，确保 OnMouse 事件可被拾取
        }
        _allRenderers = GetComponentsInChildren<Renderer>(true);
        if (_allRenderers != null && _allRenderers.Length > 0)
        {
            int n = _allRenderers.Length;
            _origSortingOrders = new int[n];
            for (int i = 0; i < n; i++)
            {
                var r = _allRenderers[i];
                _origSortingOrders[i] = r.sortingOrder;
                // 启用投射阴影与接收阴影
                try { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; } catch {}
            }
        }
        if (debugHoverLogs) Debug.Log($"[CardView3D] Awake collider={(box!=null)} trigger={box?.isTrigger} layer={gameObject.layer} ({cardViewRevision})");

        if (addShadowCasterHelper) EnsureShadowCaster();
    }

    private void Update()
    {
        if (!suppressOnMouseHandlers)
        {
            if (_cam == null) _cam = Camera.main != null ? Camera.main : Camera.current;
            if (_cam == null) return;
            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 1000f, interactLayerMask, QueryTriggerInteraction.Collide);
            if (hits != null && hits.Length > 0)
            {
                CardView3D topMost = null; float topY = float.NegativeInfinity;
                foreach (var h in hits)
                {
                    var cv = h.collider.GetComponentInParent<CardView3D>(); if (cv == null) continue;
                    float y = h.collider.bounds.max.y; if (y > topY) { topY = y; topMost = cv; }
                }
                bool onSelf = (topMost == this);
                if (onSelf && !_hovering) { _hovering = true; OnMouseEnter(); }
                else if (!onSelf && _hovering) { _hovering = false; OnMouseExit(); }
                if (onSelf && Input.GetMouseButtonDown(0)) { _usingFallbackInput = true; OnMouseDown(); }
            }
            else if (_hovering) { _hovering = false; OnMouseExit(); }
            if (_usingFallbackInput)
            {
                if (Input.GetMouseButton(0)) { OnMouseDrag(); }
                if (Input.GetMouseButtonUp(0)) { _usingFallbackInput = false; OnMouseUp(); }
            }
        }
        // 全局兜底：鼠标松开无论指针在哪里都结束拖拽，避免状态卡住导致只能拖一次
        if (IsDragging && !Input.GetMouseButton(0))
        {
            _allowInternalInvoke = true; OnMouseUp(); _allowInternalInvoke = false;
            _externalHolding = false;
        }
        if (_shadowCasterRenderer != null) UpdateShadowCasterSize();
        // 拖拽静止帧也要做一次碰撞与抬升修正，避免停住仍穿模或卡在下方
        if (_state == DragState.Dragging)
        {
            bool blocked;
            Vector3 corr = AdjustTargetForCollisions(transform.position, transform.position, out blocked);
            if ((corr - transform.position).sqrMagnitude > 1e-8f)
            {
                transform.position = corr;
            }
            if (elevateWhenBlocked)
            {
                float up = elevateUpSpeed * Time.deltaTime;
                float down = elevateDownSpeed * Time.deltaTime;
                float planeY = dragPlaneY;
                _elevateY = Mathf.Clamp(blocked ? (_elevateY + up) : (_elevateY - down), 0f, Mathf.Max(0f, elevateMax));
                Vector3 p = transform.position; p.y = planeY + _elevateY; transform.position = p;
            }
        }
        if (_state == DragState.Dragging)
        {
            bool blocked;
            Vector3 corr = AdjustTargetForCollisions(transform.position, transform.position, out blocked);
            _collisionAdjust = Vector3.SmoothDamp(_collisionAdjust, corr - transform.position, ref _collisionAdjustVel, Mathf.Max(0.01f, collisionSmoothTime));
            if (_collisionAdjust.sqrMagnitude > 0f) transform.position += _collisionAdjust;
            if (elevateWhenBlocked)
            {
                float up = elevateUpSpeed * Time.deltaTime;
                float down = elevateDownSpeed * Time.deltaTime;
                float planeY = dragPlaneY;
                _elevateY = Mathf.Clamp(blocked ? (_elevateY + up) : (_elevateY - down), 0f, Mathf.Max(0f, elevateMax));
                Vector3 p = transform.position; p.y = planeY + _elevateY; transform.position = p;
            }
        }
        if (_state == DragState.Dragging)
        {
            bool blocked2;
            Vector3 corr2 = AdjustTargetForCollisions(transform.position, transform.position, out blocked2);
            if ((corr2 - transform.position).sqrMagnitude > 1e-8f) transform.position = corr2;
        }
    }

    public void SetHomePose(Vector3 pos, Quaternion rot)
    {
        _homePos = pos; _homeRot = rot; _homeSet = true;
    }

    public void SetHomeFromZone(Transform zone, Vector3 worldPos, Quaternion worldRot)
    {
        _homeParent = zone;
        _homeLocalPos = zone != null ? zone.InverseTransformPoint(worldPos) : worldPos;
        _homeLocalRot = zone != null ? Quaternion.Inverse(zone.rotation) * worldRot : worldRot;
        _homePos = worldPos; _homeRot = worldRot; _homeSet = true;
    }

    private void GetHomeWorldPose(out Vector3 pos, out Quaternion rot)
    {
        if (_homeParent != null)
        {
            pos = _homeParent.TransformPoint(_homeLocalPos);
            rot = _homeParent.rotation * _homeLocalRot;
        }
        else { pos = _homePos; rot = _homeRot; }
    }

    public void SnapTo(Vector3 pos, Quaternion rot)
    {
        StopAllCoroutines();
        transform.position = pos;
        transform.rotation = rot;
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
        }
        _state = DragState.Idle;
        _tiltWeight = 0f;
        SetHomePose(pos, rot);
    }

    public void BeginSmoothReturnToHome(float aheadZ, float phase1Time, float phase2Time)
    {
        if (!_homeSet) return;
        if (_returnHomeCo != null) StopCoroutine(_returnHomeCo);
        _returnHomeCo = StartCoroutine(ReturnToHomeTwoPhase(aheadZ, phase1Time, phase2Time));
    }

    private IEnumerator ReturnToHomeTwoPhase(float aheadZ, float t1, float t2)
    {
        // 在归位阶段尽量隔离物理：不禁用刚体组件本身（Rigidbody 没有 enabled），只关闭碰撞/重力并冻结
        bool saved = false; bool prevBoxEnabled=false; bool prevKin=false, prevGrav=false, prevDetect=false; RigidbodyConstraints prevCons=RigidbodyConstraints.None;
        if (body != null)
        {
            saved = true;
            prevKin = body.isKinematic; prevGrav = body.useGravity; prevDetect = body.detectCollisions; prevCons = body.constraints;
            // 先停用碰撞与重力，再直接禁用组件
            body.detectCollisions = false;
            body.useGravity = false;
            body.isKinematic = true;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.Sleep();
        }
        if (box != null)
        {
            saved = true;
            prevBoxEnabled = box.enabled;
            box.enabled = false;
        }
        IsReturningHome = true;
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        float homeY = _homePos.y;
        // temp point just "above" home along +Z on XZ plane (world Z+)
        Vector3 tempXZ = new Vector3(_homePos.x, homeY, _homePos.z + aheadZ);

        // 不再修改材质/深度与 sortingOrder，避免在阶段切换处出现停顿与回撤

        // Phase 1a (duration t1): move XZ from current -> tempXZ, keep Y unchanged（曲线可配）
        float t = 0f;
        float dur1 = Mathf.Max(0.0001f, t1);
        Vector2 startXZ = new Vector2(startPos.x, startPos.z);
        Vector2 tempXZ2 = new Vector2(tempXZ.x, tempXZ.z);
        while (t < dur1)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur1);
            float k = EvaluateReturnCurve(returnPhase1XZCurve, a);
            Vector2 xz = Vector2.LerpUnclamped(startXZ, tempXZ2, k);
            transform.position = new Vector3(xz.x, startPos.y, xz.y);
            transform.rotation = Quaternion.Slerp(startRot, _homeRot, k * 0.4f);
            yield return null;
        }
        // Phase 1b: 等待/拉回 Y 到 homeY（曲线可配）
        t = 0f; float dur1b = Mathf.Max(0.0001f, t1);
        float yStart = transform.position.y;
        while (t < dur1b)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur1b);
            float k = EvaluateReturnCurve(returnPhase1YCurve, a);
            float y = Mathf.LerpUnclamped(yStart, homeY, k);
            Vector3 p = transform.position; p.y = y; transform.position = p;
            transform.rotation = Quaternion.Slerp(transform.rotation, _homeRot, k * 0.3f);
            yield return null;
        }
        // 确保在进入阶段2之前，Y 已经完全等于 homeY，并在 Z+ 相邻点停留一帧
        {
            Vector3 p = transform.position; p.y = homeY; transform.position = p; 
            yield return null;
        }
        // Phase 2 独立实现：曲线可配 + y 恒定
        yield return ReturnPhase2EaseIn(t2);
        _returnHomeCo = null;
        IsReturningHome = false;
        _state = DragState.Idle;
        if (saved)
        {
            if (body != null)
            {
                body.constraints = prevCons;
                body.detectCollisions = prevDetect;
                body.useGravity = prevGrav;
                body.isKinematic = prevKin;
            }
            if (box != null) box.enabled = prevBoxEnabled;
        }

        // 无需还原材质，因为没有修改
    }

    private IEnumerator ReturnPhase2EaseIn(float t2)
    {
        float homeY = _homePos.y;
        Vector3 startP2 = transform.position;
        Quaternion startR2 = transform.rotation;
        Vector3 endP2 = new Vector3(_homePos.x, homeY, _homePos.z);
        float dur2 = Mathf.Max(0.01f, t2);
        float tP2 = 0f;
        // 提升排序，避免二阶段被挡
        if (_allRenderers != null)
        {
            for (int i = 0; i < _allRenderers.Length; i++)
            {
                try { _allRenderers[i].sortingOrder = _origSortingOrders[i] + returnSortingBoost; } catch {}
            }
        }
        while (tP2 < dur2)
        {
            tP2 += Time.deltaTime;
            float a = Mathf.Clamp01(tP2 / dur2);
            float k = EvaluateReturnCurve(returnPhase2Curve, a);
            Vector3 pos = Vector3.LerpUnclamped(startP2, endP2, k);
            pos.y = homeY; // y 恒定
            transform.position = pos;
            transform.rotation = Quaternion.Slerp(startR2, _homeRot, k);
            yield return null;
        }
        // 结束时不做硬性终点校正，避免“最后一下”；位置已非常接近终点
        // 保持当前 transform 即可
        if (_allRenderers != null)
        {
            for (int i = 0; i < _allRenderers.Length; i++)
            {
                try { _allRenderers[i].sortingOrder = _origSortingOrders[i]; } catch {}
            }
        }
        // 让一帧过去再退出，保证排序恢复不会与最后一帧位置写入产生竞争
        yield return null;
        InitializeForNextDrag();
        // 允许下一帧开始接受新的 hover
        yield return null;
    }

    private static float EvaluateReturnCurve(AnimationCurve curve, float a)
    {
        if (curve == null || curve.length == 0)
        {
            // 默认 smoothstep
            return a * a * (3f - 2f * a);
        }
        return Mathf.Clamp01(curve.Evaluate(Mathf.Clamp01(a)));
    }

    // 绑定 CSV 数据（名称、数值、主图 & 特效）
    public void Bind(CardData data)
    {
        if (data == null) return;
        // 记录卡牌唯一ID，便于排序
        cardId = data.CA_ID;
        gameObject.layer = LayerMask.NameToLayer("Default");
        if (box != null) { box.enabled = true; box.isTrigger = false; }
        var mrAll = GetComponentsInChildren<Renderer>(true);
        foreach (var r in mrAll) if (r != null) r.enabled = true;
        if (NameText != null) NameText.text = data.CA_Name_DIS;
        if (HPText != null) HPText.text = data.CA_HPMaximum.ToString();
        if (ATKText != null) ATKText.text = data.CA_ATK_INI.ToString();
        // 尝试加载主图（Resources/CA_MainImages/<name>）
        if (MainRenderer != null && !string.IsNullOrEmpty(data.CA_MainImage))
        {
            var tex = Resources.Load<Texture2D>("CA_MainImages/" + data.CA_MainImage);
            if (tex != null)
            {
                MainTextureCache = tex;
                if (MainRenderer.material != null)
                {
                    MainRenderer.material.mainTexture = tex;
                }
            }
        }
        // 初始不绑定会产生自发运动的特效。等到真正部署/触发时再调用效果系统。
        // 初始仅显示主图，文字隐藏，悬停时再显示
        if (NameText != null) NameText.gameObject.SetActive(false);
        if (HPText != null) HPText.gameObject.SetActive(false);
        if (ATKText != null) ATKText.gameObject.SetActive(false);
        EnableShadows(true);
        UpdateShadowCasterSize();
        if (debugHoverLogs) Debug.Log($"[CardView3D] Bind ok id={cardId} at {transform.position}");
    }

    public void SetInfoVisible(bool visible)
    {
        if (NameText != null) NameText.gameObject.SetActive(visible);
        if (HPText != null) HPText.gameObject.SetActive(visible);
        if (ATKText != null) ATKText.gameObject.SetActive(visible);
    }

    private void OnMouseEnter()
    {
        if (suppressOnMouseHandlers && !_allowInternalInvoke) return;
        var zones = GameObject.FindObjectsOfType<EndfieldFrontierTCG.Hand.HandSplineZone>(true);
        foreach (var z in zones)
        {
            if (z != null && z.NotifyHover(this, true)) break;
        }
        if (debugHoverLogs) Debug.Log($"[CardView3D] HoverEnter idx={handIndex}");
        SetInfoVisible(true);
        ApplyHoverColliderExtend(true);
    }

    private void OnMouseExit()
    {
        if (suppressOnMouseHandlers && !_allowInternalInvoke) return;
        var zones = GameObject.FindObjectsOfType<EndfieldFrontierTCG.Hand.HandSplineZone>(true);
        foreach (var z in zones)
        {
            if (z != null && z.NotifyHover(this, false)) break;
        }
        if (debugHoverLogs) Debug.Log($"[CardView3D] HoverExit idx={handIndex}");
        SetInfoVisible(false);
        ApplyHoverColliderExtend(false);
    }

    private void OnMouseDown()
    {
        if (suppressOnMouseHandlers && !_allowInternalInvoke) return;
        // Reset state
        _state = DragState.Picking;
        if (_returnHomeCo != null) { StopCoroutine(_returnHomeCo); _returnHomeCo = null; IsReturningHome = false; }
        _tiltWeight = 0f;
        _wobbleDir = 1f;
        _wobbleDirSet = false;
        _startBoostT = startBoostTime;
        if (_wobbleCo != null) { StopCoroutine(_wobbleCo); _wobbleCo = null; }
        if (_fadeCo != null) { StopCoroutine(_fadeCo); _fadeCo = null; }
        if (body != null)
        {
            // 拖拽时允许位置与旋转插值，释放旋转约束
            body.useGravity = false;
            body.isKinematic = true;
            body.constraints = RigidbodyConstraints.FreezePositionY; // 由代码控制 Y 平面
        }
        // 为了拖拽与 OnMouse 事件，保持非触发器。如果需要物理禁用，用 FreezeAll 代替
        if (box != null) box.isTrigger = false;

        // 推迟到真正进入拖拽平面后再计算鼠标-物体偏移，避免因手牌父级旋转/升降导致的初始错位
        _dragOffsetInitialized = false;
        _dragOffsetWS = Vector3.zero;
        _lastTarget = transform.position;
        _dragFirstFrame = true;
        _mouseDownTime = Time.unscaledTime;
        _mouseDownPos = transform.position;
        if (debugHoverLogs) Debug.Log($"[CardView3D] DragStart idx={handIndex} pos={transform.position}");
        StartCoroutine(PickupLift());
        BoostDragRendering(true);
    }

    private IEnumerator PickupLift()
    {
        float t = 0f, dur = 0.12f;
        Vector3 start = transform.position;
        Vector3 end = new Vector3(start.x, dragPlaneY, start.z) + CameraForwardPlanar() * dragFrontBias;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            transform.position = Vector3.Lerp(start, end, p);
            yield return null;
        }
        _state = DragState.Dragging;
    }

    private void OnMouseDrag()
    {
        if (suppressOnMouseHandlers && !_allowInternalInvoke) return;
        if (_state != DragState.Dragging && _state != DragState.Picking) return;
        // 用当前高度作为拖拽平面，避免因动画升降导致的错层
        float planeY = dragPlaneY;
        if (TryRayOnPlane(planeY, out Vector3 pt))
        {
            if (!_dragOffsetInitialized)
            {
                _dragOffsetWS = transform.position - pt;
                _dragOffsetInitialized = true;
            }
            Vector3 target = pt + _dragOffsetWS + CameraForwardPlanar() * dragFrontBias;
            target.y = planeY;
            // 在不使用物理力的情况下，基于 ComputePenetration 进行软碰撞修正，避免穿模；必要时临时抬升
            bool blocked;
            Vector3 corrT = AdjustTargetForCollisions(transform.position, target, out blocked);
            Vector3 delta = corrT - target;
            _collisionAdjust = Vector3.SmoothDamp(_collisionAdjust, delta, ref _collisionAdjustVel, Mathf.Max(0.01f, collisionSmoothTime));
            target += _collisionAdjust;
            if (elevateWhenBlocked)
            {
                float up = elevateUpSpeed * Time.deltaTime;
                float down = elevateDownSpeed * Time.deltaTime;
                _elevateY = Mathf.Clamp(blocked ? (_elevateY + up) : (_elevateY - down), 0f, Mathf.Max(0f, elevateMax));
                target.y = planeY + _elevateY;
            }
            // 恢复“轻微平滑延迟”的跟随：SmoothDamp 到目标
            transform.position = Vector3.SmoothDamp(transform.position, target, ref _smoothVel, followDragSmooth);
            _dragFirstFrame = false;

            Vector3 inst = (target - _lastTarget);
            _dragVel = Vector3.Lerp(_dragVel, inst / Mathf.Max(0.0001f, Time.deltaTime), 0.35f);
            _lastTarget = target;

            ApplyFollowLean(_dragVel);
            // 首次确定摇晃方向：用 box 中心到当前命中点的 r，与平面速度 F 的叉积的 y 符号
            if (!_wobbleDirSet && box != null)
            {
                Vector3 centerWS = transform.TransformPoint(box.center);
                Vector3 r = (target - centerWS);
                Vector3 F = Vector3.ProjectOnPlane(_dragVel.sqrMagnitude > 1e-6f ? _dragVel : (target - transform.position) / Mathf.Max(0.0001f, Time.deltaTime), Vector3.up);
                float s = Vector3.Dot(Vector3.Cross(r, F), Vector3.up);
                if (Mathf.Abs(s) > 1e-5f)
                {
                    // 映射方向整体反过来
                    _wobbleDir = s >= 0f ? -1f : 1f;
                    _wobbleDirSet = true;
                }
            }
            GateWobbleBySpeed(_dragVel);
        }
    }

    private Vector3 CameraForwardPlanar()
    {
        if (_cam == null) _cam = Camera.main != null ? Camera.main : Camera.current;
        Vector3 f = _cam != null ? _cam.transform.forward : Vector3.forward;
        f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; return f.normalized;
    }

    private void BoostDragRendering(bool enable)
    {
        if (_allRenderers == null || _allRenderers.Length == 0) _allRenderers = GetComponentsInChildren<Renderer>(true);
        if (_allRenderers == null) return;
        for (int i = 0; i < _allRenderers.Length; i++)
        {
            var r = _allRenderers[i]; if (r == null) continue;
            try
            {
                r.sortingOrder = enable ? (_origSortingOrders != null && i < _origSortingOrders.Length ? _origSortingOrders[i] + dragSortingBoost : r.sortingOrder + dragSortingBoost)
                                        : (_origSortingOrders != null && i < _origSortingOrders.Length ? _origSortingOrders[i] : r.sortingOrder);
            } catch {}
        }
    }

    // 软碰撞：用 Physics.ComputePenetration 对目标点做位移修正，避免与其它卡片/桌面穿模
    private Vector3 AdjustTargetForCollisions(Vector3 current, Vector3 target, out bool blocked)
    {
        blocked = false;
        if (box == null) return target;
        Vector3 corrected = target;
        Vector3 halfExt = box.size * 0.5f * 1.02f;
        Quaternion rot = transform.rotation;
        for (int iter = 0; iter < 2; iter++)
        {
            Collider[] overlap = Physics.OverlapBox(corrected + rot * box.center, halfExt, rot, interactLayerMask, QueryTriggerInteraction.Ignore);
            bool any = false;
            for (int i = 0; i < overlap.Length; i++)
            {
                var other = overlap[i]; if (other == null) continue; if (other.transform == transform) continue;
                Vector3 dir; float dist;
                if (Physics.ComputePenetration(box, corrected, rot, other, other.transform.position, other.transform.rotation, out dir, out dist) && dist > 0f)
                {
                    // 只允许 Y+ 分离（绝不沿 X/Z 顶开）
                    Vector3 prefer = new Vector3(0f, Mathf.Max(0f, dir.y), 0f);
                    if (prefer.sqrMagnitude < 1e-8f) prefer = Vector3.up; // 兜底：至少向上分离
                    corrected += prefer.normalized * dist;
                    any = true;
                    blocked = true;
                }
            }
            if (!any) break;
        }
        return corrected;
    }

    private void OnMouseUp()
    {
        if (suppressOnMouseHandlers && !_allowInternalInvoke) return;
        if (_state == DragState.Idle) return;
        // 点击过滤：按下时间短且位移极小 -> 直接复位，不触发回家/下落流程
        float held = Time.unscaledTime - _mouseDownTime;
        float moved = Vector3.Distance(transform.position, _mouseDownPos);
        if (_state == DragState.Picking || (held <= clickMaxDuration && moved <= clickMaxDistance))
        {
            InitializeForNextDrag();
            var zone0 = GetComponentInParent<EndfieldFrontierTCG.Hand.HandSplineZone>();
            if (zone0 != null) { try { zone0.ClearInputState(); } catch {} }
            return;
        }
        _state = DragState.Releasing;
        BoostDragRendering(false);
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        StartCoroutine(FadeTilt(0f, wobbleEaseOut));
        if (_wobbleCo != null) { StopCoroutine(_wobbleCo); _wobbleCo = null; }
        // 不切换到 kinematic，避免物理状态切换造成末尾突跳。改为在回家协程内部临时冻结
        // 若该牌属于某个 HandSplineZone（已设置 homePose），优先按两阶段“回手牌”路径归位
        var zone = GetComponentInParent<EndfieldFrontierTCG.Hand.HandSplineZone>();
        if (zone == null)
        {
            var zones = GameObject.FindObjectsOfType<EndfieldFrontierTCG.Hand.HandSplineZone>(true);
            float best = float.MaxValue; EndfieldFrontierTCG.Hand.HandSplineZone bestZ = null;
            foreach (var z in zones)
            {
                if (z == null) continue;
                float d = (transform.position - z.transform.position).sqrMagnitude;
                if (d < best) { best = d; bestZ = z; }
            }
            zone = bestZ;
        }
        if (zone != null && _homeSet)
        {
            // 若在按下后极短时间松手，直接走回家逻辑；并且在内部阶段结束后会调用 ClearInputState()
            zone.TryReturnCardToHome(this);
            return;
        }
        // 否则走默认抛物线落地
        StartCoroutine(ReleaseDrop());
    }

    private void ApplyFollowLean(Vector3 velocity)
    {
        Vector3 vPlanar = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float vMag = vPlanar.magnitude;
        Quaternion followRot = _baseRot;
        if (vMag > 1e-4f)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, vPlanar).normalized;
            float boostK = 1f;
            if (_startBoostT > 0f)
            {
                boostK = Mathf.Lerp(1f, startBoostMul, _startBoostT / Mathf.Max(0.0001f, startBoostTime));
                _startBoostT = Mathf.Max(0f, _startBoostT - Time.deltaTime);
            }
            float ang = Mathf.Clamp(vMag * followLeanSpeedScale * boostK, 0f, followLeanMaxDeg);
            // 统一为“乘风而起”：风从下方掠过，速度方向那一侧被抬起
            followRot = Quaternion.AngleAxis(-ang, axis) * _baseRot;
        }
        // 仅脚本驱动旋转；避免写入刚体的速度（kinematic 提示）
        if (body != null && !body.isKinematic)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
        followRot = SoftLimitToBaseRotation(followRot);
        // 指数平滑（避免线性插值在大 dt 时产生机械感）
        float alpha = 1f - Mathf.Exp(-followLeanResponsiveness * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, followRot, alpha);
        // 拖拽中不做额外抬升，避免改变跟随平面的 y（造成鼠标与物体错位）
        if (_state != DragState.Dragging)
        {
            float lift = Mathf.Clamp(vMag * followLiftScale, 0f, followLiftMax);
            transform.position += Vector3.up * lift;
        }
    }

    private void GateWobbleBySpeed(Vector3 velocity)
    {
        float v = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
        float high = Mathf.Max(0f, speedThreshold);
        float low = Mathf.Max(0f, speedThreshold - Mathf.Max(0f, speedHysteresis));
        if (v > high)
        {
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(FadeTilt(0f, wobbleEaseOut));
        }
        else if (v < low)
        {
            if (_wobbleCo == null) _wobbleCo = StartCoroutine(WobbleLoop());
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(FadeTilt(1f, wobbleEaseIn));
        }
    }

    private IEnumerator ReleaseDrop()
    {
        Vector3 start = transform.position;
        Vector3 end = new Vector3(transform.position.x, groundY, transform.position.z);
        Quaternion startR = transform.rotation;
        Quaternion endR = _baseRot;
        float t = 0f, dur = Mathf.Max(0.05f, releaseDuration);
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            float yArc = dropArc * 4f * p * (1f - p);
            Vector3 pos = Vector3.Lerp(start, end, p);
            pos.y += yArc;
            transform.position = pos;
            transform.rotation = Quaternion.Slerp(startR, endR, Mathf.SmoothStep(0f, 1f, p));
            yield return null;
        }
        transform.position = end;
        transform.rotation = endR;
        if (body != null)
        {
            body.useGravity = false;
            body.isKinematic = true;
            body.constraints = RigidbodyConstraints.FreezeAll;
        }
        _state = DragState.Idle;
        InitializeForNextDrag();
        // 通知手牌区清理输入残留，避免“松手瞬间”留下按压状态导致后续无反馈
        var zone = GetComponentInParent<EndfieldFrontierTCG.Hand.HandSplineZone>();
        if (zone != null) { try { zone.ClearInputState(); } catch {} }
        yield return null;
    }

    private IEnumerator WobbleLoop()
    {
        float t = 0f;
        while (_state == DragState.Dragging)
        {
            t += Time.deltaTime;
            if (_tiltWeight > 0.0001f)
            {
                // 基于方向的相位推进：_wobbleDir=+1 顺时针，-1 逆时针
                float phase = Mathf.Repeat((t * _wobbleDir) / wobblePeriod, 1f);
                // 避免到达边界产生“顶死”感：根据当前偏离量减少振幅
                float currentDev = GetMaxAxisDeviationDeg(transform.rotation);
                float headroom = Mathf.Max(0f, maxAxisDeviationDeg - currentDev - 0.5f);
                float amp = Mathf.Min(wobbleAmpDeg * _tiltWeight, headroom);
                float dx = Mathf.Sin(phase * Mathf.PI * 2f) * amp;
                float dz = Mathf.Sin((phase + 0.25f * _wobbleDir) * Mathf.PI * 2f) * amp;
                Quaternion q = Quaternion.AngleAxis(dx, transform.right) * Quaternion.AngleAxis(dz, transform.forward) * transform.rotation;
                q = SoftLimitToBaseRotation(q);
                float a = 1f - Mathf.Exp(-8f * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, q, a);
            }
            yield return null;
        }
        _wobbleCo = null;
    }

    private IEnumerator FadeTilt(float target, float dur)
    {
        float start = _tiltWeight;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = dur > 0f ? Mathf.Clamp01(t / dur) : 1f;
            p = p * p * (3f - 2f * p);
            _tiltWeight = Mathf.Lerp(start, target, p);
            yield return null;
        }
        _tiltWeight = target;
        _fadeCo = null;
    }

    private bool TryRayOnPlane(float planeY, out Vector3 hit)
    {
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        var ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (plane.Raycast(ray, out float enter))
        {
            hit = ray.GetPoint(enter);
            return true;
        }
        hit = Vector3.zero;
        return false;
    }

    // 供手牌区调用：吸附到指定位置与朝向，并静止
    // (legacy) kept above; the newer SnapTo defined earlier stores home pose as well.

    // 将目标旋转限制在以 _baseRot 为中心、每轴 ±maxAxisDeviationDeg 的范围内
    // 软限制：接近边界时采用平滑缓冲，避免“撞墙停顿”
    private Quaternion SoftLimitToBaseRotation(Quaternion target)
    {
        Quaternion delta = Quaternion.Inverse(_baseRot) * target; // 相对基准
        Vector3 euler = delta.eulerAngles;
        euler.x = Normalize180(euler.x);
        euler.y = Normalize180(euler.y);
        euler.z = Normalize180(euler.z);
        float m = Mathf.Abs(maxAxisDeviationDeg);
        euler.x = SoftClamp(euler.x, m);
        euler.y = SoftClamp(euler.y, m);
        euler.z = SoftClamp(euler.z, m);
        return _baseRot * Quaternion.Euler(euler);
    }

    private static float Normalize180(float angle)
    {
        angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
        return angle;
    }

    // 在 [-limit, limit] 上的软夹紧：中心段线性，接近边缘时用平滑曲线减速
    private static float SoftClamp(float v, float limit)
    {
        float t = Mathf.Abs(v) / Mathf.Max(0.0001f, limit);
        if (t <= 0.85f) return Mathf.Clamp(v, -limit, limit); // 85% 内正常
        // 进入缓冲区：使用平滑函数把增长减缓到 0
        float sign = Mathf.Sign(v);
        float over = Mathf.Clamp01((t - 0.85f) / 0.15f); // 0..1
        float eased = 1f - (1f - over) * (1f - over);   // easeOutQuad
        float scaled = Mathf.Lerp(Mathf.Abs(v), limit, eased);
        return sign * Mathf.Min(scaled, limit);
    }

    private float GetMaxAxisDeviationDeg(Quaternion q)
    {
        Quaternion delta = Quaternion.Inverse(_baseRot) * q;
        Vector3 e = delta.eulerAngles;
        e.x = Mathf.Abs(Normalize180(e.x));
        e.y = Mathf.Abs(Normalize180(e.y));
        e.z = Mathf.Abs(Normalize180(e.z));
        return Mathf.Max(e.x, e.y, e.z);
    }

    // 恢复到“可再次拖拽”的干净状态
    private void InitializeForNextDrag()
    {
        // 清理动态变量
        _dragOffsetInitialized = false;
        _usingFallbackInput = false;
        _hovering = false;
        _externalHolding = false;
        _wobbleDir = 1f;
        _wobbleDirSet = false;
        _tiltWeight = 0f;
        _startBoostT = 0f;
        _dragVel = Vector3.zero;
        _smoothVel = Vector3.zero;
        _lastTarget = transform.position;
        // 旋转回归到基准软限制
        transform.rotation = SoftLimitToBaseRotation(_baseRot);
        // 刚体/碰撞体安全状态
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = true;
            body.constraints = RigidbodyConstraints.FreezeAll;
        }
        if (box != null)
        {
            box.enabled = true;
            box.isTrigger = false;
            box.gameObject.layer = LayerMask.NameToLayer("Default");
            // 恢复碰撞箱
            if (_boxExtended)
            {
                box.size = _boxOrigSize;
                box.center = _boxOrigCenter;
                _boxExtended = false;
            }
        }
        gameObject.layer = LayerMask.NameToLayer("Default");
        // 确保始终可被射线命中（即使有父物体临时禁用）
        var rends = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++) if (rends[i] != null) rends[i].enabled = true;
        EnableShadows(true);
        // 让 HandSplineZone 重新感知状态（避免遗留 hover/return 标记）
        var zones = GameObject.FindObjectsOfType<EndfieldFrontierTCG.Hand.HandSplineZone>(true);
        foreach (var z in zones) { try { z.NotifyHover(this, false); } catch { } }
        if (debugHoverLogs) Debug.Log($"[CardView3D] Reset OK (id={cardId}) ready");
    }

    public void EnableShadows(bool enable)
    {
        if (_allRenderers == null || _allRenderers.Length == 0) _allRenderers = GetComponentsInChildren<Renderer>(true);
        if (_allRenderers == null) return;
        for (int i = 0; i < _allRenderers.Length; i++)
        {
            var r = _allRenderers[i]; if (r == null) continue;
            try {
                r.enabled = true;
                r.receiveShadows = enable;
                r.shadowCastingMode = enable ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
            } catch {}
        }
        EnsureShadowCaster();
        UpdateShadowCasterSize();
    }

    private void EnsureShadowCaster()
    {
        try
        {
            if (_shadowCasterRenderer != null) return;
            // 创建一个仅投影的 Quad 作为子物体
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ShadowCasterHelper";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, shadowCasterZOffset);
            go.transform.localRotation = Quaternion.identity; // 与卡牌同向
            go.transform.localScale = Vector3.one;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                mr.receiveShadows = false;
                _shadowCasterRenderer = mr;
            }
            var col = go.GetComponent<Collider>(); if (col != null) GameObject.Destroy(col);
            UpdateShadowCasterSize();
        }
        catch {}
    }

    private void UpdateShadowCasterSize()
    {
        if (_shadowCasterRenderer == null) return;
        float sx = 1f, sy = 1f;
        if (box != null)
        {
            // 取局部盒子的最大两个轴作为卡面的宽高（更鲁棒，适配 5:8）
            Vector3 s = box.size;
            // 找到最大和次大
            float a = s.x, b = s.y, c = s.z;
            float max = Mathf.Max(a, Mathf.Max(b, c));
            float mid = (a + b + c) - max - Mathf.Min(a, Mathf.Min(b, c));
            // 以较大为“高”、次大为“宽”，更接近 5:8 的卡面比例
            sy = Mathf.Max(0.01f, max) * shadowCasterScaleMul;
            sx = Mathf.Max(0.01f, mid) * shadowCasterScaleMul;
        }
        else if (MainRenderer != null)
        {
            var b = MainRenderer.bounds; sx = Mathf.Max(0.01f, b.size.x) * shadowCasterScaleMul; sy = Mathf.Max(0.01f, b.size.y) * shadowCasterScaleMul;
        }
        var t = _shadowCasterRenderer.transform;
        t.localScale = new Vector3(sx, sy, 1f);
        t.localPosition = new Vector3(0f, 0f, shadowCasterZOffset);
    }

    public void ApplyHoverColliderExtend(bool enable)
    {
        if (box == null) return;
        if (enable)
        {
            if (!_boxExtended)
            {
                _boxOrigSize = box.size;
                _boxOrigCenter = box.center;
            }
            float ext = Mathf.Max(0f, hoverColliderExtendZ);
            box.size = new Vector3(_boxOrigSize.x, _boxOrigSize.y, _boxOrigSize.z + ext);
            box.center = new Vector3(_boxOrigCenter.x, _boxOrigCenter.y, _boxOrigCenter.z - ext * 0.5f);
            _boxExtended = true;
        }
        else if (_boxExtended)
        {
            box.size = _boxOrigSize;
            box.center = _boxOrigCenter;
            _boxExtended = false;
        }
    }
}
