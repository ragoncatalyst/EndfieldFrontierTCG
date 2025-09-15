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
    public float dragPlaneY = 0.35f; // 降低拖动高度，避免太高
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
        // 清除父级引用
        _homeParent = null;

        // 直接使用世界空间坐标和旋转
        _homePos = pos;
        _homeRot = rot;

        // 本地空间坐标和旋转与世界空间相同
        _homeLocalPos = pos;
        _homeLocalRot = rot;

        // 标记家位置已设置
        _homeSet = true;

        if (debugHoverLogs)
        {
            Debug.Log($"[CardView3D] 设置家位置（无父级） - 世界坐标: {pos}");
        }
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
        _homePos = worldPos;
        _homeRot = worldRot;

        // 标记家位置已设置
        _homeSet = true;

        if (debugHoverLogs)
        {
            Debug.Log($"[CardView3D] 设置家位置 - 世界坐标: {worldPos}, 本地坐标: {_homeLocalPos}, 父级: {(zone != null ? zone.name : "无")}");
        }
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

        // 提升排序，避免被其他卡牌遮挡
        if (_allRenderers != null)
        {
            for (int i = 0; i < _allRenderers.Length; i++)
            {
                try { _allRenderers[i].sortingOrder = _origSortingOrders[i] + returnSortingBoost; } catch {}
            }
        }

        // Phase 1: 保持当前高度，在XZ平面上移动到手牌区域前方的临时点
        float t = 0f;
        float dur1 = Mathf.Max(0.0001f, returnPhase1Duration);
        Vector2 startXZ = new Vector2(startPos.x, startPos.z);
        Vector2 tempXZ2 = new Vector2(tempXZ.x, tempXZ.z);
        float currentY = startPos.y; // 保持当前高度
        Vector2 velocity = Vector2.zero;

        if (debugHoverLogs)
        {
            Debug.Log($"[CardView3D] 开始第一阶段返回 - 从: {startPos}, 到前方点: {tempXZ}, 持续: {dur1}秒");
        }

        while (t < dur1)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur1);

            // 使用简单的线性插值
            Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
            Vector2 newXZ = Vector2.Lerp(currentXZ, tempXZ2, a);

            // 应用位置（保持Y不变）和旋转
            transform.position = new Vector3(newXZ.x, currentY, newXZ.y);
            transform.rotation = Quaternion.Slerp(startRot, _homeRot, a * 0.4f);

            if (debugHoverLogs && t % 0.1f < Time.deltaTime)
            {
                Debug.Log($"[CardView3D] 第一阶段进度 - {(a * 100):F0}%, 速度: {targetSpeed:F2}, 位置: {newXZ}");
            }

            yield return null;
        }

        // 确保在进入阶段2之前，XZ位置完全正确
        transform.position = new Vector3(tempXZ.x, currentY, tempXZ.z);
        yield return null;

        // Phase 2: 从临时点平滑移动到最终位置
        Vector3 startP2 = transform.position;
        Quaternion startR2 = transform.rotation;
        Vector3 endP2 = new Vector3(_homePos.x, homeY, _homePos.z);
        float dur2 = Mathf.Max(0.01f, returnPhase2Duration);
        float tP2 = 0f;
        Vector3 velocity3D = Vector3.zero;

        // 添加前向偏移，避免被相邻卡片遮挡
        Vector3 cameraForward = CameraForwardPlanar();
        startP2 += cameraForward * returnFrontBias;

        if (debugHoverLogs)
        {
            Debug.Log($"[CardView3D] 开始第二阶段返回 - 从: {startP2}, 到: {endP2}, 持续: {dur2}秒");
        }

        while (tP2 < dur2)
        {
            tP2 += Time.deltaTime;
            float a = Mathf.Clamp01(tP2 / dur2);

            // 使用简单的线性插值
            Vector3 newPos = Vector3.Lerp(transform.position, endP2, a);

            // 应用位置和旋转
            transform.position = newPos;
            transform.rotation = Quaternion.Slerp(startR2, _homeRot, a);

            if (debugHoverLogs && t % 0.1f < Time.deltaTime)
            {
                Debug.Log($"[CardView3D] 第二阶段进度 - {(a * 100):F0}%, 速度: {targetSpeed:F2}, 位置: {newPos}");
            }

            yield return null;
        }

        // 恢复原始排序顺序
        if (_allRenderers != null)
        {
            for (int i = 0; i < _allRenderers.Length; i++)
            {
                try { _allRenderers[i].sortingOrder = _origSortingOrders[i]; } catch {}
            }
        }

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
        
        // 确保所有渲染器都是启用的
        if (_allRenderers != null)
        {
            foreach (var renderer in _allRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                    // 提升渲染顺序，确保在拖动时可见
                    renderer.sortingOrder += dragSortingBoost;
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
                    
                    // 提升排序顺序
                    r.sortingOrder += dragSortingBoost;
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

            // 使用射线检测所有可能的槽位
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 1000f, interactLayerMask, QueryTriggerInteraction.Collide);
            
            // 找到最近的可用槽位
            CardSlotBehaviour nearestSlot = null;
            float nearestDistance = float.MaxValue;
            Vector3 mouseWorldPos = Vector3.zero;

            foreach (var hit in hits)
            {
                // 检查是否击中了槽位
                var slot = hit.collider.GetComponent<CardSlotBehaviour>();
                if (slot != null)
                {
                    float distance = Vector3.Distance(transform.position, slot.transform.position);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestSlot = slot;
                        mouseWorldPos = hit.point;
                    }
                }
            }

                // 如果找到了槽位，平滑移动过去
                if (nearestSlot != null)
                {
                    Debug.Log($"[CardView3D] 找到最近的槽位: {nearestSlot.name}, 距离: {nearestDistance}");
                    
                    // 记录当前世界空间位置和旋转
                    Vector3 currentWorldPos = transform.position;
                    Quaternion currentWorldRot = transform.rotation;
                    
                    // 断开与所有手牌区域的联系
                    var allHandZones = GameObject.FindObjectsOfType<EndfieldFrontierTCG.Hand.HandSplineZone>();
                    foreach (var handZone in allHandZones)
                    {
                        if (handZone != null)
                        {
                            handZone.ClearInputState();
                            handZone.UnregisterCard(this); // 从手牌区域注销这张卡
                        }
                    }
                    
                    // 确保不会被手牌区域重新认领
                    transform.SetParent(null);
                    _homeSet = false;
                    handIndex = -1; // 清除手牌索引
                    
                    // 恢复到记录的位置，确保从正确的位置开始移动
                    transform.position = currentWorldPos;
                    transform.rotation = currentWorldRot;
                    
                    StartCoroutine(SmoothMoveToSlot(nearestSlot));
                    return;
                }

            // 如果没有找到槽位，检查是否在手牌区域
            var currentHandZone = GetComponentInParent<EndfieldFrontierTCG.Hand.HandSplineZone>();
            if (currentHandZone != null && _homeSet)
            {
                // 使用配置的二段式返回动画
                BeginSmoothReturnToHome(returnFrontBias, returnPhase1Duration, returnPhase2Duration);
                return;
            }

            // 如果不在手牌区域，直接执行掉落动画
            StartCoroutine(ReleaseDrop());
        }

        private IEnumerator SmoothMoveToSlot(CardSlotBehaviour slot)
        {
            if (slot == null) yield break;

            // 先通知槽位我们要放置卡牌，让它做好准备
            if (!slot.CanAcceptCard(this))
            {
                Debug.LogWarning($"[CardView3D] 槽位 {slot.name} 无法接受卡牌");
                yield break;
            }

            // 获取槽位的精确位置和旋转
            Vector3 startPos = transform.position;
            Vector3 targetPos = slot.GetCardPosition();
            Quaternion startRot = transform.rotation;
            Quaternion targetRot = slot.GetCardRotation();
            
            // 记录初始状态
            var originalParent = transform.parent;
            var originalLayer = gameObject.layer;
            var originalState = _state;
            
            try
            {
                // 设置状态为移动中
                _state = DragState.Releasing;
                
                // 暂时禁用碰撞，避免移动过程中的干扰
                if (body != null)
                {
                    body.isKinematic = true;
                    body.detectCollisions = false;
                }
                
                // 计算移动参数
                float distance = Vector3.Distance(startPos, targetPos);
                float moveTime = Mathf.Lerp(0.2f, 0.4f, distance / 2f); // 增加时间，让动画更平滑
                float t = 0;
                Vector3 currentVelocity = Vector3.zero;
                
                Debug.Log($"[CardView3D] 开始移动，当前世界位置: {transform.position}");
                
                // 记录当前世界空间位置和旋转
                Vector3 worldReleasePos = transform.position;
                Quaternion worldReleaseRot = transform.rotation;
                
                // 先计算在槽位空间中的起始位置
                Vector3 startLocalPos = slot.transform.InverseTransformPoint(worldReleasePos);
                Vector3 endLocalPos = Vector3.zero; // 槽位中心
                
                Debug.Log($"[CardView3D] 转换到本地空间的起始位置: {startLocalPos}");
                
                // 设置父物体，但保持世界位置不变
                transform.SetParent(slot.transform, true);
                
                // 确保我们从正确的起始位置开始
                transform.localPosition = startLocalPos;
                
                Debug.Log($"[CardView3D] 设置父物体后的本地位置: {transform.localPosition}");
                
                // 计算垂直下落的本地空间终点（保持XZ不变，只改变Y）
                Vector3 dropLocalEndPos = new Vector3(startLocalPos.x, endLocalPos.y, startLocalPos.z);
                
                // 计算本地空间距离
                float dropDistance = Mathf.Abs(startLocalPos.y - endLocalPos.y);
                float slideDistance = Vector2.Distance(
                    new Vector2(startLocalPos.x, startLocalPos.z),
                    new Vector2(endLocalPos.x, endLocalPos.z)
                );
                
                // 根据距离调整时间
                float dropTime = Mathf.Lerp(0.15f, 0.3f, dropDistance / 1f);
                
                // 记录速度
                Vector3 slideVelocity = Vector3.zero;
                Vector3 currentLocalPos = startLocalPos;
                
                // 获取目标旋转
                Quaternion endWorldRot = slot.GetCardRotation();
                
                float elapsedTime = 0f;
                
                while (elapsedTime < dropDuration)
                {
                    elapsedTime += Time.deltaTime;
                    float progress = elapsedTime / dropDuration;
                    
                    // 使用曲线计算整体移动进度
                    float moveProgress = dropCurve.Evaluate(progress);
                    
                    // 直接在所有轴向上进行插值，实现斜向下落
                    Vector3 currentPos = Vector3.Lerp(startLocalPos, endLocalPos, moveProgress);
                    transform.localPosition = currentPos;
                    
                    // 平滑旋转到目标角度
                    transform.rotation = Quaternion.Slerp(transform.rotation, endWorldRot, moveProgress);
                    
                    yield return null;
                }
                
                // 确保最终位置和旋转完全精确
                transform.position = targetPos;
                transform.rotation = targetRot;
                
                // 设置为新的家位置
                SetHomePose(targetPos, targetRot);
                
                // 正式通知槽位放置完成
                if (slot.TryPlaceCard(this))
                {
                    Debug.Log($"[CardView3D] 成功放置到槽位: {slot.name}");
                    _state = DragState.Idle;
                    _homeSet = false; // 确保不会被手牌区域重新认领
                    
                    // 断开与所有手牌区域的联系
                    var allHandZones = GameObject.FindObjectsOfType<EndfieldFrontierTCG.Hand.HandSplineZone>();
                    foreach (var handZone in allHandZones)
                    {
                        if (handZone != null)
                        {
                            handZone.ClearInputState();
                            // 如果这个卡牌在这个手牌区域中，移除它
                            if (handZone.transform == transform.parent)
                            {
                                transform.SetParent(slot.transform);
                            }
                        }
                    }

                    // 强制设置父物体为槽位
                    transform.SetParent(slot.transform, true);
                    
                    // 禁用碰撞和物理，防止意外移动
                    if (body != null)
                    {
                        body.isKinematic = true;
                        body.detectCollisions = false;
                        body.constraints = RigidbodyConstraints.FreezeAll;
                    }
                    
                    // 更新卡牌的家位置为槽位
                    SetHomePose(targetPos, targetRot);
                }
                else
                {
                    Debug.LogError($"[CardView3D] 放置失败: {slot.name}");
                    // 如果放置失败，恢复原始状态
                    transform.SetParent(originalParent, true);
                    gameObject.layer = originalLayer;
                    _state = originalState;
                }
            }
            finally
            {
                // 恢复碰撞检测
                if (body != null)
                {
                    body.isKinematic = true;
                    body.detectCollisions = true;
                    body.constraints = RigidbodyConstraints.FreezeAll;
                }
            }
        }

    private void ApplyFollowLean(Vector3 velocity)
    {
        Vector3 vPlanar = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float vMag = vPlanar.magnitude;
        
        // 保持当前旋转，让倾斜效果基于当前旋转计算
        
        if (vMag > 1e-4f)
        {
            // 计算移动方向在世界空间中的向量
            Vector3 moveDir = vPlanar.normalized;
            
            // 计算倾斜轴：使用世界空间的右方向作为参考
            Vector3 axis = Vector3.Cross(moveDir, Vector3.up).normalized;
            
            float boostK = 1f;
            if (_startBoostT > 0f)
            {
                boostK = Mathf.Lerp(1f, startBoostMul, _startBoostT / Mathf.Max(0.0001f, startBoostTime));
                _startBoostT = Mathf.Max(0f, _startBoostT - Time.deltaTime);
            }

            // 计算倾斜角度（保持较小的角度以避免翻转）
            float ang = Mathf.Clamp(vMag * followLeanSpeedScale * boostK, 0f, followLeanMaxDeg * 0.5f);
            
            // 先应用基准旋转，再应用倾斜
            Quaternion followRot = _baseRot * Quaternion.AngleAxis(ang, axis);

            // 添加调试日志
            if (debugHoverLogs)
            {
                Debug.Log($"[CardView3D] 倾斜角度: {ang}, 速度: {vMag}, 轴: {axis}, 旋转: {followRot.eulerAngles}");
            }

            // 确保旋转不会导致卡牌翻转
            followRot = SoftLimitToBaseRotation(followRot);
            
            // 指数平滑过渡
            float alpha = 1f - Mathf.Exp(-followLeanResponsiveness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, followRot, alpha);
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
