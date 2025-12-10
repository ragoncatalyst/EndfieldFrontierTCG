using System.Collections;
using UnityEngine;
using TMPro;
using EndfieldFrontierTCG.Board;
using EndfieldFrontierTCG.Hand;
// Core CardView3D fields and minimal lifecycle. Functionality is split
// across partial class files: Rearrange, InfoDisplay, ReturnHome, Drag.

public partial class CardView3D : MonoBehaviour
{
    // Public refs (kept minimal)
    [Header("Refs")] public Rigidbody body;
    public BoxCollider box;
    public Renderer MainRenderer;
    public TMP_Text NameText;
    public TMP_Text HPText;
    public TMP_Text ATKText;

    // Basic identity
    public enum CardCategory { Unknown, Unit, Event }
    public CardCategory Category { get; private set; } = CardCategory.Unknown;
    public string CardType { get; private set; } = string.Empty;
    [HideInInspector] public int handIndex = -1;
    [HideInInspector] public int slotIndex = -1;
    [HideInInspector] public int cardId = -1;
    [HideInInspector] public int createId = -1;  // creation order for sorting (older < newer)
    private static int s_nextCreateId = 0;
    [HideInInspector] public int lastKnownSlotIndex = -1;
    [HideInInspector] public int lastKnownHandIndex = -1;

    // Simplified follow/drag params
    public float followDragSmooth = 0.04f;
    public float dragFrontBias = 0.03f;
    public float clickMaxDuration = 0.15f;
    public float clickMaxDistance = 0.05f;

    // Hand stacking Y logic
    private float _targetY = 0f; // transient offsets (e.g. elevation)
    private float _baseY = 0.3f; // per-card base Y
    private static float s_nextY = 0.3f;
    private const float Y_SPACING = 0.02f;
    private const float MIN_HAND_Y = 0.28f;
    public bool simplifyMode = true; // keeps minimal behavior when true

    // Home pose (private; managed by ReturnHome partial)
    private Vector3 _handRestPosition;
    private Vector3 _homeLocalPos;
    private Quaternion _homeLocalRot;
    private Vector3 _homePos;
    private Quaternion _homeRot;
    private Transform _homeParent;
    private bool _homeSet = false;

    // Drag state (used by Drag partial)
    private enum DragState { Idle, Picking, Dragging, Releasing }
    private DragState _state = DragState.Idle;
    public bool IsDragging => _state == DragState.Dragging || _state == DragState.Picking;

    // Internal
    private Camera _cam;
    private Vector3 _dragOffsetWS;
    private bool _dragOffsetInitialized = false;
    private Vector3 _smoothVel;
    private Vector3 _lastTarget;
    private float _mouseDownTime;
    private Vector3 _mouseDownPos;

    // Return coroutine control (used by ReturnHome partial)
    private Coroutine _returnHomeCo;
    public bool IsReturningHome { get; private set; }
    private bool _forceReturnYNow = false;
    // Per-card return timing defaults (some callers read these fields)
    [HideInInspector] public float returnPhase1Duration = 0.18f;
    [HideInInspector] public float returnPhase2Duration = 0.22f;

    private void Awake()
    {
        _cam = Camera.main != null ? Camera.main : Camera.current;
        if (body == null) body = GetComponent<Rigidbody>();
        if (box == null) box = GetComponent<BoxCollider>();
        if (body != null)
        {
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    private void Start()
    {
        // init per-card baseY spacing (A-scheme)
        _baseY = s_nextY;
        s_nextY += Y_SPACING;
        _handRestPosition = transform.position;
        _handRestPosition.y = GetFinalHandY();
        _homePos = _handRestPosition;
        _homeRot = transform.rotation;

        // assign creation order id for sorting
        createId = s_nextCreateId++;

        _homeSet = true;
    }

    // Canonical final hand Y (base + transient offset)
    public float GetFinalHandY() => _baseY + _targetY;

    public void SetTargetY(float offsetY) => _targetY = offsetY;

    // Compatibility helpers expected by many callers elsewhere in the project
    // Indicates whether this card is a Unit (common call-site: CardSlotBehaviour)
    public bool IsUnitCard => Category == CardCategory.Unit;

    // Rendering order: callers (HandSplineZone etc.) set draw order via this API
    public void SetRenderOrder(int order)
    {
        if (MainRenderer != null)
        {
            try { MainRenderer.sortingOrder = order; } catch { }
        }
    }

    // External drag wrappers: some zones call into the card rather than the card
    // listening for input. These are simple, safe stubs that can be replaced
    // with richer behavior in the Drag partial later.
    public void ExternalBeginDrag()
    {
        _dragOffsetInitialized = false;
        _state = DragState.Picking;
        _mouseDownTime = Time.time;
    }

    public void ExternalDrag(Vector3 worldPosition)
    {
        _state = DragState.Dragging;
        worldPosition.y = GetFinalHandY();
        transform.position = worldPosition;
    }

    // Convenience overload used by older callers that don't pass a position
    public void ExternalDrag()
    {
        _state = DragState.Dragging;
        // leave position untouched; caller likely updates card transform elsewhere
    }

    public void ExternalEndDrag(bool snapHome = true)
    {
        _state = DragState.Idle;
        if (snapHome) ReturnHomeUnified();
    }

    // small helpers
    private static float Normalize180(float angle) { angle = Mathf.Repeat(angle + 180f, 360f) - 180f; return angle; }
    private static float AdjustCardY(float y) { return Mathf.Max(y, MIN_HAND_Y); }

}

