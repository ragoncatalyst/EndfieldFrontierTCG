using UnityEngine;
using EndfieldFrontierTCG.CA;
using EndfieldFrontierTCG.Environment;
using EndfieldFrontierTCG.Board;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace EndfieldFrontierTCG.Hand
{
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(-5)]
	public partial class HandSplineZone : MonoBehaviour
	{
		public const string HandZoneVersion = "HZ_VER_2025_09_03_12_28";

		[Header("Layout (Line)")]
		public int slots = 10;
		public float offsetUp = 0.0f;
		public float offsetForward = 0.0f;
		public bool flipForward = false;
		[Range(-180f, 180f)] public float yawAdjustDeg = 0f;
		[Tooltip("吸附阈值（米），拖拽到该距离内则吸附到槽位")] public float snapDistance = 0.25f;

		[Header("Auto Placement")]
		[Tooltip("是否自动把手牌栏放到摄像机视野底部（同X于摄像机）")]
		public bool autoPlaceAtCameraBottom = true;
		[Tooltip("在视口底部往上保留的归一化边距 (0-0.2 比较合适)")]
		[Range(0f, 0.2f)] public float viewportBottomMargin = 0.06f;

		[Header("Depth Stacking")]
		public bool stackDepthByIndex = true;
		public float depthPerSlot = 0.01f;
		public bool reverseDepthOrder = false;
		[Tooltip("当手牌静止时，使用每槽位的垂直偏移来保持前后关系（米），而不是沿摄像机方向的 Z 偏移）")]
		public float verticalDepthPerSlot = 0.002f;

		[Header("Line Params")]
		public float lineSpacing = 0.8f;
		public Vector3 lineLocalDirection = new Vector3(1, 0, 0);
		public float lineLocalY = 0f;
		public float lineYawOffsetDeg = 90f;

		[Header("Shadows")]
		[Tooltip("为避免接触阴影被灯光偏移吃掉，给所有槽位抬升的微小高度（米）")]
		public float shadowLiftY = 0.02f;

		[Header("Card Rotation")]
		[Tooltip("是否使用固定欧拉角作为卡牌基础朝向（避免被代码强制成难以修改的姿态）")]
		public bool useFixedCardEuler = true;
		[Tooltip("卡牌基础欧拉角（作为输出旋转的基准）")]
		public Vector3 fixedCardEuler = new Vector3(90f, 0f, 0f);
		[Tooltip("是否根据手牌线方向自动叠加水平朝向（绕 Y 轴）")]
		public bool yawAlignToLineDirection = true;

		[Header("Hover Interact")]
		[Range(0f, 100f)] public float hoverX = 0.06f;
		[Range(0.01f, 1f)] public float hoverXLerp = 0.1f;
		public float hoverZ = 10f;
		[Range(0.01f, 3f)] public float hoverZLerp = 0.1f;
		public float hoverZLeft = 10f;
		[Range(0.01f, 3f)] public float hoverZLeftLerp = 0.1f;
		public bool invertHoverSide = false;
		[Tooltip("ENTER 来源判定的像素冗余（用于‘从下方进入’判断）")] [Range(0f, 20f)] public float enterFromBelowSlackPx = 6f;
		[Tooltip("把卡牌下边缘向下扩展的像素（把红色区域等同为碰撞箱的一部分）")] [Range(0f, 60f)] public float hoverBottomExtendPx = 12f;
		[Tooltip("在世界坐标系 Z- 方向虚拟延伸碰撞箱的距离（米），仅用于 hover 检测")]
		[Range(0f, 0.5f)] public float hoverExtendZBackward = 0.05f;

		[Header("Input Thresholds")]
		[Tooltip("开始拖拽所需的最小鼠标位移（像素）")]
		public float pressMoveThresholdPx = 8f;
		[Tooltip("开始拖拽所需的最短按住时间（秒）")]
		public float pressHoldThresholdSec = 0.12f;

[Header("Return-to-Home")]
        [Tooltip("第一阶段：XZ平面移动的速度曲线（0→1）\n值越大移动越快\n建议范围：0.2-1.0")]
        public AnimationCurve returnPhase1Curve = new AnimationCurve(
            new Keyframe(0f, 0.2f, 0f, 2f),
            new Keyframe(0.3f, 0.8f, 1f, 1f),
            new Keyframe(0.7f, 0.8f, 0f, 0f),
            new Keyframe(1f, 0.2f, -2f, 0f)
        );

        [Tooltip("第二阶段：回到手牌的速度曲线（0→1）\n值越大移动越快\n建议范围：0.2-1.0")]
        public AnimationCurve returnPhase2Curve = new AnimationCurve(
            new Keyframe(0f, 0.2f, 0f, 1f),
            new Keyframe(0.4f, 0.9f, 1f, 1f),
            new Keyframe(0.6f, 0.9f, 0f, 0f),
            new Keyframe(1f, 0.2f, -2f, 0f)
        );

        // 新增：供“非 hover 卡牌”使用的归位曲线（允许在 Inspector 为被 hover / 非被 hover 的归位分别编辑）
        [Tooltip("非 hover 卡牌的第一阶段归位曲线（可在 Inspector 编辑）")]
        public AnimationCurve returnPhase1Curve_Other = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("非 hover 卡牌的第二阶段归位曲线（可在 Inspector 编辑）")]
        public AnimationCurve returnPhase2Curve_Other = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // 新增：供 RepositionByHover 映射使用的 S-t 曲线（完全由玩家在 Inspector 编辑）
        [Tooltip("被 hover 的卡牌在 RepositionByHover 中的进度映射曲线（S(u)，0→1）")]
        public AnimationCurve hoverCardMoveCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("其他卡牌在 RepositionByHover 中的进度映射曲线（S(u)，0→1）")]
        public AnimationCurve otherCardsMoveCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        
        [Tooltip("前向偏移距离（米）")]
        public float returnAheadZ = 0.2f;
        [Tooltip("第一阶段持续时间（秒）")]
        public float returnPhase1 = 0.15f;
        [Tooltip("第二阶段持续时间（秒）")]
        public float returnPhase2 = 0.18f;
	[Tooltip("归位时：当有卡插回手牌，临时把左侧邻卡抬高、右侧邻卡降低的 Y 偏移（米）。仅作用于动画目标，不修改逻辑插槽位置。）")]
	public float returnNeighborYOffset = 0.02f;

		[Header("Debug")] public bool debugLogs = false;
		[Header("Build")]
		[SerializeField] private string handZoneRevision = "rev_2025-09-03_02"; // touch to force recompilation
		[Tooltip("用于独立悬停检测的层遮罩（-1 表示全部层）")] public LayerMask interactLayerMask = ~0;

		private CardView3D[] _cards = new CardView3D[0];
		private int _hoverIndex = -1;
		[Tooltip("Cooldown (seconds) to prevent rapid hover switching when pointer sits between overlapping cards")]
		public float hoverChangeCooldown = 0.1f;
		// last time the hover index was changed (unscaled time)
		private float _lastHoverChangeTime = -999f;
		// pending candidate for stabilization: when pointer moves between overlapping cards
		[Tooltip("Required stable time (s) that a hover candidate must persist before it's committed")] public float hoverStableTime = 0.12f;
		private int _pendingHoverIndex = -2; // sentinel: -2 = no pending
		private float _pendingHoverStartTime = -999f;

		[Header("Hover Pass Tuning")]
		[Tooltip("If the pointer moves faster than this (pixels/sec), treat the pass as an intentional quick hover and commit immediately")] public float hoverImmediateSpeed = 1200f;
		[Tooltip("Allow fast pointer passes to immediately trigger hover regardless of stable time")] public bool allowImmediateHoverOnFastPass = true;
		// track mouse motion between frames (screen pixels)
		private Vector2 _lastMousePos = Vector2.zero;
		private float _lastMouseTime = -999f;
		private bool _hoverFromBelow = false;
		private Coroutine _hoverCo;
		private int _paramsHash = 0;
		private readonly Dictionary<int, float> _enterMinYByIndex = new Dictionary<int, float>();
		private bool _pseudoHover = false;
		// Press/drag tracking
		private bool _pressing = false;
		private Vector2 _pressStartPos;
		private CardView3D _activePressed = null;
		private bool _activeDragging = false;
		private float _pressStartTime = 0f;
		// Compacting state during drag
		private bool _compactOnDrag = false;
		private CardView3D _draggingCard = null;
		private int _draggingSlot = -1;
		// Compact mapping: other cards -> reassigned contiguous slots around center (visual only)
		private readonly System.Collections.Generic.Dictionary<CardView3D, int> _origSlotIndex = new System.Collections.Generic.Dictionary<CardView3D, int>();
		private readonly System.Collections.Generic.Dictionary<CardView3D, int> _compactAssignedSlot = new System.Collections.Generic.Dictionary<CardView3D, int>();
		private readonly Dictionary<CardView3D, float> _targetUnits = new Dictionary<CardView3D, float>();
		[SerializeField]
		private float _baseSpacingUnits = 1f;
		[SerializeField]
		private int _renderOrderStep = 5;
		// Drag gap reservation: temporarily remove dragged card so the rest can snap to center immediately
		private CardView3D _reservedGapCard = null;
		private int _reservedGapSlot = -1;
		private int _reservedGapInsertIndex = -1;
		private readonly Dictionary<CardView3D, Coroutine> _repositionCos = new Dictionary<CardView3D, Coroutine>();

		private Coroutine _returnCo = null;

		private void BuildCompactMapping()
		{
			_compactAssignedSlot.Clear(); _origSlotIndex.Clear();
			if (_cards == null || _cards.Length == 0) return;
			// Collect others ordered by their current slotIndex
			var others = new System.Collections.Generic.List<CardView3D>();
			for (int i = 0; i < _cards.Length; i++)
			{
				var c = _cards[i]; if (c == null) continue; if (_draggingCard != null && c == _draggingCard) continue;
				others.Add(c);
			}
			others.Sort((a,b)=> (a.slotIndex).CompareTo(b.slotIndex));
			int count = others.Count; if (count <= 0) return;
			int centerSlot = (slots - 1) / 2;
			int start = centerSlot - (count - 1) / 2;
			for (int k = 0; k < count; k++)
			{
				var c = others[k]; int assigned = start + k;
				_compactAssignedSlot[c] = Mathf.Clamp(assigned, 0, Mathf.Max(0, slots - 1));
				_origSlotIndex[c] = c.slotIndex;
			}
		}

		private void Awake()
		{
			if (debugLogs)
				Debug.Log($"[HandSplineZone] Awake ({handZoneRevision}) at {name}");
		}

		private void OnEnable()
		{
			var childCards = GetComponentsInChildren<CardView3D>(true);
			if (childCards != null && childCards.Length > 0) RegisterCards(childCards);
			if (_hoverCo == null) _hoverCo = StartCoroutine(RepositionByHover());
		}

		private void OnValidate()
		{
			// 用一个不常见的值强制认为“参数改变”，触发重建/重编行为
			_paramsHash = -1;
		}

		private void LateUpdate()
		{
            // compute mouse speed sample for RequestHoverChange usage (use previous stored values)
            // note: we update the stored values at the end of LateUpdate so RequestHoverChange sees the last-frame sample

			// 自动将手牌栏放置到摄像机视野底部（与摄像机同 X）。
			if (autoPlaceAtCameraBottom)
			{
				var cam = Camera.main;
				if (cam != null)
				{
					float yPlane = coupleToTable ? (GetTableYOr(transform.position.y) + handHeightAboveTable) : transform.position.y;
					Vector3 p = WorldPointOnYPlaneByViewport(cam, new Vector2(0.5f, Mathf.Clamp01(viewportBottomMargin)), yPlane, transform.position);
					transform.position = new Vector3(cam.transform.position.x, yPlane, p.z);
				}
			}

			int h = 17;
			h = h * 31 + hoverX.GetHashCode();
			h = h * 31 + hoverXLerp.GetHashCode();
			h = h * 31 + hoverZ.GetHashCode();
			h = h * 31 + hoverZLerp.GetHashCode();
			h = h * 31 + hoverZLeft.GetHashCode();
			h = h * 31 + hoverZLeftLerp.GetHashCode();
			h = h * 31 + invertHoverSide.GetHashCode();
			h = h * 31 + enterFromBelowSlackPx.GetHashCode();
			h = h * 31 + hoverBottomExtendPx.GetHashCode();
			if (h != _paramsHash)
			{
				_paramsHash = h;
				if (_hoverCo != null) StopCoroutine(_hoverCo);
				_hoverCo = StartCoroutine(RepositionByHover());
			}

			// 中央输入：统一更新 hover（真/伪），再驱动拖拽
			UpdateHoverUnified();
			DriveDragByRay();

			// Enforce strict raycast-driven hover: clear any pseudo-hover flag
			_pseudoHover = false;
			// 拖拽时不允许 hover（被拖拽牌视为非 hover，其他牌也不触发 hover 动画）
			if (_activeDragging) { SetHoverIndexInternal(-1, false, false, true); }
			// 兜底：若没有在按、没有拖，但仍然残留活动目标，则清理
			if (!_pressing && !_activeDragging && _activePressed != null)
			{
				_activePressed = null;
			}

			// update mouse sample for next frame's speed calculation
			_lastMousePos = Input.mousePosition;
			_lastMouseTime = Time.unscaledTime;
		}

		private void UpdateHoverUnified()
		{
			if (_activeDragging) { SetHoverIndexInternal(-1, false, false, true); return; }
			var cam = Camera.main; if (cam == null) return;
			int newIdx = -1; bool fromBelow = false; bool pseudo = false;
			// 1) 物理射线命中顶层
			var ray = cam.ScreenPointToRay(Input.mousePosition);
			var hits = Physics.RaycastAll(ray, 1000f, interactLayerMask, QueryTriggerInteraction.Collide);
			if (hits != null && hits.Length > 0)
			{
				// Choose among all raycast hits the card whose world-top Y value is largest.
				CardView3D top = null; float bestTopY = float.NegativeInfinity;
				for (int i = 0; i < hits.Length; i++)
				{
					var cv = hits[i].collider != null ? hits[i].collider.GetComponentInParent<CardView3D>() : null;
					if (cv == null || cv.IsReturningHome) continue;
					float ty = GetCardWorldTopY(cv);
					if (ty > bestTopY) { bestTopY = ty; top = cv; }
				}
				if (top != null) { newIdx = top.handIndex; fromBelow = IsPointerFromBelow(top); }
			}
			// Strict raycast: no pseudo hover or screen-band fallback; if there's no ray hit, newIdx stays -1
			if (newIdx != _hoverIndex)
			{
				RequestHoverChange(newIdx, pseudo, fromBelow);
			}
			else if (_pseudoHover)
			{
				if (_hoverIndex >= 0 && !IsInPseudoArea(_hoverIndex) && !IsPointerOnTopOfCard(_hoverIndex))
				{
					RequestHoverChange(-1, false, false);
				}
			}
		}

		[Header("Table Coupling")]
		[Tooltip("启用后，手牌 Y 以桌面高度加偏移计算；关闭则保持自身 Y 平面")]
		public bool coupleToTable = false;
		[Tooltip("手牌相对于桌面的高度（米）")]
		public float handHeightAboveTable = 0.03f;

		private static float GetTableYOr(float fallback)
		{
			var t = GameObject.FindObjectOfType<TablePlane>(true);
			if (t != null) return t.SurfaceY;
			return fallback;
		}

		private static Vector3 WorldPointOnYPlaneByViewport(Camera cam, Vector2 viewport, float yPlane, Vector3 fallback)
		{
			Ray r = cam.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
			if (Mathf.Abs(r.direction.y) < 1e-6f) return fallback; // 平行，返回备用
			float t = (yPlane - r.origin.y) / r.direction.y;
			if (t < 0f) return fallback;
			return r.origin + r.direction * t;
		}

		private void DriveDragByRay()
		{
			if (_cards == null || _cards.Length == 0) return;
			var cam = Camera.main; if (cam == null) return;
			var ray = cam.ScreenPointToRay(Input.mousePosition);
			var hits = Physics.RaycastAll(ray, 1000f, interactLayerMask, QueryTriggerInteraction.Collide);
			// Choose among all raycast hits the card whose world-top Y value is largest.
			CardView3D top = null; float bestTopY = float.NegativeInfinity;
			for (int i = 0; i < hits.Length; i++)
			{
				var cv = hits[i].collider != null ? hits[i].collider.GetComponentInParent<CardView3D>() : null;
				if (cv == null || cv.IsReturningHome) continue;
				float ty = GetCardWorldTopY(cv);
				if (ty > bestTopY) { bestTopY = ty; top = cv; }
			}
			bool pointerOnAny = (top != null);

			// Press/drag threshold
			const float pressMoveThresholdPx = 6f; // 小点击不触发拖拽
			static Vector2 MP() => (Vector2)Input.mousePosition;
			if (!_pressing)
			{
				if (Input.GetMouseButtonDown(0))
				{
					_pressing = true;
					_pressStartPos = MP();
					_pressStartTime = Time.unscaledTime;
					_activePressed = pointerOnAny ? top : null;
					// 保持悬停可用：不清空 _hoverIndex，避免点击期间丢失 hover
				}
				return;
			}
			// 按住阶段
			if (Input.GetMouseButton(0))
			{
				if (_activePressed != null)
				{
					float moved = Vector2.Distance(MP(), _pressStartPos);
					bool longEnough = (Time.unscaledTime - _pressStartTime) >= Mathf.Max(0f, pressHoldThresholdSec);
					// 若被判定为“点击”（未达位移或时间阈值），则不允许转为拖拽
					if (!_activeDragging && moved >= pressMoveThresholdPx && longEnough)
					{
						_activeDragging = true;
						_activePressed.ExternalBeginDrag();
						_activePressed.ExternalDrag();
						_draggingCard = _activePressed;
						_draggingSlot = (_draggingCard != null) ? _draggingCard.slotIndex : -1;
						if (_draggingCard != null && _draggingSlot >= 0)
						{
							ReserveGapForCard(_draggingCard, _draggingSlot);
						}
						_compactOnDrag = false;
					}
					else if (_activeDragging)
					{
						_activePressed.ExternalDrag();
					}
				}
				return;
			}
			// 松开或未按：确保清理输入状态，避免残留导致后续无反馈
			if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
			{
				if (_activeDragging && _activePressed != null) _activePressed.ExternalEndDrag();
				_activePressed = null; _pressing = false; _activeDragging = false;
				// 停止并拢，给归位的卡保留原位
				_compactOnDrag = false; _draggingCard = null; _draggingSlot = -1;
				// 松开后允许悬停动画重新工作
				UpdateHoverByRay();
			}
		}

		// 供卡牌在完成归位后调用，重置输入状态以确保可再次交互
		public void ClearInputState()
		{
			_activePressed = null;
			_pressing = false;
			_activeDragging = false;
			_compactOnDrag = false; _draggingCard = null; _draggingSlot = -1;
			_compactAssignedSlot.Clear(); _origSlotIndex.Clear();
			_reservedGapCard = null; _reservedGapSlot = -1; _reservedGapInsertIndex = -1;
			_targetUnits.Clear();
		}

		private void UpdateHoverByRay()
		{
			// 正在拖拽时彻底禁止 hover
			if (_activeDragging) { SetHoverIndexInternal(-1, false, false, true); return; }
			var cam = Camera.main; if (cam == null) return;
			// 若正在按住鼠标并已有 hover 目标，则锁定该目标（但不再在这里驱动拖拽）
			if (Input.GetMouseButton(0) && _hoverIndex >= 0 && _cards != null && _hoverIndex < _cards.Length)
			{
				var lockCv = _cards[_hoverIndex]; if (lockCv != null) { return; }
			}
			var ray = cam.ScreenPointToRay(Input.mousePosition);
			var hits = Physics.RaycastAll(ray, 1000f, interactLayerMask, QueryTriggerInteraction.Collide);
			if (hits == null || hits.Length == 0)
			{
				// Strict raycast: if nothing is hit, clear hover immediately
				if (_hoverIndex != -1 && debugLogs) Debug.Log("[HandSplineZone] hoverIndex=-1 (no hits)");
				SetHoverIndexInternal(-1, false, false, true);
				return;
			}
			// Choose among all raycast hits the card whose world-top Y value is largest.
			CardView3D top = null; float bestTopY = float.NegativeInfinity;
			for (int i = 0; i < hits.Length; i++)
			{
				var cv = hits[i].collider != null ? hits[i].collider.GetComponentInParent<CardView3D>() : null;
				if (cv == null) continue;
				float ty = GetCardWorldTopY(cv);
				if (ty > bestTopY) { bestTopY = ty; top = cv; }
			}
			if (top == null) { if (_hoverIndex != -1 && debugLogs) Debug.Log("[HandSplineZone] hoverIndex=-1 (no top)"); return; }
			if (top.IsReturningHome) { RequestHoverChange(-1, false, false); return; }
			// Strict raycast: do not apply any visual-projection override here
			int newIdx = top.handIndex;
			if (newIdx != _hoverIndex)
			{
				if (debugLogs)
				{
					string n = top != null ? top.name : "null";
					Debug.Log($"[HandSplineZone] hoverIndex { _hoverIndex } -> { newIdx } ({ n })");
				}
				bool fromBelow = false;
				try
				{
					fromBelow = IsPointerFromBelow(top);
				}
				catch { }
				RequestHoverChange(newIdx, false, fromBelow);
				try
				{
					if (fromBelow) _enterMinYByIndex[_hoverIndex] = GetCardScreenMinY(top);
				}
				catch { }
			}
		}

        public void RegisterCards(CardView3D[] cards)
        {
            _cards = cards ?? new CardView3D[0];
            for (int i = 0; i < _cards.Length; i++) if (_cards[i] != null) _cards[i].handIndex = i;
            ForceRelayoutExistingCards();
        }

		public void UnregisterCard(CardView3D card)
		{
			if (card == null) return;
			
			// 从卡牌数组中移除
			CancelRepositionAnimation(card);
			if (_cards != null)
			{
				var newCards = new List<CardView3D>(_cards.Length);
				for (int i = 0; i < _cards.Length; i++)
				{
					var existing = _cards[i];
					if (existing == null || existing == card) continue;
					newCards.Add(existing);
				}
				_cards = newCards.ToArray();
			}
			
			// 清理卡牌的手牌相关状态
			if (card.handIndex >= 0)
			{
                card.handIndex = -1;
                card.slotIndex = -1;
            }
            
            // 如果这张卡是当前悬停的卡，清除悬停状态
			if (_hoverIndex >= 0 && _cards != null && _hoverIndex < _cards.Length && _cards[_hoverIndex] == card)
			{
				SetHoverIndexInternal(-1, false, false, true);
			}
            
            // 如果这张卡是当前按下的卡，清除按下状态
            if (_activePressed == card)
            {
                _activePressed = null;
                _pressing = false;
                _activeDragging = false;
            }
            
			// 如果这张卡是当前拖拽的卡，清除拖拽状态
			if (_draggingCard == card)
			{
				_draggingCard = null;
				_draggingSlot = -1;
				_compactOnDrag = false;
			}
			if (_reservedGapCard == card)
			{
				_reservedGapCard = null;
				_reservedGapSlot = -1;
				_reservedGapInsertIndex = -1;
			}
			
			// 从压缩映射中移除
			_origSlotIndex.Remove(card);
			_compactAssignedSlot.Remove(card);
			_targetUnits.Remove(card);
			
			Debug.Log($"[HandSplineZone] 卡牌已从手牌区域注销: {card.name}");
			
			// 重新初始化剩余卡牌的索引
			for (int i = 0; i < _cards.Length; i++)
			{
				if (_cards[i] != null)
				{
					_cards[i].handIndex = i;
				}
			}

			RealignCards(null, true);
		}

		public void ForceRelayoutExistingCards()
		{
			// Rebuild the internal _cards array from child CardView3D components
			var children = GetComponentsInChildren<CardView3D>(true) ?? new CardView3D[0];
			_cards = children;
			for (int i = 0; i < _cards.Length; i++) if (_cards[i] != null) _cards[i].handIndex = i;
			// Recompute poses and optionally animate existing cards back into place
			RealignCards(null, true);
		}

		public bool NotifyHover(CardView3D card, bool enter)
		{
			if (card == null) return false;
			if (_cards == null || _cards.Length == 0)
			{
				var childCards = GetComponentsInChildren<CardView3D>(true);
				if (childCards != null && childCards.Length > 0) RegisterCards(childCards); else return false;
			}
			if (card.handIndex < 0 || card.handIndex >= _cards.Length) return false;

			if (enter)
			{
				// immediate when explicitly notified
				SetHoverIndexInternal(card.handIndex, false, IsPointerFromBelow(card), true);
				try { _enterMinYByIndex[_hoverIndex] = GetCardScreenMinY(card); } catch {}
			}
			else
			{
				// 若不是从下方进入，按常规退出；
				// 若是从下方进入，交由 LateUpdate 的可视判定维持，不立即清空
				if (!_hoverFromBelow && _hoverIndex == card.handIndex)
					SetHoverIndexInternal(-1, false, false, true);
			}
			if (_hoverCo == null) _hoverCo = StartCoroutine(RepositionByHover());
			return true;
		}

		private float GetCardScreenMinY(CardView3D card)
		{
			var cam = Camera.main; if (cam == null || card == null) return 0f;
			var col = card.GetComponentInChildren<Collider>(); if (col == null) return 0f;
			var b = col.bounds; Vector3 c=b.center,e=b.extents;
			float minY=float.PositiveInfinity;
			Vector3[] corners = new Vector3[8]
			{
				new Vector3(c.x-e.x, c.y-e.y, c.z-e.z),
				new Vector3(c.x-e.x, c.y-e.y, c.z+e.z),
				new Vector3(c.x-e.x, c.y+e.y, c.z-e.z),
				new Vector3(c.x-e.x, c.y+e.y, c.z+e.z),
				new Vector3(c.x+e.x, c.y-e.y, c.z-e.z),
				new Vector3(c.x+e.x, c.y-e.y, c.z+e.z),
				new Vector3(c.x+e.x, c.y+e.y, c.z-e.z),
				new Vector3(c.x+e.x, c.y+e.y, c.z+e.z)
			};
			for (int i=0;i<8;i++)
			{
				var sp = cam.WorldToScreenPoint(corners[i]);
				minY = Mathf.Min(minY, sp.y);
			}
			return minY;
		}

		// Find the visually topmost card under the given screen position.
		// We project each card's bounds into screen space and test containment of the pointer.
		// Return the card whose bounds contain the pointer and whose camera-space depth is smallest (closest to camera).
		private CardView3D FindTopCardUnderPointer(Camera cam, Vector2 screenPos)
		{
			if (cam == null || _cards == null || _cards.Length == 0) return null;
			CardView3D best = null;
			float bestZ = float.PositiveInfinity;
			for (int i = 0; i < _cards.Length; i++)
			{
				var c = _cards[i]; if (c == null) continue;
				if (c.IsReturningHome) continue;
				// Skip dragging card
				if (_activeDragging && _draggingCard != null && c == _draggingCard) continue;
				// Use renderer bounds if available
				Renderer r = c.MainRenderer != null ? c.MainRenderer : c.GetComponentInChildren<Renderer>(true);
				if (r == null) continue;
				var b = r.bounds;
				// project 8 corners
				Vector3[] corners = new Vector3[8]
				{
					new Vector3(b.center.x-b.extents.x, b.center.y-b.extents.y, b.center.z-b.extents.z),
					new Vector3(b.center.x-b.extents.x, b.center.y-b.extents.y, b.center.z+b.extents.z),
					new Vector3(b.center.x-b.extents.x, b.center.y+b.extents.y, b.center.z-b.extents.z),
					new Vector3(b.center.x-b.extents.x, b.center.y+b.extents.y, b.center.z+b.extents.z),
					new Vector3(b.center.x+b.extents.x, b.center.y-b.extents.y, b.center.z-b.extents.z),
					new Vector3(b.center.x+b.extents.x, b.center.y-b.extents.y, b.center.z+b.extents.z),
					new Vector3(b.center.x+b.extents.x, b.center.y+b.extents.y, b.center.z-b.extents.z),
					new Vector3(b.center.x+b.extents.x, b.center.y+b.extents.y, b.center.z+b.extents.z)
				};
				float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
				for (int k = 0; k < 8; k++)
				{
					var sp = cam.WorldToScreenPoint(corners[k]);
					minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
					minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
				}
				if (screenPos.x >= minX && screenPos.x <= maxX && screenPos.y >= minY && screenPos.y <= maxY)
				{
					// compute camera-space depth at the pointer location (approx) and prefer the closest
					float z = GetBoundsScreenZAtPosition(b, cam, screenPos);
					if (z < bestZ)
					{
						bestZ = z; best = c;
					}
				}
			}
			return best;
		}

		// Try to set the hover index, enforcing a short cooldown to avoid rapid oscillation.
		// If force==true, bypass cooldown (used for immediate clears such as entering drag).
		private bool SetHoverIndexInternal(int newIdx, bool pseudo = false, bool fromBelow = false, bool force = false)
		{
			float now = Time.unscaledTime;
			if (!force && newIdx != _hoverIndex && now - _lastHoverChangeTime < hoverChangeCooldown)
			{
				// ignore rapid changes
				return false;
			}

			// Apply the hover change (update state, start reposition coroutine if needed)
			int prev = _hoverIndex;
			_hoverIndex = newIdx;
			_pseudoHover = pseudo;
			_hoverFromBelow = fromBelow;
			_lastHoverChangeTime = now;

			// record enter baseline for the newly hovered card
			if (_hoverIndex >= 0 && _cards != null && _hoverIndex < _cards.Length)
			{
				try { _enterMinYByIndex[_hoverIndex] = GetCardScreenMinY(_cards[_hoverIndex]); } catch { }
			}

			// ensure the RepositionByHover coroutine is running to animate the change
			if (_hoverCo == null) _hoverCo = StartCoroutine(RepositionByHover());
			return true;
		}

		// Request a hover change but require the candidate to be stable for hoverStableTime
		// to avoid rapid flipping when the pointer sits between overlapping cards.
		private void RequestHoverChange(int newIdx, bool pseudo = false, bool fromBelow = false, bool force = false)
		{
			// compute instantaneous pointer speed since previous stored sample
			float now = Time.unscaledTime;
			Vector2 cur = Input.mousePosition;
			float dt = now - _lastMouseTime;
			float speed = 0f;
			if (dt > 1e-6f) speed = Vector2.Distance(cur, _lastMousePos) / dt;

			if (force)
			{
				// immediate
				_pendingHoverIndex = -2;
				_pendingHoverStartTime = -999f;
				SetHoverIndexInternal(newIdx, pseudo, fromBelow, true);
				return;
			}
			// if the pointer is moving fast and immediate-pass is allowed, commit immediately
			if (allowImmediateHoverOnFastPass && newIdx != _hoverIndex && speed >= Mathf.Max(0f, hoverImmediateSpeed))
			{
				_pendingHoverIndex = -2;
				_pendingHoverStartTime = -999f;
				SetHoverIndexInternal(newIdx, pseudo, fromBelow, true);
				return;
			}
			// if already hovered, clear pending
			if (newIdx == _hoverIndex)
			{
				_pendingHoverIndex = -2;
				_pendingHoverStartTime = -999f;
				return;
			}
			// same candidate continues -> commit if stable long enough
			if (newIdx == _pendingHoverIndex)
			{
				if (now - _pendingHoverStartTime >= Mathf.Max(0f, hoverStableTime))
				{
					_pendingHoverIndex = -2;
					_pendingHoverStartTime = -999f;
					// force to bypass SetHoverIndexInternal's cooldown here
					SetHoverIndexInternal(newIdx, pseudo, fromBelow, true);
				}
				return;
			}
			// new pending candidate
			_pendingHoverIndex = newIdx;
			_pendingHoverStartTime = now;
		}

			// Return the minimal camera-space screen Z among the 8 corners of the bounds.
			// If a corner projects behind the camera (z<=0), we use distance to camera as a fallback.
			private float GetBoundsMinScreenZ(Bounds b, Camera cam)
			{
				float minZ = float.PositiveInfinity;
				Vector3[] corners = new Vector3[8]
				{
					new Vector3(b.center.x-b.extents.x, b.center.y-b.extents.y, b.center.z-b.extents.z),
					new Vector3(b.center.x-b.extents.x, b.center.y-b.extents.y, b.center.z+b.extents.z),
					new Vector3(b.center.x-b.extents.x, b.center.y+b.extents.y, b.center.z-b.extents.z),
					new Vector3(b.center.x-b.extents.x, b.center.y+b.extents.y, b.center.z+b.extents.z),
					new Vector3(b.center.x+b.extents.x, b.center.y-b.extents.y, b.center.z-b.extents.z),
					new Vector3(b.center.x+b.extents.x, b.center.y-b.extents.y, b.center.z+b.extents.z),
					new Vector3(b.center.x+b.extents.x, b.center.y+b.extents.y, b.center.z-b.extents.z),
					new Vector3(b.center.x+b.extents.x, b.center.y+b.extents.y, b.center.z+b.extents.z)
				};
				for (int k = 0; k < 8; k++)
				{
					var sp = cam.WorldToScreenPoint(corners[k]);
					if (sp.z > 0f) minZ = Mathf.Min(minZ, sp.z);
					else minZ = Mathf.Min(minZ, Vector3.Distance(cam.transform.position, corners[k]));
				}
				return minZ;
			}

		private bool IsPointerFromBelow(CardView3D card)
		{
			float minY = GetCardScreenMinY(card);
			return Input.mousePosition.y <= (minY + enterFromBelowSlackPx);
		}

		// Estimate the camera-space screen Z at the given screen position by projecting the bounds' corners
		// and choosing the corner whose projected XY is closest to screenPos. This is a cheap proxy for
		// the depth at the pointer location and is more robust than taking the minimal corner Z alone.
		private float GetBoundsScreenZAtPosition(Bounds b, Camera cam, Vector2 screenPos)
		{
			float bestDistSq = float.PositiveInfinity;
			float bestZ = float.PositiveInfinity;
			Vector3[] corners = new Vector3[8]
			{
				new Vector3(b.center.x-b.extents.x, b.center.y-b.extents.y, b.center.z-b.extents.z),
				new Vector3(b.center.x-b.extents.x, b.center.y-b.extents.y, b.center.z+b.extents.z),
				new Vector3(b.center.x-b.extents.x, b.center.y+b.extents.y, b.center.z-b.extents.z),
				new Vector3(b.center.x-b.extents.x, b.center.y+b.extents.y, b.center.z+b.extents.z),
				new Vector3(b.center.x+b.extents.x, b.center.y-b.extents.y, b.center.z-b.extents.z),
				new Vector3(b.center.x+b.extents.x, b.center.y-b.extents.y, b.center.z+b.extents.z),
				new Vector3(b.center.x+b.extents.x, b.center.y+b.extents.y, b.center.z-b.extents.z),
				new Vector3(b.center.x+b.extents.x, b.center.y+b.extents.y, b.center.z+b.extents.z)
			};
			for (int k = 0; k < 8; k++)
			{
				var sp = cam.WorldToScreenPoint(corners[k]);
				float dx = sp.x - screenPos.x; float dy = sp.y - screenPos.y;
				float d2 = dx*dx + dy*dy;
				if (d2 < bestDistSq)
				{
					bestDistSq = d2;
					if (sp.z > 0f) bestZ = sp.z; else bestZ = Mathf.Min(bestZ, Vector3.Distance(cam.transform.position, corners[k]));
				}
			}
			return bestZ;
		}

		// Return the top-most world Y for a card for tie-breaking among raycast hits.
		// Prefer renderer.bounds.max.y when possible, otherwise fall back to transform.position.y.
		private float GetCardWorldTopY(CardView3D c)
		{
			if (c == null) return float.NegativeInfinity;
			try
			{
				Renderer r = c.MainRenderer != null ? c.MainRenderer : c.GetComponentInChildren<Renderer>(true);
				if (r != null) return r.bounds.max.y;
			}
			catch {}
			return c.transform.position.y;
		}



		private System.Collections.IEnumerator RepositionByHover()
		{
			float durX = Mathf.Clamp(hoverXLerp, 0.01f, 1f);
			float durY = Mathf.Clamp(hoverXLerp, 0.01f, 1f);
			float durZR = Mathf.Clamp(hoverZLerp, 0.01f, 1f);
			float durZL = Mathf.Clamp(hoverZLeftLerp, 0.01f, 1f);
			float px = 0f, py = 0f, pzR = 0f, pzL = 0f;
			float desiredPx = 0f, desiredPy = 0f, desiredPzR = 0f, desiredPzL = 0f;
			int lastHoverIndex = -1;
			var vels = new System.Collections.Generic.Dictionary<CardView3D, Vector3>(_cards.Length);
			while (true)
			{
				durX = Mathf.Clamp(hoverXLerp, 0.01f, 1f);
				durY = Mathf.Clamp(hoverXLerp, 0.01f, 1f);
				durZR = Mathf.Clamp(hoverZLerp, 0.01f, 1f);
				durZL = Mathf.Clamp(hoverZLeftLerp, 0.01f, 1f);
				bool hoverChangedThisFrame = (_hoverIndex != lastHoverIndex);
				desiredPx = (_hoverIndex >= 0) ? 1f : 0f;
				desiredPy = desiredPx;
				desiredPzR = desiredPx;
				desiredPzL = desiredPx;
				if (hoverChangedThisFrame)
				{
					px = desiredPx;
					py = desiredPy;
					pzR = desiredPzR;
					pzL = desiredPzL;
					lastHoverIndex = _hoverIndex;
				}
				else
				{
					px = Mathf.MoveTowards(px, desiredPx, Time.deltaTime / durX);
					py = Mathf.MoveTowards(py, desiredPy, Time.deltaTime / durY);
					pzR = Mathf.MoveTowards(pzR, desiredPzR, Time.deltaTime / durZR);
					pzL = Mathf.MoveTowards(pzL, desiredPzL, Time.deltaTime / durZL);
				}

				Vector3 dirW = transform.TransformDirection(lineLocalDirection);
				Vector3 lineXZ = new Vector3(dirW.x, 0f, dirW.z);
				if (lineXZ.sqrMagnitude < 1e-6f) lineXZ = Vector3.right;
				lineXZ.Normalize();
				Vector3 liftDir = Vector3.forward; // 保证 hover 卡牌只沿世界 Z+ 方向运动

			if (hoverChangedThisFrame) vels.Clear();
			for (int i = 0; i < _cards.Length; i++)
			{
				var c = _cards[i]; if (c == null) continue;
				bool isHoveredNow = (i == _hoverIndex);
				try { c.SetInfoVisible(isHoveredNow); c.ApplyHoverColliderExtend(isHoveredNow); } catch {}
				if (c.IsReturningHome) continue;
				if (_repositionCos.ContainsKey(c)) continue;
				// 被拖拽的牌：完全从自动布局中排除
				if (_activeDragging && _draggingCard != null && c == _draggingCard) continue;
				float units;
				if (!_targetUnits.TryGetValue(c, out units))
				{
					if (_cards[i].slotIndex >= 0)
						units = _cards[i].slotIndex - (slots - 1) * 0.5f;
					else
						units = i - (_cards.Length - 1) * 0.5f;
				}
				int approxSlot = Mathf.Clamp(Mathf.RoundToInt(units + (slots - 1) * 0.5f), 0, Mathf.Max(0, slots - 1));
				Vector3 p; Quaternion r;
				if (!TryGetPoseByUnits(units, out p, out r))
				{
					TryGetSlotPose(approxSlot, out p, out r);
				}
				Vector3 baseTarget = p;
				Vector3 target = baseTarget; Quaternion rot = r;
					// 并拢排序：当有卡被拖出时，其余卡被重新连续编号到中心附近的槽位
					if (_compactOnDrag && (_draggingCard == null || c != _draggingCard))
					{
						if (_compactAssignedSlot.TryGetValue(c, out int assigned))
						{
							Vector3 p2; Quaternion r2; TryGetSlotPose(assigned, out p2, out r2);
							target = p2; rot = r2;
							approxSlot = assigned;
						}
					}
					if (_hoverIndex >= 0)
                    {
								// Use array index relative to the hovered card when available. This is more reliable
								// for deciding which neighbor should slide aside (left/right) because _cards is
								// maintained in hand-order and 'i' is the current card's hand index.
								bool isLeft = false, isRight = false;
								if (_hoverIndex >= 0 && _hoverIndex < _cards.Length)
								{
									isLeft = (i < _hoverIndex);
									isRight = (i > _hoverIndex);
								}
								else
								{
									int hoveredSlot = (_cards[_hoverIndex].slotIndex >= 0) ? _cards[_hoverIndex].slotIndex : _hoverIndex;
									isLeft = approxSlot < hoveredSlot;
									isRight = approxSlot > hoveredSlot;
								}
                        // 把原本的线性/MoveTowards 数值通过 Inspector 曲线映射为最终 local factor（0..1）
                        float mappedPX = (hoverCardMoveCurve != null && otherCardsMoveCurve != null) ? 
                            ((i == _hoverIndex) ? hoverCardMoveCurve.Evaluate(px) : otherCardsMoveCurve.Evaluate(px)) : px;
                        float mappedPY = (hoverCardMoveCurve != null && otherCardsMoveCurve != null) ? 
                            ((i == _hoverIndex) ? hoverCardMoveCurve.Evaluate(py) : otherCardsMoveCurve.Evaluate(py)) : py;
                        float mappedPzR = (hoverCardMoveCurve != null && otherCardsMoveCurve != null) ? 
                            ((i == _hoverIndex) ? hoverCardMoveCurve.Evaluate(pzR) : otherCardsMoveCurve.Evaluate(pzR)) : pzR;
                        float mappedPzL = (hoverCardMoveCurve != null && otherCardsMoveCurve != null) ? 
                            ((i == _hoverIndex) ? hoverCardMoveCurve.Evaluate(pzL) : otherCardsMoveCurve.Evaluate(pzL)) : pzL;

                        if (isRight)
                        {
                            var cam = Camera.main;
                            if (cam != null && Mathf.Abs(hoverZ) > 1e-6f)
                            {
                                Vector3 rightScr = cam.transform.right; rightScr.y = 0f; rightScr.Normalize();
                                target += rightScr * (hoverZ * mappedPzR);
                            }
                        }
                        else if (isLeft)
                        {
                            var cam = Camera.main;
                            if (cam != null && Mathf.Abs(hoverZLeft) > 1e-6f)
                            {
                                Vector3 rightScr = cam.transform.right; rightScr.y = 0f; rightScr.Normalize();
                                target += (-rightScr) * (hoverZLeft * mappedPzL);
                            }
                        }
                        if (i == _hoverIndex)
                        {
                            Vector3 lifted = baseTarget + liftDir * (hoverX * mappedPY);
                            lifted.x = baseTarget.x; // hover 仅允许向上（Z+）移动，保持左右位置不变
                            target = lifted;
                        }
                    }
					if (!vels.TryGetValue(c, out var vel)) vel = Vector3.zero;
					Vector3 smoothed = Vector3.SmoothDamp(c.transform.position, target, ref vel, 0.06f);
					smoothed.y = target.y;
					vel.y = 0f;
					c.transform.position = smoothed;
					vels[c] = vel;
					c.transform.rotation = Quaternion.Slerp(c.transform.rotation, rot, Time.deltaTime / Mathf.Max(0.01f, hoverXLerp));
				}
				yield return null;
			}
		}

		public bool TryGetSlotPose(int index, out Vector3 pos, out Quaternion rot)
		{
			pos = transform.position; rot = transform.rotation;
			if (slots <= 0) return false;
			float center = (slots - 1) * 0.5f;
			float idxOffset = index - center;
			Vector3 dirL = lineLocalDirection.sqrMagnitude < 1e-6f ? Vector3.right : lineLocalDirection.normalized;
			Vector3 local = dirL * (idxOffset * lineSpacing);
			local.y = lineLocalY;
			Vector3 pWorld = transform.TransformPoint(local);
			Vector3 fwdW = transform.TransformDirection(dirL);
			Vector3 fwdXZ = new Vector3(fwdW.x, 0f, fwdW.z);
			if (fwdXZ.sqrMagnitude < 1e-6f) fwdXZ = transform.forward;
			fwdXZ.Normalize();
			float yaw = 0f;
			if (yawAlignToLineDirection)
			{
				yaw = Mathf.Atan2(fwdXZ.x, fwdXZ.z) * Mathf.Rad2Deg;
				if (flipForward) yaw += 180f;
			}
			Vector3 baseEuler = useFixedCardEuler ? fixedCardEuler : new Vector3(90f, 0f, 0f);
			rot = Quaternion.Euler(baseEuler.x, baseEuler.y + yaw + yawAdjustDeg + lineYawOffsetDeg, baseEuler.z);
			pos = pWorld + Vector3.up * (offsetUp + shadowLiftY) + fwdXZ * offsetForward;
			// By default, when the hand is in a static arrangement (no hover and not actively dragging),
			// keep all cards at the same Z so they visually sit on the same plane. When hovering or
			// dragging, honor the depth stacking so hovered/affected cards can be rendered slightly
			// in front/back by slot order.
			if (stackDepthByIndex && Camera.main != null && slots > 0)
			{
				bool applyDepth = false;
				try { applyDepth = (_hoverIndex >= 0) || _activeDragging; } catch { applyDepth = false; }
				int order = reverseDepthOrder ? (slots - 1 - index) : index;
				if (applyDepth)
				{
					// interactive: use camera-forward Z stacking so hovered/dragged cards can pop forward
					pos += -Camera.main.transform.forward * (order * depthPerSlot);
				}
				else
				{
					// idle: keep Z identical but stagger slightly in Y so render order and occlusion remain consistent
					pos += Vector3.up * (order * verticalDepthPerSlot);
				}
			}
			return true;
		}

		public bool TryGetPoseByUnits(float units, out Vector3 pos, out Quaternion rot)
		{
			if (slots <= 0)
			{
				pos = transform.position;
				rot = transform.rotation;
				return false;
			}
			EvaluatePoseForUnits(units, out pos, out rot, out _);
			return true;
		}

		public bool AssignCardToSlot(CardView3D card, int index)
		{
			if (card == null) return false;
			if (!TryGetSlotPose(index, out var p, out var r)) return false;
			card.SnapTo(p, r);
			card.SetHomeFromZone(transform, p, r);
			if (_cards != null)
			{
				for (int i = 0; i < _cards.Length; i++) if (_cards[i] == card) { card.handIndex = i; break; }
			}
			card.slotIndex = index;
			return true;
		}

		public bool AssignCardAtUnits(CardView3D card, float units)
		{
			if (card == null) return false;
			if (!TryGetPoseByUnits(units, out var p, out var r)) return false;
			card.SnapTo(p, r);
			card.SetHomeFromZone(transform, p, r);
			return true;
		}

		public Vector3 GetSlotWorldPosition(int index)
		{
			if (TryGetSlotPose(Mathf.Clamp(index, 0, Mathf.Max(0, slots - 1)), out var p, out _)) return p;
			return transform.position;
		}

		public Quaternion GetSlotWorldRotation(int index)
		{
			if (TryGetSlotPose(Mathf.Clamp(index, 0, Mathf.Max(0, slots - 1)), out _, out var r)) return r;
			return transform.rotation;
		}

		public int GetActiveCardCount()
		{
			if (_cards == null) return 0;
			int count = 0;
			for (int i = 0; i < _cards.Length; i++)
			{
				if (_cards[i] != null && _cards[i].handIndex >= 0) count++;
			}
			return count;
		}

		public void RegisterCard(CardView3D card)
		{
			if (card == null) return;
			var list = new List<CardView3D>();
			if (_cards != null)
			{
				for (int i = 0; i < _cards.Length; i++)
				{
					var existing = _cards[i];
					if (existing != null && existing != card) list.Add(existing);
				}
			}
			if (!list.Contains(card)) list.Add(card);
			_cards = list.ToArray();
			for (int i = 0; i < _cards.Length; i++)
			{
				if (_cards[i] != null) _cards[i].handIndex = i;
			}
		}

		private void EvaluatePoseForUnits(float units, out Vector3 pos, out Quaternion rot, out int slotIndex)
		{
			pos = transform.position;
			rot = transform.rotation;
			slotIndex = Mathf.Clamp((slots - 1) / 2, 0, Mathf.Max(0, slots - 1));
			if (slots <= 0) return;
			float center = (slots - 1) * 0.5f;
			float slotF = units + center;
			int maxSlot = Mathf.Max(0, slots - 1);
			int baseSlot = Mathf.Clamp(Mathf.FloorToInt(slotF), 0, maxSlot);
			int nextSlot = Mathf.Clamp(baseSlot + 1, 0, maxSlot);
			float t = Mathf.Clamp01(slotF - baseSlot);
			Vector3 posA, posB; Quaternion rotA, rotB;
			if (!TryGetSlotPose(baseSlot, out posA, out rotA))
			{
				posA = transform.position;
				rotA = transform.rotation;
			}
			if (!TryGetSlotPose(nextSlot, out posB, out rotB))
			{
				posB = posA;
				rotB = rotA;
			}
			pos = Vector3.LerpUnclamped(posA, posB, t);
			rot = Quaternion.Slerp(rotA, rotB, t);
			float bias = units < 0f ? -1e-3f : (units > 0f ? 1e-3f : 0f);
			slotIndex = Mathf.Clamp(Mathf.RoundToInt(slotF + bias), 0, maxSlot);
		}

		public void RealignCards(CardView3D newlyAdded = null, bool repositionExisting = true)
		{
			if (_cards == null || _cards.Length == 0) return;

			// When a card is being inserted (newlyAdded != null) we may apply a small
			// vertical offset to its immediate neighbors so the returning card can
			// visually slot between an elevated left and lowered right neighbor.
			// This offset only affects the temporary animation target Y and does not
			// change logical slot indices.
			float neighborReturnYOffset = this.returnNeighborYOffset;
			var active = new List<CardView3D>();
			for (int i = 0; i < _cards.Length; i++)
			{
				if (_cards[i] != null) active.Add(_cards[i]);
			}
			if (active.Count == 0) return;
			active.Sort((a, b) =>
			{
				int orderA = a.handIndex >= 0 ? a.handIndex : a.createId;
				int orderB = b.handIndex >= 0 ? b.handIndex : b.createId;
				return orderA.CompareTo(orderB);
			});

			_targetUnits.Clear();
			float startUnit = -((active.Count - 1) * 0.5f) * _baseSpacingUnits;
			int newlyAddedIndex = -1;
			if (newlyAdded != null) newlyAddedIndex = active.FindIndex(c => c == newlyAdded);
			for (int i = 0; i < active.Count; i++)
			{
				var card = active[i];
				float units = startUnit + i * _baseSpacingUnits;
				EvaluatePoseForUnits(units, out Vector3 pos, out Quaternion rot, out int slotIndex);
				card.slotIndex = slotIndex;
				// Set the card's canonical home pose (do NOT include neighbor animation offsets here)
				card.SetHomeFromZone(transform, pos, rot);
				card.handIndex = i;
				_targetUnits[card] = units;
				try { card.transform.SetSiblingIndex(i); } catch {}
				try
				{
					// Ensure the newly-added (inserted) card gets a temporary render-order boost
					// so it won't be intermittently occluded during the insertion/phase1 animation.
					// Do NOT use card.IsReturningHome here because that can remain true during
					// phase2 and cause the card to occlude neighbors incorrectly. Only boost
					// when RealignCards is called with a freshly inserted card reference.
					bool isReturningVisual = (newlyAdded != null && card == newlyAdded);
					int baseOrder = i * _renderOrderStep;
					if (isReturningVisual)
					{
						int boost = Mathf.Max(_renderOrderStep * (active.Count), 1) + 100;
						card.ApplyHandRenderOrder(baseOrder + boost);
					}
					else
					{
						card.ApplyHandRenderOrder(baseOrder);
					}
				} catch {}
				if (!repositionExisting) continue;
				if (card == newlyAdded) continue;
				if (card == _draggingCard) continue;
				if (card.IsReturningHome) continue;
				// Compute animation target separately so we can apply a temporary Y offset to immediate neighbors
				Vector3 animPos = pos;
				if (newlyAddedIndex >= 0)
				{
					if (i == newlyAddedIndex - 1)
					{
						// left neighbor: raise up
						animPos.y += neighborReturnYOffset;
					}
					else if (i == newlyAddedIndex + 1)
					{
						// right neighbor: push down
						animPos.y -= neighborReturnYOffset;
					}
				}
				AnimateCardToSlot(card, animPos, rot, true);
			}
			_cards = active.ToArray();

		}

        

		public void ApplyTwoPhaseHome(CardView3D card, out Vector3 targetPos, out Quaternion targetRot)
		{
			targetPos = transform.position;
			targetRot = transform.rotation;
			if (card == null) return;
			if (!_targetUnits.TryGetValue(card, out float units))
			{
				int count = (_cards != null) ? _cards.Length : 0;
				if (count <= 0)
				{
					count = Mathf.Max(1, (card.handIndex >= 0) ? card.handIndex + 1 : 1);
				}
				float start = -((count - 1) * 0.5f) * _baseSpacingUnits;
				int index = (card.handIndex >= 0) ? Mathf.Clamp(card.handIndex, 0, count - 1) : (count - 1);
				units = start + index * _baseSpacingUnits;
			}
			EvaluatePoseForUnits(units, out targetPos, out targetRot, out int slotIndex);
			card.SetHomeFromZone(transform, targetPos, targetRot);
			card.slotIndex = slotIndex;
			_targetUnits[card] = units;
			int siblingIndex = (_cards != null) ? System.Array.IndexOf(_cards, card) : slotIndex;
			if (siblingIndex < 0) siblingIndex = slotIndex;
			try { card.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, Mathf.Max(0, (_cards?.Length ?? 0) - 1))); } catch {}
			int renderIndex = (card.handIndex >= 0) ? card.handIndex : siblingIndex;
			try { card.ApplyHandRenderOrder(renderIndex * _renderOrderStep); } catch {}
		}

		private void ReserveGapForCard(CardView3D card, int originalSlot)
		{
			if (card == null) return;
			if (_reservedGapCard == card) return;
			if (_cards == null || _cards.Length == 0) return;
			CancelRepositionAnimation(card);

			var list = new List<CardView3D>(_cards.Length);
			_reservedGapInsertIndex = -1;
			for (int i = 0; i < _cards.Length; i++)
			{
				var current = _cards[i];
				if (current == null) continue;
				if (current == card)
				{
					_reservedGapInsertIndex = list.Count;
					continue;
				}
				list.Add(current);
			}

			_cards = list.ToArray();
			for (int i = 0; i < _cards.Length; i++)
			{
				if (_cards[i] != null) _cards[i].handIndex = i;
			}

			_reservedGapCard = card;
			_reservedGapSlot = originalSlot;
			// If we couldn't locate the card in the current _cards array (e.g. the card
			// hasn't been fully registered yet), map the original slot index to an
			// insertion index by finding the first existing card whose slotIndex is
			// greater-than-or-equal to originalSlot. This avoids clamping raw slot
			// indices to the array length which would push the gap to the far right.
			if (_reservedGapInsertIndex < 0)
			{
				int insertIdx = _cards.Length;
				if (originalSlot >= 0)
				{
					for (int i = 0; i < _cards.Length; i++)
					{
						var c = _cards[i];
						if (c == null) continue;
						if (c.slotIndex >= originalSlot)
						{
							insertIdx = i;
							break;
						}
					}
				}
				_reservedGapInsertIndex = Mathf.Clamp(insertIdx, 0, Mathf.Max(0, _cards.Length));
			}

			card.handIndex = -1;
			card.slotIndex = originalSlot;
			_compactOnDrag = false;
			_origSlotIndex.Clear();
			_compactAssignedSlot.Clear();
			_targetUnits.Remove(card);

			RealignCards(null, true);
		}

		private void RestoreGapForCard(CardView3D card, bool animateReturning)
		{
			if (card == null) return;
			CancelRepositionAnimation(card);

			var list = new List<CardView3D>();
			if (_cards != null && _cards.Length > 0)
			{
				for (int i = 0; i < _cards.Length; i++)
				{
					var current = _cards[i];
					if (current != null && current != card) list.Add(current);
				}
			}

			int insertIndex = list.Count;
			if (_reservedGapCard == card && _reservedGapInsertIndex >= 0)
				insertIndex = Mathf.Clamp(_reservedGapInsertIndex, 0, list.Count);
			else if (_reservedGapSlot >= 0)
				insertIndex = Mathf.Clamp(_reservedGapSlot, 0, list.Count);
			else if (card.slotIndex >= 0)
				insertIndex = Mathf.Clamp(card.slotIndex, 0, list.Count);

			if (!list.Contains(card))
			{
				insertIndex = Mathf.Clamp(insertIndex, 0, list.Count);
				// If we are animating the return and there's a reserved gap for this card,
				// defer the actual insertion until after phase1 so Y is already correct
				// and sibling/render order will be correct when we insert.
				if (!(animateReturning && _reservedGapCard == card && _reservedGapInsertIndex >= 0))
				{
					list.Insert(insertIndex, card);
				}
			}

			_cards = list.ToArray();
			for (int i = 0; i < _cards.Length; i++)
			{
				if (_cards[i] != null) _cards[i].handIndex = i;
			}

			if (animateReturning && _reservedGapCard == card && _reservedGapInsertIndex >= 0)
			{
				// Insert the card immediately so sibling index / render order and neighbor
				// animation targets happen during phase1. This ensures by the time
				// phase1 ends, all layout and Y adjustments are completed.
				insertIndex = Mathf.Clamp(_reservedGapInsertIndex, 0, list.Count);
				if (!list.Contains(card)) list.Insert(insertIndex, card);
				_cards = list.ToArray();
				for (int i = 0; i < _cards.Length; i++) if (_cards[i] != null) _cards[i].handIndex = i;
				// clear reserved state now that we've inserted
				_reservedGapCard = null;
				_reservedGapSlot = -1;
				_reservedGapInsertIndex = -1;
				// Realign and animate neighbors during phase1
				RealignCards(card, true);
				if (_returnCo != null) StopCoroutine(_returnCo);
				_returnCo = StartCoroutine(SmoothMoveCardToHome(card, returnAheadZ, returnPhase1, returnPhase2, false));
			}
			else
			{
				// commit insertion immediately
				_reservedGapCard = null;
				_reservedGapSlot = -1;
				_reservedGapInsertIndex = -1;
				if (animateReturning)
				{
					RealignCards(card, true);
					if (_returnCo != null) StopCoroutine(_returnCo);
					_returnCo = StartCoroutine(SmoothMoveCardToHome(card, returnAheadZ, returnPhase1, returnPhase2, false));
				}
				else
				{
					RealignCards(null, true);
				}
			}
		}

		public bool TryReturnCardToHome(CardView3D card)
		{
			if (card == null) return false;

			var cardSlot = card.transform.parent?.GetComponent<CardSlotBehaviour>();
			if (cardSlot != null)
			{
				Debug.Log($"[HandSplineZone] 卡牌已在槽位中，不返回手牌: {card.name}");
				return false;
			}

			RestoreGapForCard(card, animateReturning: true);
			Debug.Log($"[HandSplineZone] 卡牌开始返回手牌: {card.name}");
			return true;
		}

		public bool TrySnap(CardView3D card)
		{
			if (card == null) return false;
			float best = float.MaxValue; int bestIdx = -1;
			for (int i = 0; i < slots; i++)
			{
				if (TryGetSlotPose(i, out var p, out _))
				{
					float d = Vector3.Distance(card.transform.position, p);
					if (d < best) { best = d; bestIdx = i; }
				}
			}
			if (bestIdx >= 0 && best <= snapDistance) return AssignCardToSlot(card, bestIdx);
			return false;
		}

		private void AnimateCardToSlot(CardView3D card, Vector3 targetPos, Quaternion targetRot, bool instantIfClose)
		{
			if (card == null) return;
			if (instantIfClose)
			{
				float dist = Vector3.Distance(card.transform.position, targetPos);
				float ang = Quaternion.Angle(card.transform.rotation, targetRot);
				if (dist <= 1e-3f && ang <= 0.5f)
				{
					card.transform.SetPositionAndRotation(targetPos, targetRot);
					CancelRepositionAnimation(card);
					return;
				}
			}

			CancelRepositionAnimation(card);
			Coroutine co = StartCoroutine(RepositionCardRoutine(card, targetPos, targetRot));
			_repositionCos[card] = co;
		}

		private IEnumerator RepositionCardRoutine(CardView3D card, Vector3 targetPos, Quaternion targetRot)
		{
			Vector3 startPos = card.transform.position;
			Quaternion startRot = card.transform.rotation;
			float duration = Mathf.Max(0.01f, returnPhase1);
			AnimationCurve curve = returnPhase1Curve_Other ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
			float elapsed = 0f;
			while (elapsed < duration && card != null)
			{
				elapsed += Time.deltaTime;
				float u = Mathf.Clamp01(elapsed / duration);
				float w = Mathf.Clamp01(curve.Evaluate(u));
				Vector3 pos = Vector3.LerpUnclamped(startPos, targetPos, w);
				Quaternion rot = Quaternion.Slerp(startRot, targetRot, w);
				card.transform.SetPositionAndRotation(pos, rot);
				yield return null;
			}
			if (card != null)
			{
				card.transform.SetPositionAndRotation(targetPos, targetRot);
			}
			_repositionCos.Remove(card);
		}

		private void CancelRepositionAnimation(CardView3D card)
		{
			if (card == null) return;
			if (_repositionCos.TryGetValue(card, out var co) && co != null)
			{
				StopCoroutine(co);
			}
			_repositionCos.Remove(card);
		}

		public static bool TrySnapIntoAny(CardView3D card)
		{
			var zones = GameObject.FindObjectsOfType<HandSplineZone>(true);
			foreach (var z in zones) if (z != null && z.TrySnap(card)) return true;
			return false;
		}

		public void GetNeighbors(CardView3D card, out CardView3D left, out CardView3D right)
		{
			left = null; right = null;
			if (card == null || _cards == null || _cards.Length == 0) return;
			var list = new System.Collections.Generic.List<CardView3D>();
			for (int i = 0; i < _cards.Length; i++) if (_cards[i] != null) list.Add(_cards[i]);
			list.Sort((a,b)=> (a.slotIndex).CompareTo(b.slotIndex));
			int idx = list.FindIndex(c => c == card);
			if (idx < 0) return;
			if (idx-1 >= 0) left = list[idx-1];
			if (idx+1 < list.Count) right = list[idx+1];
		}

		private void GetCardTargetPose(CardView3D card, out Vector3 targetPos, out Quaternion targetRot)
		{
			targetPos = transform.position;
			targetRot = transform.rotation;
			if (card == null) return;

			if (_targetUnits != null && _targetUnits.TryGetValue(card, out float unitsFromDict))
			{
				EvaluatePoseForUnits(unitsFromDict, out targetPos, out targetRot, out _);
				return;
			}

			if (card.slotIndex >= 0 && TryGetSlotPose(card.slotIndex, out targetPos, out targetRot)) return;

			var tryGetHome = card.GetType().GetMethod("GetHomePose", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
			if (tryGetHome != null)
			{
				if (tryGetHome.Invoke(card, null) is System.ValueTuple<Vector3, Quaternion> homePose)
				{
					targetPos = homePose.Item1;
					targetRot = homePose.Item2;
					return;
				}
			}

			TryGetSlotPose((slots > 0) ? (slots - 1) / 2 : 0, out targetPos, out targetRot);
		}

		private IEnumerator SmoothMoveCardToHome(CardView3D card, float aheadZ, float phase1Dur, float phase2Dur, bool insertAfterPhase1 = false)
		{
			if (card == null) yield break;

			System.Reflection.PropertyInfo isReturningProp = null;
			try
			{
				isReturningProp = card.GetType().GetProperty("IsReturningHome", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				var setter = isReturningProp?.GetSetMethod(true);
				setter?.Invoke(card, new object[] { true });
			}
			catch { }

			Rigidbody rb = card.body;
			BoxCollider bx = card.box != null ? card.box : card.GetComponentInChildren<BoxCollider>(true);
			bool hasBody = rb != null;
			bool prevDetect = false, prevGravity = false, prevKinematic = false, prevBoxEnabled = false;
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
			if (bx != null)
			{
				prevBoxEnabled = bx.enabled;
				bx.enabled = false;
			}

			try
			{
                Vector3 startPos = card.transform.position;
                Quaternion startRot = card.transform.rotation;
                GetCardTargetPose(card, out Vector3 targetPos, out Quaternion targetRot);

				Vector3 forwardDir = transform.forward;
				if (forwardDir.sqrMagnitude < 1e-6f) forwardDir = Vector3.forward;
				forwardDir.Normalize();
				// Ensure the ahead position reaches the target Y before phase 2 starts so
				// vertical coordinate is already correct at the start of phase2.
				Vector3 aheadPos = targetPos + forwardDir * aheadZ;
				// Force aheadPos Y to be exactly the target Y so Y interpolation completes in phase1
				aheadPos.y = targetPos.y;

                System.Func<AnimationCurve, float, float> evalCurve = (curve, t) =>
                {
                    if (curve == null) return t;
                    return Mathf.Clamp01(curve.Evaluate(Mathf.Clamp01(t)));
                };

				if (phase1Dur > 1e-4f)
                {
                    float t = 0f;
                    while (t < phase1Dur)
                    {
                        t += Time.deltaTime;
                        float u = Mathf.Clamp01(t / phase1Dur);
                        float w = evalCurve(returnPhase1Curve, u);
                        Vector3 pos = Vector3.LerpUnclamped(startPos, aheadPos, w);
                        Quaternion rot = Quaternion.Slerp(startRot, targetRot, w * 0.35f);
                        card.transform.SetPositionAndRotation(pos, rot);
                        yield return null;
                    }
                }
                else
                {
                    card.transform.position = aheadPos;
                }

				// If requested, insert the card into the hand (so sibling order / render order
				// are correct) before phase2 begins. This ensures the card's Y is already at
				// the home Y when it's added to the hierarchy and avoids occlusion popping.
				if (insertAfterPhase1)
				{
					try
					{
						// Build a list of existing cards (card should not be present)
						var list = new List<CardView3D>();
						if (_cards != null && _cards.Length > 0)
						{
							for (int i = 0; i < _cards.Length; i++) if (_cards[i] != null) list.Add(_cards[i]);
						}

						int insertIndex = list.Count;
						if (_reservedGapCard == card && _reservedGapInsertIndex >= 0)
						{
							insertIndex = Mathf.Clamp(_reservedGapInsertIndex, 0, list.Count);
						}
						else if (_reservedGapSlot >= 0)
						{
							insertIndex = Mathf.Clamp(_reservedGapSlot, 0, list.Count);
						}

						// Insert the card and update indices
						if (!list.Contains(card)) list.Insert(insertIndex, card);
						_cards = list.ToArray();
						for (int i = 0; i < _cards.Length; i++) if (_cards[i] != null) _cards[i].handIndex = i;

						// Clear reserved gap state
						_reservedGapCard = null;
						_reservedGapSlot = -1;
						_reservedGapInsertIndex = -1;

						// Ensure render / sibling order is set, and let RealignCards animate others
						try { card.transform.SetSiblingIndex(Mathf.Clamp(insertIndex, 0, Mathf.Max(0, (_cards?.Length ?? 0) - 1))); } catch {}
						try { card.ApplyHandRenderOrder(insertIndex * _renderOrderStep); } catch {}
						RealignCards(card, true);
					}
					catch { }
				}

				// Phase 2: smoothly move from current position (including Y) to the target pose.
				// Previously Y was snapped to the target immediately; instead keep the current
				// start position so Y will interpolate according to the return curve.
				// preserve the originally computed target pose so phase2 follows the
				// same XZ/rotation trajectory as planned at the start (inserting the
				// card into the hierarchy must not change the phase2 path)
				Vector3 phase2StartPos = card.transform.position;
				Vector3 phase2TargetPos = targetPos;
				Quaternion phase2TargetRot = targetRot;
				Quaternion phase2StartRot = card.transform.rotation;

                if (phase2Dur > 1e-4f)
                {
                    float t2 = 0f;
                    while (t2 < phase2Dur)
                    {
                        t2 += Time.deltaTime;
                        float u = Mathf.Clamp01(t2 / phase2Dur);
                        float w = evalCurve(returnPhase2Curve, u);
						Vector3 pos = Vector3.LerpUnclamped(phase2StartPos, phase2TargetPos, w);
						Quaternion rot = Quaternion.Slerp(phase2StartRot, phase2TargetRot, w);
                        card.transform.SetPositionAndRotation(pos, rot);
                        yield return null;
                    }
                }
				else
				{
					card.transform.position = phase2TargetPos;
				}

				card.transform.SetPositionAndRotation(phase2TargetPos, phase2TargetRot);
				card.SetHomeFromZone(transform, targetPos, targetRot);
			}
			finally
			{
				if (hasBody)
				{
					rb.constraints = prevConstraints;
					rb.detectCollisions = prevDetect;
					rb.useGravity = prevGravity;
					rb.isKinematic = prevKinematic;
				}
				if (bx != null) bx.enabled = prevBoxEnabled;

				try
				{
					var setter2 = isReturningProp?.GetSetMethod(true);
					setter2?.Invoke(card, new object[] { false });
				}
				catch { }
			}

			var onRet = card.GetType().GetMethod("OnReturnedHome", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
			if (onRet != null) onRet.Invoke(card, null);

			_returnCo = null;
		}

	}
	
}
