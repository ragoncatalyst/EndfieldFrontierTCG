using System.Collections;
using UnityEngine;
using TMPro;
using EndfieldFrontierTCG.CA;
using EndfieldFrontierTCG.Board;

// CardView3D (clean rewrite)
// - Leaf-in-water follow: position SmoothDamp + rotation leaning into motion
// - Low-speed wobble with hysteresis
// - Clear lifecycle: Pickup -> Drag -> Release
public class CardView3D : MonoBehaviour
{
    // 手牌静止排列时的真实位置
    private Vector3 _handRestPosition;
    // 拖拽前的真实初始位置
    [Header("Refs")]
    public Rigidbody body;
    private Vector3 _preDragPosition;
    public BoxCollider box;
    // Shared physic material used to make card-card collisions frictionless
    private static PhysicMaterial s_cardNoFrictionMat;
    public Renderer MainRenderer; // 可选：展示卡图
    public TMP_Text NameText;
    public TMP_Text HPText;
    public TMP_Text ATKText;
    public Texture2D MainTextureCache;
    public enum CardCategory { Unknown, Unit, Event }
    public CardCategory Category { get; private set; } = CardCategory.Unknown;
    public string CardType { get; private set; } = string.Empty;
    public bool IsEventCard => Category == CardCategory.Event;
    public bool IsUnitCard => Category == CardCategory.Unit;
    public const float DefaultPlacementHalfThickness = 0.01f;
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
    // 保存最近一次已知的槽位索引（用于被移出手牌时的回插参考）
    [HideInInspector] public int lastKnownSlotIndex = -1;
    [HideInInspector] public int lastKnownHandIndex = -1;
    // ...其余成员...
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


    [Header("Drag Lean Limits")]
    [Tooltip("绕本地 X 轴的最大前后倾角（度）")]
    public float leanMaxPitchDeg = 40f;
    [Tooltip("绕本地 Y 轴的最大左右扭转角（度）")]
    public float leanMaxYawDeg = 20f;
    [Tooltip("绕本地 Z 轴的最大左右倾角（度）")]
    public float leanMaxRollDeg = 40f;
    [Tooltip("速度映射到 Pitch 倾角的增益（度/单位速度）")]
    public float leanPitchGain = 90f;
    [Tooltip("速度映射到 Yaw 扭转的增益（度/单位速度）")]
    public float leanYawGain = 30f;
    [Tooltip("速度映射到 Roll 倾角的增益（度/单位速度）")]
    public float leanRollGain = 90f;
    [Tooltip("鼠标指针移动速度映射到倾斜强度的增益（单位速度 -> 0..1）")]
    public float pointerLeanSpeedGain = 6f;
    [Tooltip("当鼠标静止时倾斜强度衰减的速度（1/秒）")]
    public float pointerLeanRelax = 6f;
    [Tooltip("鼠标局部位置插值过滤频率（1/秒），值越大响应越快")]
    public float pointerLeanFilter = 16f;
    [Header("Wobble Timing")]
    [Tooltip("拖拽速度低于阈值后，需要保持静止多长时间才开始晃动（秒）")]
    public float wobbleStartDelay = 0.25f;
    [Tooltip("拖拽速度重新升高后，多少秒后才允许再次进入晃动（秒）")]
    public float wobbleCooldown = 0.25f;

    [Header("Wobble (Low-speed)")]
    public float wobbleAmpDeg = 3f; // 降低 y 轴等整体摆动感
    public float wobblePeriod = 1.4f;
    public float wobbleEaseIn = 0.15f;
    public float wobbleEaseOut = 0.12f;
    public float speedThreshold = 1.0f;
    public float speedHysteresis = 0.3f;

    [Header("Release Animation")]
    [Tooltip("下落动画的速度曲线（0→1）")] 
    public AnimationCurve dropCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 2f),     // 开始时快速加速
        new Keyframe(0.7f, 0.9f, 0.8f, 0.5f), // 70%时完成90%的移动
        new Keyframe(1f, 1f, 0f, 0f)      // 平滑结束
    );
    [Tooltip("下落动画的持续时间（秒）")] 
    public float dropDuration = 0.25f;

    [Header("Two-Phase Return Animation")]
    [SerializeField]
    [Tooltip("第一阶段的持续时间（秒）")]
    private float _returnPhase1Duration = 0.2f;
    public float returnPhase1Duration
    {
        get => _returnPhase1Duration;
        set
        {
            _returnPhase1Duration = Mathf.Max(0.01f, value);
            if (debugHoverLogs) Debug.Log($"[CardView3D] 更新第一阶段时间: {_returnPhase1Duration}秒");
        }
    }

    [SerializeField]
    [Tooltip("第二阶段的持续时间（秒）")]
    private float _returnPhase2Duration = 0.25f;
    public float returnPhase2Duration
    {
        get => _returnPhase2Duration;
        set
        {
            _returnPhase2Duration = Mathf.Max(0.01f, value);
            if (debugHoverLogs) Debug.Log($"[CardView3D] 更新第二阶段时间: {_returnPhase2Duration}秒");
        }
    }

    [SerializeField]
    [Tooltip("在第二阶段前方的偏移距离（米），避免被相邻卡遮挡")]
    private float _returnFrontBias = 0.15f;
    public float returnFrontBias
    {
        get => _returnFrontBias;
        set
        {
            _returnFrontBias = Mathf.Max(0f, value);
            if (debugHoverLogs) Debug.Log($"[CardView3D] 更新前向偏移: {_returnFrontBias}米");
        }
    }

    [SerializeField]
    [Tooltip("第二阶段期间临时提高排序顺序，避免被相邻卡挡住")]
    private int _returnSortingBoost = 20;
    public int returnSortingBoost
    {
        get => _returnSortingBoost;
        set
        {
            _returnSortingBoost = Mathf.Max(0, value);
            if (debugHoverLogs) Debug.Log($"[CardView3D] 更新排序提升: +{_returnSortingBoost}");
        }
    }

    [Header("Click Filtering")]
    [Tooltip("按下至松开的最大时间（秒），低于该值且移动距离很小则视为点击，不触发回家动画")]
    public float clickMaxDuration = 0.15f;
    [Tooltip("按下至松开的最大位移（米），低于该值且时间很短则视为点击")]
    public float clickMaxDistance = 0.05f;


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
    [Tooltip("单帧碰撞修正的最大位移（米），限制 ComputePenetration 导致的抖动幅度")]
    public float collisionResolveMaxStep = 0.025f;
    private Vector3 _collisionAdjust = Vector3.zero;
    private Vector3 _collisionAdjustVel = Vector3.zero;


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
    private Vector3 _lastPointerWS = Vector3.zero;
    private Vector3 _lastCenterWS = Vector3.zero;
    private bool _hasPointerAnchor = false;
    private float _pointerWorldSpeed = 0f;
    private Vector2 _pointerLocalNorm = Vector2.zero;
    private Vector2 _pointerLocalPlanar = Vector2.zero;
    private Vector2 _pointerLocalPlanarPrev = Vector2.zero;
    private float _pointerAngle = 0f;
    private float _wobbleIdleTimer = 0f;
    private float _wobbleCooldownTimer = 0f;

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
    private int[] _handBaseSortingOrders;
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

    private float _targetY; // Unified target Y coordinate
    private float _baseY; // Base Y coordinate for each card
    private static float s_nextY = 0.3f; // Static Y tracker for spacing between cards (start at 0.3 per request)
    private const float Y_SPACING = 0.02f; // Spacing between cards
    private float _hoverY; // Explicit target Y coordinate during hover

    private float _handRestY; // Y coordinate for hand rest position
    private float _dragY; // Y coordinate during dragging
    private const float MIN_HAND_Y = 0.28f; // Enforce no y-values smaller than this
    // Simplified mode: when true, only run the core Y/drag/return behaviour requested.
    // Set to false to restore full behaviour.
    public bool simplifyMode = true;

    private void Awake()
    {
        // 强制初始化，防止Inspector和Prefab污染
        // 不再使用字段，相关数值直接硬编码到用到的地方

        _cam = Camera.main != null ? Camera.main : Camera.current;
        if (body == null) body = GetComponent<Rigidbody>();
        if (box == null) box = GetComponent<BoxCollider>();
        if (box == null) box = GetComponentInChildren<BoxCollider>(true);
        // Ensure card colliders use a shared frictionless physic material to avoid
        // friction-caused sticking/penetration artifacts between cards.
        try
        {
            if (s_cardNoFrictionMat == null)
            {
                s_cardNoFrictionMat = new PhysicMaterial("Card_NoFriction")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    frictionCombine = PhysicMaterialCombine.Minimum,
                    bounciness = 0f,
                    bounceCombine = PhysicMaterialCombine.Minimum
                };
            }
            // Assign to primary box collider
            if (box != null) box.material = s_cardNoFrictionMat;
            // Also assign to any other colliders under the card (children)
            var cols = GetComponentsInChildren<Collider>(true);
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i]; if (c == null) continue;
                    // keep triggers as-is but they can still carry material without harm
                    c.material = s_cardNoFrictionMat;
                }
            }

            // If the primary box collider is a trigger (used by other systems), create or
            // reuse a child non-trigger collider for physics interactions so cards can
            // still participate in frictionless collisions without changing the trigger.
            try
            {
                if (box != null && box.isTrigger)
                {
                    BoxCollider phys = null;
                    var t = transform.Find("PhysicsCollider");
                    if (t != null) phys = t.GetComponent<BoxCollider>();
                    if (phys == null)
                    {
                        var go = new GameObject("PhysicsCollider");
                        go.transform.SetParent(transform, false);
                        phys = go.AddComponent<BoxCollider>();
                    }
                    phys.size = box.size;
                    phys.center = box.center;
                    phys.isTrigger = false;
                    phys.material = s_cardNoFrictionMat;
                    phys.gameObject.layer = gameObject.layer;
                }
            }
            catch { }
        }
        catch { }
        createId = s_nextCreateId++;
        _baseRot = transform.rotation; // 使用当前旋转作为基准
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
            // Do not force box.isTrigger here; some systems rely on the collider being a trigger.
            // If a physics (non-trigger) collider is required, a separate child collider will be created.
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

    private void Start()
    {
        // Initialize the base Y coordinate for this card and increment for the next card
        _baseY = s_nextY;
        _handRestY = _baseY; // Set hand rest Y to base Y
        _dragY = _baseY + 0.1f; // Add offset for dragging height
        s_nextY += Y_SPACING;
    }

    // Return the canonical final Y that represents this card's resting/stacked height.
    private float GetFinalHandY()
    {
        // final y comes from per-card base plus any target offset
        return _baseY + _targetY;
    }

    private void RecordHoverY()
    {
        // Record the current Y as the hover target
        _hoverY = transform.position.y;
    }

    public void SetTargetY(float offsetY)
    {
        // Set the target Y coordinate
        _targetY = offsetY;
    }

    private void UpdateYCoordinate(float smoothTime = 0.06f)
    {
        // Adjust the y-coordinate based on the current state. Final resting Y is computed first.
        Vector3 position = transform.position;
        float finalY = GetFinalHandY();
        float targetY = IsDragging ? _dragY : finalY; // during drag use dragY; otherwise move toward final resting Y
        position.y = Mathf.Lerp(position.y, targetY, Time.deltaTime / smoothTime);
        transform.position = position;
    }

    private void Update()
    {
        if (simplifyMode)
        {
            // Minimal input handling: allow external bridge and fallback mouse drag to work
            if (!suppressOnMouseHandlers)
            {
                if (_cam == null) _cam = Camera.main != null ? Camera.main : Camera.current;
                if (_cam != null)
                {
                    if (Input.GetMouseButtonDown(0)) { _usingFallbackInput = true; OnMouseDown(); }
                    if (_usingFallbackInput)
                    {
                        if (Input.GetMouseButton(0)) { OnMouseDrag(); }
                        if (Input.GetMouseButtonUp(0)) { _usingFallbackInput = false; OnMouseUp(); }
                    }
                }
            }

            // If dragging, perform a single collision correction pass (avoid changing targetY here)
            if (_state == DragState.Dragging)
            {
                bool blocked;
                Vector3 corr = AdjustTargetForCollisions(transform.position, transform.position, out blocked);
                transform.position = corr;
            }

            // Update Y toward canonical final or drag height
            UpdateYCoordinate();

            // Enforce minimum Y
            Vector3 __tmpP = transform.position;
            if (__tmpP.y < MIN_HAND_Y) { __tmpP.y = MIN_HAND_Y; transform.position = __tmpP; }

            return;
        }
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
            transform.position = corr;

            if (elevateWhenBlocked)
            {
                float planeY = 0.07f;
                SetTargetY(blocked ? elevateMax : 0f);
            }
        }

        // Apply unified Y coordinate adjustment
        UpdateYCoordinate();

        // Enforce minimum Y across all states to avoid tiny 0.0x values
        Vector3 __p = transform.position;
        if (__p.y < MIN_HAND_Y) { __p.y = MIN_HAND_Y; transform.position = __p; }
    }

    public void SetHomePose(Vector3 pos, Quaternion rot)
    {
        // 记录手牌静止排列时的真实位置
        _handRestPosition = pos;
        // Ensure stored home position uses the canonical final Y
        _handRestPosition.y = GetFinalHandY();
        // 清除父级引用
        _homeParent = null;
        // 直接使用世界空间坐标和旋转
        _homePos = _handRestPosition;
        // 强制零 Z 轴旋转，确保手牌与归位过程中 Z 始终为 0
        Vector3 rEuler = rot.eulerAngles;
        Quaternion rotZ0 = Quaternion.Euler(rEuler.x, rEuler.y, 0f);
        _homeRot = rotZ0;
        // 本地空间坐标和旋转与世界空间相同
        _homeLocalPos = pos;
        _homeLocalRot = rotZ0;
        _homeSet = true;
        if (debugHoverLogs)
            Debug.Log($"[CardView3D] 设置家位置（无父级） - 世界坐标: {pos}");
        // 将基准旋转的 Z 轴清零，保证手牌内 tilt/lean 计算以 Z=0 为中心
        _baseRot = Quaternion.Euler(_baseRot.eulerAngles.x, _baseRot.eulerAngles.y, 0f);
        Debug.Log($"[CardView3D] SetHomePose called - pos: {pos}, rot: {rot.eulerAngles}");
    }

    public void SetHomeFromZone(Transform zone, Vector3 worldPos, Quaternion worldRot)
    {
        // 记录父级引用
        _homeParent = zone;

        // 计算本地空间坐标和旋转
        if (zone != null)
        {
            _homeLocalPos = zone.InverseTransformPoint(worldPos);
            _homeLocalRot = Quaternion.Inverse(zone.rotation) * worldRot;
        }
        else
        {
            _homeLocalPos = worldPos;
            _homeLocalRot = worldRot;
        }

    // 记录世界空间坐标和旋转（用于快速访问）
    // Adjust worldPos Y to canonical final Y so zone-provided Y doesn't override stacking
    Vector3 adjustedWorldPos = new Vector3(worldPos.x, GetFinalHandY(), worldPos.z);
    _homePos = adjustedWorldPos;
    // 强制 Z 轴为零，保证手牌与归位过程中 Z 始终为 0
    Vector3 wr = worldRot.eulerAngles;

        // 标记家位置已设置
        _homeSet = true;

        if (debugHoverLogs)
        {
            Debug.Log($"[CardView3D] 设置家位置 - 世界坐标: {worldPos}, 本地坐标: {_homeLocalPos}, 父级: {(zone != null ? zone.name : "无")}");
        }
        // 同步基准旋转，确保手牌中的旋转基准 Z 分量为 0
        _baseRot = Quaternion.Euler(_baseRot.eulerAngles.x, _baseRot.eulerAngles.y, 0f);
    }

    public void GetPlacementExtents(Quaternion targetRotation, out float minY, out float maxY)
    {
        if (box != null)
        {
            Vector3 lossy = transform.lossyScale;
            Vector3 scaledCenter = new Vector3(box.center.x * lossy.x, box.center.y * lossy.y, box.center.z * lossy.z);
            Vector3 scaledHalf = new Vector3(box.size.x * 0.5f * lossy.x, box.size.y * 0.5f * lossy.y, box.size.z * 0.5f * lossy.z);

            minY = float.PositiveInfinity;
            maxY = float.NegativeInfinity;

            for (int ix = -1; ix <= 1; ix += 2)
            {
                for (int iy = -1; iy <= 1; iy += 2)
                {
                    for (int iz = -1; iz <= 1; iz += 2)
                    {
                        Vector3 cornerLocal = scaledCenter + new Vector3(scaledHalf.x * ix, scaledHalf.y * iy, scaledHalf.z * iz);
                        float y = (targetRotation * cornerLocal).y;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (float.IsInfinity(minY) || float.IsInfinity(maxY))
            {
                minY = -DefaultPlacementHalfThickness;
                maxY = DefaultPlacementHalfThickness;
            }
            return;
        }

        minY = -DefaultPlacementHalfThickness;
        maxY = DefaultPlacementHalfThickness;
    }

    public void AlignToSlotSurface(CardSlotBehaviour slot)
    {
        if (slot == null) return;
        if (!TryGetWorldBounds(out Bounds bounds)) return;

        float surfaceY = slot.GetSurfaceWorldY();
        SetTargetY(surfaceY - _baseY);

        if (_homeSet)
        {
            SetHomePose(transform.position, transform.rotation);
        }
    }

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        if (box != null)
        {
            bounds = box.bounds;
            return true;
        }

        var rends = GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        bounds = new Bounds();
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null) continue;

            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return hasBounds;
    }

    private void GetHomeWorldPose(out Vector3 pos, out Quaternion rot)
    {
        if (_homeParent != null && _homeParent.gameObject.activeInHierarchy)
        {
            // 如果有父级且父级处于激活状态，计算世界空间坐标和旋转
            pos = _homeParent.TransformPoint(_homeLocalPos);
            rot = _homeParent.rotation * _homeLocalRot;

            // 更新缓存的世界空间坐标和旋转
            _homePos = pos;
            _homeRot = rot;

            if (debugHoverLogs)
            {
                Debug.Log($"[CardView3D] 获取家位置 - 从父级 {_homeParent.name} 计算世界坐标: {pos}");
            }
        }
        else
        {
            // 如果没有父级或父级未激活，使用缓存的世界空间坐标和旋转
            pos = _homePos;
            rot = _homeRot;

            if (debugHoverLogs && _homeParent != null)
            {
                Debug.Log($"[CardView3D] 获取家位置 - 父级 {_homeParent.name} 未激活，使用缓存坐标: {pos}");
            }
        }
    }

    // Public accessor used by HandSplineZone via reflection to obtain the
    // canonical home/world pose for this card (position + rotation).
    // Returns a ValueTuple so invoking code can directly deconstruct it.
    public (Vector3, Quaternion) GetHomePose()
    {
        GetHomeWorldPose(out Vector3 pos, out Quaternion rot);
        return (pos, rot);
    }

    public void SnapTo(Vector3 pos, Quaternion rot)
    {
        Debug.Log($"[CardView3D] SnapTo called - 初始位置: {transform.position}, 目标位置: {pos}, 目标旋转: {rot.eulerAngles}");

        // 调试：打印传入的 pos 值
        Debug.Log($"SnapTo called with pos: {pos}");

        // 记录手牌静止排列时的真实位置 (use canonical final Y)
        float finalY = GetFinalHandY();
        pos.y = finalY;
        _handRestPosition = pos;

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
        // Use the fuller implementation which handles physics restore and final state cleanup.
        _returnHomeCo = StartCoroutine(ReturnToHomeTwoPhase_Old(aheadZ, phase1Time, phase2Time));
    }

    // Unified return entry point: always attempt to use parent HandSplineZone (if any)
    // so that the deferred-insert two-phase flow is used. Falls back to local two-phase
    // return or drop if no home is available.
    public void ReturnHomeUnified()
    {
        // Prefer zone-managed return which handles gap reservation and centralized ordering
        var zone = GetComponentInParent<EndfieldFrontierTCG.Hand.HandSplineZone>();
        try
        {
            if (zone != null && zone.TryReturnCardToHome(this))
            {
                return;
            }
        }
        catch {}

        // Otherwise fall back to local two-phase return if we have a home pose
        if (_homeSet)
        {
            BeginSmoothReturnToHome(returnFrontBias, returnPhase1Duration, returnPhase2Duration);
            return;
        }

        // No home: play drop animation as a last resort
        StartCoroutine(ReleaseDrop());
    }

    // 记录拖拽前的Y值（非交互状态时的Y）
    private float _preDragY;
    // External control: when set, coroutines will refresh their returnTarget.y to match GetFinalHandY()
    private bool _forceReturnYNow = false;

    // 外部调用：强制使正在进行的二段归位的目标 Y 立即同步为当前的最终 Y
    public void ForceReturnYNow()
    {
        _forceReturnYNow = true;
    }

    private IEnumerator ReturnToHomeTwoPhase(float aheadZ, float t1, float t2)
    {
        Debug.Log($"[CardView3D] 开始归位 - 当前世界位置: {transform.position}, 目标位置: {_handRestPosition}, aheadZ: {aheadZ}, 阶段1时间: {t1}, 阶段2时间: {t2}");

        Vector3 returnTarget = _handRestPosition;
        // Temporarily ignore transient _targetY (e.g., elevation) so return targets the per-card base
        float savedTargetY = _targetY;
        SetTargetY(0f);
        float finalY = GetFinalHandY();

        // Phase 1: Smoothly move to the temporary position
        float t = 0f;
        while (t < t1)
        {
            // if external caller requested finalY sync, refresh it and the stored home pos
            if (_forceReturnYNow)
            {
                finalY = GetFinalHandY();
                returnTarget.y = finalY;
                _handRestPosition.y = finalY;
                _homePos.y = finalY;
                _forceReturnYNow = false;
            }
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / t1);
            transform.position = new Vector3(
                Mathf.Lerp(transform.position.x, returnTarget.x, progress),
                finalY, // Maintain final Y during phase 1
                Mathf.Lerp(transform.position.z, returnTarget.z + aheadZ, progress)
            );
            yield return null;
        }

        // Phase 2: Smoothly move to the final position
        t = 0f;
        while (t < t2)
        {
            // allow external caller to force finalY during phase2 as well
            if (_forceReturnYNow)
            {
                finalY = GetFinalHandY();
                returnTarget.y = finalY;
                _handRestPosition.y = finalY;
                _homePos.y = finalY;
                _forceReturnYNow = false;
            }
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / t2);
            transform.position = new Vector3(
                Mathf.Lerp(transform.position.x, returnTarget.x, progress),
                finalY, // Maintain final Y during phase 2
                Mathf.Lerp(transform.position.z, returnTarget.z, progress)
            );
            yield return null;
        }

        // Ensure the final position uses the canonical final Y
        transform.position = new Vector3(returnTarget.x, finalY, returnTarget.z);
        // restore previous target offset
        SetTargetY(savedTargetY);
    }

    private IEnumerator ReturnToHomeTwoPhase_Old(float aheadZ, float t1, float t2)
    {
        Debug.Log($"[CardView3D] 开始归位 - 当前世界位置: {transform.position}, 目标位置: {_handRestPosition}, aheadZ: {aheadZ}, 阶段1时间: {t1}, 阶段2时间: {t2}");

        Vector3 returnTarget = _handRestPosition;
        // Temporarily ignore transient _targetY (e.g., elevation) so return targets the per-card base
        float savedTargetY = _targetY;
        SetTargetY(0f);
        float finalY = GetFinalHandY();

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
    // 确保开始时 Z 轴为 0（避免首帧出现 Z 轴旋转抖动）
    Quaternion startRot = transform.rotation;
    Vector3 sEuler = startRot.eulerAngles;
    startRot = Quaternion.Euler(sEuler.x, sEuler.y, 0f);
    // 立即应用以确保整个归位过程不含 Z 轴旋转
    transform.rotation = startRot;
    // 强制在第一阶段立即把卡牌摆正为 (90, 0, 0)（以避免在 phase1 结束前因角度导致穿插）
    Quaternion earlyFlatRot = Quaternion.Euler(90f, 0f, 0f);
    transform.rotation = earlyFlatRot;

    // temp point just "above" returnTarget 沿Z轴偏移
    Vector3 tempXZ = new Vector3(returnTarget.x, finalY, returnTarget.z + aheadZ);

    // (no render-order boost here; HandSplineZone controls ordering to avoid phase2 occlusion)

    // Phase 1: 在 XZ 平面移动到临时点，同时平滑将 Y 插值到 homeY（以确保在 phase2 开始前 Y 已到位）
    float t = 0f;
    float dur1 = Mathf.Max(0.0001f, returnPhase1Duration);
    Vector2 startXZ = new Vector2(startPos.x, startPos.z);
    Vector2 tempXZ2 = new Vector2(tempXZ.x, tempXZ.z);
    float startY = transform.position.y; // start from current Y, interpolate toward finalY
    Vector2 velocity = Vector2.zero;

        if (debugHoverLogs)
        {
            Debug.Log($"[CardView3D] 开始第一阶段返回 - 从: {startPos}, 到前方点: {tempXZ}, 持续: {dur1}秒");
        }

            while (t < dur1)
        {
            // allow external caller to force finalY while running
            if (_forceReturnYNow)
            {
                finalY = GetFinalHandY();
                returnTarget.y = finalY;
                _handRestPosition.y = finalY;
                _homePos.y = finalY;
                _forceReturnYNow = false;
            }
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur1);

            // 获取当前速度系数（0-1范围）
            var handZone = GetComponentInParent<EndfieldFrontierTCG.Hand.HandSplineZone>();
            float speedFactor = handZone != null ? handZone.returnPhase1Curve.Evaluate(a) : a;

            // 计算当前位置到目标的方向和距离
            Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
            Vector2 toTarget = tempXZ2 - currentXZ;
            float distanceToTarget = toTarget.magnitude;
            
            // 使用速度系数直接控制移动（XZ）并平滑插值 Y
            float baseSpeed = 5f; // 基础速度
            float maxSpeed = baseSpeed * (1f + distanceToTarget); // 距离越远速度越快
            float targetSpeed = maxSpeed * speedFactor; // 应用速度曲线

            // 平滑过渡到目标速度（XZ）
            velocity = Vector2.Lerp(velocity, toTarget.normalized * targetSpeed, Time.deltaTime * 10f);
            Vector2 newXZ = currentXZ + velocity * Time.deltaTime;

            // phase1: XZ插值到tempXZ，Y插值到returnTarget.y
            float newX = Mathf.Lerp(startPos.x, tempXZ.x, speedFactor);
            float newZ = Mathf.Lerp(startPos.z, tempXZ.z, speedFactor);
            float newY = Mathf.Lerp(startY, finalY, speedFactor);
            transform.position = new Vector3(newX, newY, newZ);
            transform.rotation = earlyFlatRot;

            if (debugHoverLogs && t % 0.1f < Time.deltaTime)
            {
                Debug.Log($"[CardView3D] 第一阶段进度 - {(a * 100):F0}%, 位置: {newXZ}");
            }

            yield return null;
        }

    // Ensure position Y is at final value before phase2
    transform.position = new Vector3(returnTarget.x, finalY, returnTarget.z);
    yield return null;

    // Phase 2: 从临时点平滑移动到最终位置
    Vector3 startP2 = transform.position;
    Quaternion startR2 = transform.rotation;
    float dur2 = Mathf.Max(0.01f, returnPhase2Duration);
    float tP2 = 0f;
    // phase2: 只插值Z到returnTarget.z，X/Y保持不变
    while (tP2 < dur2)
    {
        tP2 += Time.deltaTime;
        float a = Mathf.Clamp01(tP2 / dur2);
        var handZone = GetComponentInParent<EndfieldFrontierTCG.Hand.HandSplineZone>();
        float speedFactor = handZone != null ? handZone.returnPhase2Curve.Evaluate(a) : a;
            float newZ = Mathf.LerpUnclamped(startP2.z, returnTarget.z, speedFactor);
            transform.position = new Vector3(returnTarget.x, finalY, newZ);
        transform.rotation = Quaternion.Slerp(startR2, _homeRot, a);
        if (debugHoverLogs && tP2 % 0.1f < Time.deltaTime)
        {
            Debug.Log($"[CardView3D] 第二阶段进度 - {(a * 100):F0}%, 位置: {transform.position}");
        }
        yield return null;
    }
    // 归位动画结束，精确还原到拖拽前的真实初始位置 (use finalY)
    transform.position = new Vector3(returnTarget.x, finalY, returnTarget.z);
    // restore previous target offset
    SetTargetY(savedTargetY);

        // render order is managed by HandSplineZone; do not override here to avoid
        // inconsistent occlusion during phase2.

        // 让一帧过去再退出，保证排序恢复不会与最后一帧位置写入产生竞争
        yield return null;

        // 恢复物理状态
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

        // 清理状态
        _returnHomeCo = null;
        IsReturningHome = false;
        _state = DragState.Idle;
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
        CardType = string.IsNullOrEmpty(data.CA_Type) ? string.Empty : data.CA_Type.Trim();
        Category = ParseCategory(CardType);
        gameObject.layer = LayerMask.NameToLayer("Default");
    if (box != null) { box.enabled = true; /* keep isTrigger as configured by other systems */ }
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
                    var mat = MainRenderer.material;
                    var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (unlitShader != null && mat.shader != null && mat.shader.name != "Universal Render Pipeline/Unlit")
                    {
                        mat.shader = unlitShader;
                    }
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                    else mat.mainTexture = tex;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
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

    public void PlayEventSequence(EventPlayZone zone)
    {
        StartCoroutine(PlayEventCard(zone, null));
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
        // 记录拖拽前的完整位置
        _preDragPosition = transform.position;
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
    // Preserve box.isTrigger state; do not force non-trigger here as other systems may require it

        if (IsEventCard)
        {
            transform.rotation = AlignToTableRotation(transform.rotation);
        }
        // 重新记录当前旋转作为拖拽基准
        _baseRot = transform.rotation;

        // 推迟到真正进入拖拽平面后再计算鼠标-物体偏移，避免因手牌父级旋转/升降导致的初始错位
        _dragOffsetInitialized = false;
        _dragOffsetWS = Vector3.zero;
        _lastTarget = transform.position;
        _dragFirstFrame = true;
        _mouseDownTime = Time.unscaledTime;
        _mouseDownPos = transform.position;
        if (debugHoverLogs) Debug.Log($"[CardView3D] DragStart idx={handIndex} pos={transform.position}");
        
        // 确保所有渲染器都是启用的
        if (_allRenderers != null)
        {
            foreach (var renderer in _allRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                    // Do not change sortingOrder here; hand render order is centralized in HandSplineZone.
                }
            }
        }
        
        StartCoroutine(PickupLift());
        BoostDragRendering(true);
    }

    private IEnumerator PickupLift()
    {
        float t = 0f, dur = 0.12f;
        Vector3 start = transform.position;
        Vector3 end = new Vector3(start.x, 0.07f, start.z) + CameraForwardPlanar() * dragFrontBias;
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
        float planeY = 0.07f;
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

            Vector3 centerWSCurrent = transform.TransformPoint(box != null ? box.center : Vector3.zero);
            UpdateDragPointerAnchor(centerWSCurrent, pt);

            ApplyFollowLean(_dragVel);
            // 首次确定摇晃方向：用 box 中心到当前命中点的 r，与平面速度 F 的叉积的 y 符号
            if (!_wobbleDirSet && box != null)
            {
                Vector3 centerWS = centerWSCurrent;
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

    private void UpdateDragPointerAnchor(Vector3 centerWS, Vector3 fallbackPoint)
    {
        Vector3 pointerWS = fallbackPoint;
        Camera cam = _cam != null ? _cam : (Camera.main != null ? Camera.main : Camera.current);
        if (cam != null)
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, interactLayerMask, QueryTriggerInteraction.Collide))
            {
                var cv = hit.collider != null ? hit.collider.GetComponentInParent<CardView3D>() : null;
                if (cv == this)
                {
                    pointerWS = hit.point;
                }
            }
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float rawSpeed = _hasPointerAnchor ? Vector3.Distance(pointerWS, _lastPointerWS) / dt : 0f;
        _pointerWorldSpeed = Mathf.Lerp(_pointerWorldSpeed, rawSpeed, Mathf.Clamp01(pointerLeanFilter * dt));

        _lastCenterWS = centerWS;
        _lastPointerWS = pointerWS;
        _hasPointerAnchor = true;
    }

    private Vector3 CameraForwardPlanar()
    {
        if (_cam == null) _cam = Camera.main != null ? Camera.main : Camera.current;
        Vector3 f = _cam != null ? _cam.transform.forward : Vector3.forward;
        f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
        f.Normalize();
        return f;
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
                // 确保渲染器启用
                r.enabled = true;
                
                // 更新排序顺序
                if (enable)
                {
                    // 保存原始排序顺序
                    if (_origSortingOrders == null || _origSortingOrders.Length != _allRenderers.Length)
                    {
                        _origSortingOrders = new int[_allRenderers.Length];
                    }
                    _origSortingOrders[i] = r.sortingOrder;
                    // Do not modify sortingOrder here; HandSplineZone will assign render orders centrally.
                }
                else
                {
                    // 恢复原始排序顺序
                    if (_origSortingOrders != null && i < _origSortingOrders.Length)
                    {
                        r.sortingOrder = _origSortingOrders[i];
                    }
                }
                
                // 确保阴影设置正确
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            } catch {}
        }
    }

    // 软碰撞：用 Physics.ComputePenetration 对目标点做位移修正，避免与其它卡片/桌面穿模
    private Vector3 AdjustTargetForCollisions(Vector3 current, Vector3 target, out bool blocked)
    {
        blocked = false;
        if (box == null) return target;
        Vector3 corrected = target;
        float maxStep = Mathf.Max(0.001f, collisionResolveMaxStep);
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
                    float step = Mathf.Min(dist, maxStep * 2);
                    corrected += prefer.normalized * step;
                    any = true;
                    blocked = true;

                    // 调试：打印分离方向和距离
                    Debug.Log($"[AdjustTargetForCollisions] 分离方向: {dir}, 距离: {dist}, 当前Y: {corrected.y}");
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

            var currentHandZone = GetComponentInParent<EndfieldFrontierTCG.Hand.HandSplineZone>();
            var eventZoneCandidate = EventPlayZone.FindZoneForCard(this);

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 1000f, interactLayerMask, QueryTriggerInteraction.Collide);

            if (IsEventCard)
            {
                EventPlayZone targetZone = eventZoneCandidate;
                float nearestZoneDistance = float.MaxValue;
                if (targetZone != null)
                {
                    nearestZoneDistance = Vector3.Distance(transform.position, targetZone.transform.position);
                }

                foreach (var hit in hits)
                {
                    var zone = hit.collider.GetComponent<EventPlayZone>();
                    if (zone == null) zone = hit.collider.GetComponentInParent<EventPlayZone>();
                    if (zone == null) continue;
                    float dist = Vector3.Distance(transform.position, zone.transform.position);
                    if (dist < nearestZoneDistance)
                    {
                        nearestZoneDistance = dist;
                        targetZone = zone;
                    }
                }

                if (targetZone != null)
                {
                    currentHandZone?.ClearInputState();
                    StartCoroutine(PlayEventCard(targetZone, currentHandZone));
                    return;
                }

                if (currentHandZone != null && currentHandZone.TryReturnCardToHome(this))
                {
                    return;
                }

                StartCoroutine(ReleaseDrop());
                return;
            }

            // 找到最近的可用槽位
            CardSlotBehaviour nearestSlot = null;
            float nearestDistance = float.MaxValue;

            foreach (var hit in hits)
            {
                var slot = hit.collider.GetComponent<CardSlotBehaviour>();
                if (slot == null) continue;
                float distance = Vector3.Distance(transform.position, slot.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestSlot = slot;
                }
            }

            if (nearestSlot != null)
            {
                Debug.Log($"[CardView3D] 找到最近的槽位: {nearestSlot.name}, 距离: {nearestDistance}");
                currentHandZone?.ClearInputState();
                StartCoroutine(SmoothMoveToSlot(nearestSlot, currentHandZone));
                return;
            }

            if (currentHandZone != null && currentHandZone.TryReturnCardToHome(this))
            {
                return;
            }

            // 如果不在手牌区域，直接执行掉落动画
            StartCoroutine(ReleaseDrop());
        }

        private IEnumerator SmoothMoveToSlot(CardSlotBehaviour slot, EndfieldFrontierTCG.Hand.HandSplineZone originZone)
        {
            if (slot == null) yield break;

            if (!slot.CanAcceptCard(this))
            {
                Debug.LogWarning($"[CardView3D] 槽位 {slot.name} 无法接受卡牌");
                // Use unified return behavior so all failed placements use the same two-phase return
                ReturnHomeUnified();
                yield break;
            }

            // 记录初始状态，便于失败时恢复
            Transform originalParent = transform.parent;
            int originalLayer = gameObject.layer;
            DragState originalState = _state;

            Transform savedHomeParent = _homeParent;
            Vector3 savedHomeLocalPos = _homeLocalPos;
            Quaternion savedHomeLocalRot = _homeLocalRot;
            Vector3 savedHomePos = _homePos;
            Quaternion savedHomeRot = _homeRot;
            bool savedHomeSet = _homeSet;

            int savedHandIndex = handIndex;
            int savedSlotIndex = slotIndex;

            bool placed = false;
            IsReturningHome = true;

            Rigidbody rb = body;
            bool hasBody = rb != null;
            bool prevDetect = false, prevGravity = false, prevKinematic = false;
            RigidbodyConstraints prevConstraints = RigidbodyConstraints.FreezeAll;

            if (hasBody)
            {
                prevDetect = rb.detectCollisions;
                prevGravity = rb.useGravity;
                prevKinematic = rb.isKinematic;
                prevConstraints = rb.constraints;
                rb.detectCollisions = false;
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.constraints = RigidbodyConstraints.FreezeAll;
            }

            try
            {
                _state = DragState.Releasing;

                Vector3 startPos = transform.position;
                Quaternion startRot = transform.rotation;
                slot.GetPlacementForCard(this, out Vector3 targetPos, out Quaternion targetRot);

                // 在槽位局部空间内进行插值动画
                Vector3 releaseWorldPos = startPos;
                Quaternion releaseWorldRot = startRot;
                // Unregister from origin hand zone early so remaining cards can re-layout while
                // this card animates toward the slot.
                if (originZone != null)
                {
                    try { originZone.UnregisterCard(this); } catch { }
                }
                transform.SetParent(slot.transform, true);
                Vector3 startLocalPos = slot.transform.InverseTransformPoint(releaseWorldPos);
                Vector3 targetLocalPos = slot.transform.InverseTransformPoint(targetPos);

                float duration = Mathf.Max(0.0001f, dropDuration);
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float normalized = Mathf.Clamp01(elapsed / duration);
                    float curveT = dropCurve != null ? dropCurve.Evaluate(normalized) : normalized;
                    transform.localPosition = Vector3.LerpUnclamped(startLocalPos, targetLocalPos, curveT);
                    transform.rotation = Quaternion.Slerp(releaseWorldRot, targetRot, curveT);
                    yield return null;
                }

                transform.localPosition = targetLocalPos;
                transform.rotation = targetRot;
                transform.position = targetPos;

                if (slot.TryPlaceCard(this))
                {
                    placed = true;
                    Debug.Log($"[CardView3D] 成功放置到槽位: {slot.name}");
                    handIndex = -1;
                    slotIndex = -1;
                    _homeSet = false;

                    transform.SetParent(slot.transform, true);

                    if (rb != null)
                    {
                        rb.isKinematic = true;
                        rb.useGravity = false;
                        rb.detectCollisions = false;
                        rb.constraints = RigidbodyConstraints.FreezeAll;
                    }

                    SetHomePose(transform.position, transform.rotation);
                    _state = DragState.Idle;
                    yield break;
                }

                Debug.LogError($"[CardView3D] 放置失败: {slot.name}");

                // 失败：恢复原始父级/状态
                transform.SetParent(originalParent, true);
                transform.position = startPos;
                transform.rotation = startRot;
                gameObject.layer = originalLayer;
                _state = originalState;

                if (savedHomeSet)
                {
                    if (savedHomeParent != null)
                    {
                        Vector3 restoredPos = savedHomeParent.TransformPoint(savedHomeLocalPos);
                        Quaternion restoredRot = savedHomeParent.rotation * savedHomeLocalRot;
                        SetHomeFromZone(savedHomeParent, restoredPos, restoredRot);
                    }
                    else
                    {
                        SetHomePose(savedHomePos, savedHomeRot);
                    }
                }

                handIndex = savedHandIndex;
                slotIndex = savedSlotIndex;

                // Use unified return flow for failed placement
                ReturnHomeUnified();
                yield break;
            }
            finally
            {
                if (!placed && hasBody)
                {
                    rb.detectCollisions = prevDetect;
                    rb.useGravity = prevGravity;
                    rb.isKinematic = prevKinematic;
                    rb.constraints = prevConstraints;
                }

                IsReturningHome = false;
            }
        }

        private IEnumerator PlayEventCard(EventPlayZone zone, EndfieldFrontierTCG.Hand.HandSplineZone originZone)
        {
            if (zone == null)
            {
                yield break;
            }

            Transform originalParent = transform.parent;
            int originalLayer = gameObject.layer;

            // 把卡牌临时从手牌层级中移除，避免 HandSplineZone 继续管控
            transform.SetParent(null, true);

            originZone?.UnregisterCard(this);

            Transform savedHomeParent = _homeParent;
            Vector3 savedHomeLocalPos = _homeLocalPos;
            Quaternion savedHomeLocalRot = _homeLocalRot;
            Vector3 savedHomePos = _homePos;
            Quaternion savedHomeRot = _homeRot;
            bool savedHomeSet = _homeSet;

            handIndex = -1;
            slotIndex = -1;
            _homeSet = false;

            IsReturningHome = true;

            Rigidbody rb = body;
            bool hasBody = rb != null;
            bool prevDetect = false, prevGravity = false, prevKinematic = false;
            RigidbodyConstraints prevConstraints = RigidbodyConstraints.None;

            if (hasBody)
            {
                prevDetect = rb.detectCollisions;
                prevGravity = rb.useGravity;
                prevKinematic = rb.isKinematic;
                prevConstraints = rb.constraints;
                rb.detectCollisions = false;
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.constraints = RigidbodyConstraints.FreezeAll;
            }

            if (box != null) box.enabled = false;

            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

            zone.GetPlacementForCard(this, out Vector3 landingPos, out Quaternion landingRot, out Vector3 exitPos);
            landingRot = AlignToTableRotation(landingRot);

            const float flipStartProgress = 0.2f;
            const float flipCompletionProgress = 0.7f;

            Vector3 startPos = transform.position;
            transform.rotation = AlignToTableRotation(transform.rotation);

            Vector3 displayPos = landingPos;
            Quaternion displayRot = landingRot;
            if (zone.TryGetDisplayPose(out Vector3 dispPos, out Quaternion dispRot))
            {
                displayPos = dispPos;
                displayRot = AlignToTableRotation(dispRot);
            }

            Quaternion flipStartRot = displayRot;

            Vector3 travelStartPos = startPos;
            Quaternion travelStartRot = flipStartRot;
            transform.rotation = flipStartRot;
            float pathLength = Vector3.Distance(displayPos, travelStartPos);
            displayPos = new Vector3(displayPos.x, startPos.y, displayPos.z);

            float displayMoveDur = Mathf.Max(0.01f, zone.displayMoveDuration);
            AnimationCurve displayCurve = zone.displayMoveCurve != null ? zone.displayMoveCurve : AnimationCurve.Linear(0f, 0f, 1f, 1f);

            float t = 0f;
            while (t < displayMoveDur)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / displayMoveDur);
                float w = displayCurve.Evaluate(a);
                transform.position = Vector3.LerpUnclamped(travelStartPos, displayPos, w);
                float travelled = Vector3.Distance(transform.position, travelStartPos);
                float progress = pathLength > 1e-4f ? Mathf.Clamp01(travelled / pathLength) : Mathf.Clamp01(w);
                float flipDenom = Mathf.Max(0.01f, flipCompletionProgress - flipStartProgress);
                float flipFactor = 0f;
                if (progress > flipStartProgress)
                {
                    flipFactor = Mathf.Clamp01((progress - flipStartProgress) / flipDenom);
                }
                transform.rotation = Quaternion.Slerp(travelStartRot, displayRot, flipFactor);
                yield return null;
            }
            transform.position = displayPos;
            transform.rotation = displayRot;

            float displayHold = Mathf.Max(0f, zone.displayHoldDuration);
            if (displayHold > 0f) yield return new WaitForSeconds(displayHold);

            exitPos = zone.GetExitPosition(displayPos);
            exitPos = new Vector3(exitPos.x, startPos.y, exitPos.z);

            float exitDur = Mathf.Max(0.01f, zone.exitDuration);
            AnimationCurve exitCurve = zone.exitCurve != null ? zone.exitCurve : AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Vector3 exitStart = displayPos;
            Quaternion exitRot = AlignToTableRotation(displayRot);

            t = 0f;
            while (t < exitDur)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / exitDur);
                float w = exitCurve.Evaluate(a);
                transform.position = Vector3.LerpUnclamped(exitStart, exitPos, w);
                transform.rotation = Quaternion.Slerp(exitRot, exitRot, w);
                yield return null;
            }

            transform.position = exitPos;

            float extraDelay = Mathf.Max(0f, zone.destroyAfterExitDelay);
            if (extraDelay > 0f) yield return new WaitForSeconds(extraDelay);

            if (zone.destroyOnExit)
            {
                Destroy(gameObject);
            }
            else
            {
                if (hasBody)
                {
                    rb.detectCollisions = prevDetect;
                    rb.useGravity = prevGravity;
                    rb.isKinematic = prevKinematic;
                    rb.constraints = prevConstraints;
                }

                transform.SetParent(originalParent, true);
                gameObject.layer = originalLayer;
                gameObject.SetActive(false);

                if (savedHomeSet)
                {
                    if (savedHomeParent != null)
                    {
                        Vector3 restoredPos = savedHomeParent.TransformPoint(savedHomeLocalPos);
                        Quaternion restoredRot = savedHomeParent.rotation * savedHomeLocalRot;
                        SetHomeFromZone(savedHomeParent, restoredPos, restoredRot);
                    }
                    else
                    {
                        SetHomePose(savedHomePos, savedHomeRot);
                    }
                }
            }

            IsReturningHome = false;
            _state = DragState.Idle;
        }

    private void ApplyFollowLean(Vector3 velocity)
    {
        if (IsEventCard)
        {
            float alpha = 1f - Mathf.Exp(-followLeanResponsiveness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, _baseRot, alpha);
            return;
        }

        Vector3 vPlanar = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float vMag = vPlanar.magnitude;

        var camRef = _cam != null ? _cam : (Camera.main != null ? Camera.main : Camera.current);
        if (camRef == null)
        {
            if (_cam == null) _cam = Camera.main != null ? Camera.main : Camera.current;
        }
        Vector3 camForward = camRef != null ? camRef.transform.forward : Vector3.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 1e-6f) camForward = Vector3.forward;
        camForward.Normalize();
        Vector3 camRight = camRef != null ? camRef.transform.right : Vector3.right;
        camRight.y = 0f;
        if (camRight.sqrMagnitude < 1e-6f)
        {
            camRight = new Vector3(camForward.z, 0f, -camForward.x);
        }
        camRight.Normalize();

        float boostK = 1f;
        if (_startBoostT > 0f)
        {
            boostK = Mathf.Lerp(1f, startBoostMul, _startBoostT / Mathf.Max(0.0001f, startBoostTime));
            _startBoostT = Mathf.Max(0f, _startBoostT - Time.deltaTime);
        }

        float alphaBlend = 1f - Mathf.Exp(-followLeanResponsiveness * Time.deltaTime);
        _pointerWorldSpeed = Mathf.MoveTowards(_pointerWorldSpeed, 0f, pointerLeanRelax * Time.deltaTime);
        float maxPitch = Mathf.Max(0f, leanMaxPitchDeg);
        float maxYaw = Mathf.Max(0f, leanMaxYawDeg);
        float maxRoll = Mathf.Max(0f, leanMaxRollDeg);

        float pitch = 0f, yaw = 0f, roll = 0f;

        if (_hasPointerAnchor && box != null)
        {
            Vector3 pointerLocal = transform.InverseTransformPoint(_lastPointerWS);
            Vector3 centerLocal = box.center;
            Vector2 planarRaw = new Vector2(pointerLocal.x - centerLocal.x, pointerLocal.z - centerLocal.z);
            float filter = Mathf.Clamp01(pointerLeanFilter * Time.deltaTime);
            Vector2 planarPrev = _pointerLocalPlanar;
            Vector2 planarFiltered = Vector2.Lerp(planarPrev, planarRaw, filter);
            if (!_hasPointerAnchor) planarFiltered = planarRaw;
            Vector2 moveVec = planarFiltered - planarPrev;
            _pointerLocalPlanarPrev = planarPrev;
            _pointerLocalPlanar = planarFiltered;

            Vector3 size = box.size;
            float halfW = Mathf.Max(0.0001f, size.x * 0.5f);
            float halfH = Mathf.Max(0.0001f, size.y * 0.5f);
            float normX = Mathf.Clamp(planarFiltered.x / halfW, -1f, 1f);
            float normY = Mathf.Clamp(planarFiltered.y / halfH, -1f, 1f);
            _pointerLocalNorm = new Vector2(normX, normY);

            float pointerMag = Mathf.Clamp(planarFiltered.magnitude / Mathf.Max(halfW, halfH), 0f, 1f);
            Vector2 pointerDir = planarFiltered.sqrMagnitude > 1e-6f ? planarFiltered.normalized : Vector2.zero;
            Vector2 moveDir = moveVec.sqrMagnitude > 1e-6f ? moveVec.normalized : Vector2.zero;

            float speedFactor = Mathf.Clamp01(_pointerWorldSpeed * pointerLeanSpeedGain);
            float pointerWeight = Mathf.SmoothStep(0f, 1f, pointerMag);
            float blend = Mathf.Clamp01(pointerWeight * 0.6f + speedFactor * 0.4f);

            float basePitch = Mathf.Clamp(-pointerDir.y * pointerWeight * leanPitchGain, -maxPitch, maxPitch);
            float baseRoll = Mathf.Clamp(pointerDir.x * pointerWeight * leanRollGain, -maxRoll, maxRoll);

            float diagAdjust = pointerDir.x * pointerDir.y * pointerWeight * leanRollGain * 0.5f;
            baseRoll += diagAdjust;

            float movePitch = Mathf.Clamp(-moveDir.y * speedFactor * leanPitchGain * 0.6f, -maxPitch, maxPitch);
            float moveRoll = Mathf.Clamp(moveDir.x * speedFactor * leanRollGain * 0.6f, -maxRoll, maxRoll);

            float desiredPitch = Mathf.Clamp(basePitch + movePitch, -maxPitch, maxPitch);
           

            float desiredRoll = Mathf.Clamp(baseRoll + moveRoll, -maxRoll, maxRoll);

            float yawPointer = pointerDir.x * pointerWeight * leanYawGain * 0.5f;







            float yawMove = 0f;
            if (pointerDir.sqrMagnitude > 1e-6f && moveDir.sqrMagnitude > 1e-6f)
            {
                float angleDeg = Mathf.Clamp(Vector2.SignedAngle(pointerDir, moveDir), -135f, 135f);
                yawMove = (angleDeg / 90f) * leanYawGain * speedFactor;
            }
            float desiredYaw = Mathf.Clamp(yawPointer + yawMove, -maxYaw, maxYaw);

            _pointerAngle = Mathf.Lerp(_pointerAngle, desiredYaw, filter);

            pitch = Mathf.Lerp(0f, desiredPitch, blend);
            roll = Mathf.Lerp(0f, desiredRoll, blend);
            yaw = _pointerAngle;
        }
        else if (vMag > 1e-4f)
        {
            float velForward = Vector3.Dot(vPlanar, camForward);
            float velRight = Vector3.Dot(vPlanar, camRight);
            pitch = Mathf.Clamp(-velForward * leanPitchGain * boostK, -maxPitch, maxPitch);
            roll = Mathf.Clamp(velRight * leanRollGain * boostK, -maxRoll, maxRoll);
            yaw = Mathf.Clamp(velRight * leanYawGain * boostK, -maxYaw, maxYaw);
        }
        else
 {
            _hasPointerAnchor = false;
            _pointerLocalNorm = Vector2.zero;
            _pointerLocalPlanar = Vector2.zero;
            _pointerLocalPlanarPrev = Vector2.zero;
            _pointerAngle = 0f;
        }

        float velForward2 = Vector3.Dot(vPlanar, camForward);
        float velRight2 = Vector3.Dot(vPlanar, camRight);
        float yawVelocity = Mathf.Clamp(velRight2 * leanYawGain * boostK * 0.5f, -maxYaw, maxYaw);
        yaw = Mathf.Clamp(yaw + yawVelocity, -maxYaw, maxYaw);

        Quaternion followRot = _baseRot * Quaternion.Euler(pitch, yaw, roll);
        followRot = SoftLimitToBaseRotation(followRot);
        transform.rotation = Quaternion.Slerp(transform.rotation, followRot, alphaBlend);

        if (debugHoverLogs)
        {
            Debug.Log($"[CardView3D] 倾斜 pitch={pitch:F2}, yaw={yaw:F2}, roll={roll:F2}, vMag={vMag:F2}");
        }

        // 仅脚本驱动旋转；避免写入刚体的速度
        if (body != null && !body.isKinematic)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
        
        // 拖拽中不做额外抬升，避免改变跟随平面的 y（造成鼠标与物体错位）
        if (_state != DragState.Dragging)
        {
            float lift = 0f;
            transform.position += Vector3.up * lift;
        }
    }

    private void GateWobbleBySpeed(Vector3 velocity)
    {
        if (_state != DragState.Dragging)
        {
            if (_wobbleCo != null) { StopCoroutine(_wobbleCo); _wobbleCo = null; }
            if (_fadeCo != null) { StopCoroutine(_fadeCo); _fadeCo = null; }
            _tiltWeight = 0f;
            _wobbleIdleTimer = 0f;
            _wobbleCooldownTimer = 0f;
            return;
        }

        float v = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
        float high = Mathf.Max(0f, speedThreshold);
        float low = Mathf.Max(0f, speedThreshold - Mathf.Max(0f, speedHysteresis));
        float pointerSpeed = _pointerWorldSpeed;
        bool pointerStill = pointerSpeed <= 0.05f;

        if (v > high || !pointerStill)
        {
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(FadeTilt(0f, wobbleEaseOut));
            if (_wobbleCo != null) { StopCoroutine(_wobbleCo); _wobbleCo = null; }
            _wobbleIdleTimer = 0f;
            _wobbleCooldownTimer = wobbleCooldown;
        }
        else if (v < low && pointerStill)
        {
            if (_wobbleCooldownTimer > 0f)
            {
                _wobbleCooldownTimer = Mathf.Max(0f, _wobbleCooldownTimer - Time.deltaTime);
                return;
            }

            _wobbleIdleTimer += Time.deltaTime;
            if (_wobbleIdleTimer >= wobbleStartDelay)
            {
                if (_wobbleCo == null) _wobbleCo = StartCoroutine(WobbleLoop());
                if (_fadeCo != null) StopCoroutine(_fadeCo);
                _fadeCo = StartCoroutine(FadeTilt(1f, wobbleEaseIn));
            }
        }
        else
        {
            _wobbleIdleTimer = 0f;
        }
    }

    private IEnumerator ReleaseDrop()
    {
        Vector3 start = transform.position;
        Vector3 end = new Vector3(transform.position.x, 0f, transform.position.z);
        Quaternion startR = transform.rotation;
        Quaternion endR = _baseRot;
        float t = 0f, dur = 0.2f; // 固定下落时间
        Vector3 currentVelocity = Vector3.zero;
        
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            
            // 直接线性插值计算位置
            Vector3 targetPos = Vector3.Lerp(start, end, p);
            transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref currentVelocity, 0.05f);
            transform.rotation = Quaternion.Slerp(startR, endR, p);
            yield return null;
        }
        
        // 确保最终位置和旋转完全精确
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
        float xLimit = leanMaxPitchDeg > 0.0001f ? leanMaxPitchDeg : m;
        float yLimit = leanMaxYawDeg > 0.0001f ? leanMaxYawDeg : m;
        float zLimit = leanMaxRollDeg > 0.0001f ? leanMaxRollDeg : m;

        euler.x = ApplySoftClamp(euler.x, xLimit, m);
        euler.y = ApplySoftClamp(euler.y, yLimit, m);
        euler.z = ApplySoftClamp(euler.z, zLimit, m);
        return _baseRot * Quaternion.Euler(euler);
    }

    private float ApplySoftClamp(float angle, float preferredLimit, float fallbackLimit)
    {
        float limit = preferredLimit > 0.0001f ? preferredLimit : fallbackLimit;
        if (limit <= 0.0001f) return 0f;
        return SoftClamp(angle, limit);
    }

    private static float Normalize180(float angle)
    {
        angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
        return angle;
    }

    private static CardCategory ParseCategory(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) return CardCategory.Unknown;
        string t = type.Trim().ToLowerInvariant();
        switch (t)
        {
            case "unit":
            case "units":
                return CardCategory.Unit;
            case "event":
            case "events":
            case "spell":
                return CardCategory.Event;
            default:
                return CardCategory.Unknown;
        }
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

    private static Quaternion AlignToTableRotation(Quaternion rot)
    {
        Vector3 euler = rot.eulerAngles;
        return Quaternion.Euler(90f, euler.y, 0f);
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
        _hasPointerAnchor = false;
        _pointerWorldSpeed = 0f;
        _pointerLocalNorm = Vector2.zero;
        _pointerLocalPlanar = Vector2.zero;
        _pointerLocalPlanarPrev = Vector2.zero;
        _pointerAngle = 0f;
        _wobbleIdleTimer = 0f;
        _wobbleCooldownTimer = 0f;
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
            // Preserve box.isTrigger state; do not force non-trigger here as other systems may require it
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
    public void ApplyHandRenderOrder(int order)
    {
        if (_allRenderers == null || _allRenderers.Length == 0) _allRenderers = GetComponentsInChildren<Renderer>(true);
        if (_allRenderers == null) return;
        if (_handBaseSortingOrders == null || _handBaseSortingOrders.Length != _allRenderers.Length)
        {
            _handBaseSortingOrders = new int[_allRenderers.Length];
            for (int i = 0; i < _allRenderers.Length; i++)
            {
                var rInit = _allRenderers[i];
                _handBaseSortingOrders[i] = rInit != null ? rInit.sortingOrder : 0;
            }
        }
        if (_origSortingOrders == null || _origSortingOrders.Length != _allRenderers.Length)
            _origSortingOrders = new int[_allRenderers.Length];

        for (int i = 0; i < _allRenderers.Length; i++)
        {
            var r = _allRenderers[i]; if (r == null) continue;
            int baseOrder = _handBaseSortingOrders[i];
            int finalOrder = baseOrder + order;
            r.sortingOrder = finalOrder;
            _origSortingOrders[i] = finalOrder;
        }
    }

    public void SetRenderOrder(int order)
    {
        ApplyHandRenderOrder(order);
    }

    // 保证卡牌Y值不低于21且只保留两位小数
    private static float AdjustCardY(float y)
    {
        // 确保卡牌Y值不接近20，并保留两位小数
        float adjustedY = Mathf.Max(y, 22f); // 将最低值提高到22
        return Mathf.Round(adjustedY * 100f) / 100f;
    }
}
