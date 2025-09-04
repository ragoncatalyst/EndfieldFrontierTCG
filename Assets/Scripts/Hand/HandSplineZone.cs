using UnityEngine;
using EndfieldFrontierTCG.CA;
using System.Collections.Generic;

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

		[Header("Depth Stacking")]
		public bool stackDepthByIndex = true;
		public float depthPerSlot = 0.01f;
		public bool reverseDepthOrder = false;

		[Header("Line Params")]
		public float lineSpacing = 0.8f;
		public Vector3 lineLocalDirection = new Vector3(1, 0, 0);
		public float lineLocalY = 0f;
		public float lineYawOffsetDeg = 90f;

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

		[Header("Input Thresholds")]
		[Tooltip("开始拖拽所需的最小鼠标位移（像素）")]
		public float pressMoveThresholdPx = 8f;
		[Tooltip("开始拖拽所需的最短按住时间（秒）")]
		public float pressHoldThresholdSec = 0.12f;

		[Header("Return-to-Home")]
		public float returnAheadZ = 0.2f;
		public float returnPhase1 = 0.15f;
		public float returnPhase2 = 0.18f;

		[Header("Debug")] public bool debugLogs = false;
		[Header("Build")]
		[SerializeField] private string handZoneRevision = "rev_2025-09-03_02"; // touch to force recompilation
		[Tooltip("用于独立悬停检测的层遮罩（-1 表示全部层）")] public LayerMask interactLayerMask = ~0;

		private CardView3D[] _cards = new CardView3D[0];
		private int _hoverIndex = -1;
		private bool _hoverFromBelow = false;
		private Coroutine _hoverCo;
		private int _paramsHash = 0;
		private readonly Dictionary<int, float> _enterMinYByIndex = new Dictionary<int, float>();
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

			// 中央输入：独立的射线悬停与拖拽
			UpdateHoverByRay();
			DriveDragByRay();

			// 自主维持：若是“从下方进入”，只要未被前方物体遮挡并且射线能击中该牌，就保持 hover
			if (_hoverIndex >= 0 && _hoverFromBelow)
			{
				if (!IsPointerOnTopOfCard(_hoverIndex))
					_hoverIndex = -1; // 被遮挡或射线未击中 -> 结束
			}
			// 拖拽时不允许 hover（被拖拽牌视为非 hover，其他牌也不触发 hover 动画）
			if (_activeDragging) _hoverIndex = -1;
			// 兜底：若没有在按、没有拖，但仍然残留活动目标，则清理
			if (!_pressing && !_activeDragging && _activePressed != null)
			{
				_activePressed = null;
			}
		}

		private void DriveDragByRay()
		{
			if (_cards == null || _cards.Length == 0) return;
			var cam = Camera.main; if (cam == null) return;
			var ray = cam.ScreenPointToRay(Input.mousePosition);
			var hits = Physics.RaycastAll(ray, 1000f, interactLayerMask, QueryTriggerInteraction.Collide);
			CardView3D top = null; float topY = float.NegativeInfinity;
			for (int i = 0; i < hits.Length; i++)
			{
				var cv = hits[i].collider != null ? hits[i].collider.GetComponentInParent<CardView3D>() : null;
				if (cv == null) continue;
				float y = hits[i].collider.bounds.max.y;
				if (y > topY) { topY = y; top = cv; }
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
						// 开启并拢：记录被拖拽的卡与其原槽位，其他牌向中心收拢但保留各自原始 slotIndex
						_draggingCard = _activePressed;
						_draggingSlot = (_draggingCard != null) ? _draggingCard.slotIndex : -1;
						_compactOnDrag = (_draggingSlot >= 0);
						if (_compactOnDrag) BuildCompactMapping();
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
		}

		private void UpdateHoverByRay()
		{
			// 正在拖拽时彻底禁止 hover
			if (_activeDragging) { _hoverIndex = -1; return; }
			var cam = Camera.main; if (cam == null) return;
			// 若正在按住鼠标并已有 hover 目标，则锁定该目标（但不再在这里驱动拖拽）
			if (Input.GetMouseButton(0) && _hoverIndex >= 0 && _cards != null && _hoverIndex < _cards.Length)
			{
				var lockCv = _cards[_hoverIndex]; if (lockCv != null) { return; }
			}
			var ray = cam.ScreenPointToRay(Input.mousePosition);
			var hits = Physics.RaycastAll(ray, 1000f, interactLayerMask, QueryTriggerInteraction.Collide);
			if (hits == null || hits.Length == 0) { if (_hoverIndex != -1 && debugLogs) Debug.Log("[HandSplineZone] hoverIndex=-1 (no hits)"); _hoverIndex = -1; return; }
			CardView3D top = null; float topY = float.NegativeInfinity;
			for (int i = 0; i < hits.Length; i++)
			{
				var cv = hits[i].collider != null ? hits[i].collider.GetComponentInParent<CardView3D>() : null;
				if (cv == null) continue;
				float y = hits[i].collider.bounds.max.y;
				if (y > topY) { topY = y; top = cv; }
			}
			if (top == null) { if (_hoverIndex != -1 && debugLogs) Debug.Log("[HandSplineZone] hoverIndex=-1 (no top)"); _hoverIndex = -1; return; }
			if (top.IsReturningHome) { return; }
			int newIdx = top.handIndex;
			if (newIdx != _hoverIndex)
			{
				if (debugLogs)
				{
					string n = top != null ? top.name : "null";
					Debug.Log($"[HandSplineZone] hoverIndex { _hoverIndex } -> { newIdx } ({ n })");
				}
				_hoverIndex = newIdx; // 仅在变化时赋值，减少抖动
			}
		}

		public void RegisterCards(CardView3D[] cards)
		{
			_cards = cards ?? new CardView3D[0];
			for (int i = 0; i < _cards.Length; i++) if (_cards[i] != null) _cards[i].handIndex = i;
			ForceRelayoutExistingCards();
		}

		public void ForceRelayoutExistingCards()
		{
			var list = GetComponentsInChildren<CardView3D>(true);
			for (int i = 0; i < list.Length; i++)
			{
				int bestIdx = 0; float best = float.MaxValue;
				for (int s = 0; s < slots; s++)
				{
					if (TryGetSlotPose(s, out var p, out _))
					{
						float d = Vector3.SqrMagnitude(list[i].transform.position - p);
						if (d < best) { best = d; bestIdx = s; }
					}
				}
				AssignCardToSlot(list[i], bestIdx);
			}
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
				_hoverIndex = card.handIndex;
				_hoverFromBelow = IsPointerFromBelow(card);
				_enterMinYByIndex[_hoverIndex] = GetCardScreenMinY(card);
			}
			else
			{
				// 若不是从下方进入，按常规退出；
				// 若是从下方进入，交由 LateUpdate 的可视判定维持，不立即清空
				if (!_hoverFromBelow && _hoverIndex == card.handIndex)
					_hoverIndex = -1;
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

		private bool IsPointerFromBelow(CardView3D card)
		{
			float minY = GetCardScreenMinY(card);
			return Input.mousePosition.y <= (minY + enterFromBelowSlackPx);
		}



		private System.Collections.IEnumerator RepositionByHover()
		{
			float durX = Mathf.Clamp(hoverXLerp, 0.01f, 1f);
			float durY = Mathf.Clamp(hoverXLerp, 0.01f, 1f);
			float durZR = Mathf.Clamp(hoverZLerp, 0.01f, 1f);
			float durZL = Mathf.Clamp(hoverZLeftLerp, 0.01f, 1f);
			float px = 0f, py = 0f, pzR = 0f, pzL = 0f;
			var vels = new System.Collections.Generic.Dictionary<CardView3D, Vector3>(_cards.Length);
			while (true)
			{
				durX = Mathf.Clamp(hoverXLerp, 0.01f, 1f);
				durY = Mathf.Clamp(hoverXLerp, 0.01f, 1f);
				durZR = Mathf.Clamp(hoverZLerp, 0.01f, 1f);
				durZL = Mathf.Clamp(hoverZLeftLerp, 0.01f, 1f);
				float tActive = (_hoverIndex >= 0) ? 1f : 0f;
				px = Mathf.MoveTowards(px, tActive, Time.deltaTime / durX);
				py = Mathf.MoveTowards(py, tActive, Time.deltaTime / durY);
				pzR = Mathf.MoveTowards(pzR, tActive, Time.deltaTime / durZR);
				pzL = Mathf.MoveTowards(pzL, tActive, Time.deltaTime / durZL);

				Vector3 dirW = transform.TransformDirection(lineLocalDirection);
				Vector3 lineXZ = new Vector3(dirW.x, 0f, dirW.z);
				if (lineXZ.sqrMagnitude < 1e-6f) lineXZ = Vector3.right;
				lineXZ.Normalize();
				Vector3 liftDir = new Vector3(-lineXZ.z, 0f, lineXZ.x);

				for (int i = 0; i < _cards.Length; i++)
				{
					var c = _cards[i]; if (c == null) continue;
					if (c.IsReturningHome) continue;
					// 被拖拽的牌：完全从自动布局中排除
					if (_activeDragging && _draggingCard != null && c == _draggingCard) continue;
					int sIdx = _cards[i].slotIndex >= 0 ? _cards[i].slotIndex : i;
					Vector3 p; Quaternion r; TryGetSlotPose(sIdx, out p, out r);
					Vector3 target = p; Quaternion rot = r;
					// 并拢排序：当有卡被拖出时，其余卡被重新连续编号到中心附近的槽位
					if (_compactOnDrag && (_draggingCard == null || c != _draggingCard))
					{
						if (_compactAssignedSlot.TryGetValue(c, out int assigned))
						{
							Vector3 p2; Quaternion r2; TryGetSlotPose(assigned, out p2, out r2);
							target = p2; rot = r2;
						}
					}
					if (_hoverIndex >= 0)
					{
						int hoveredSlot = (_cards[_hoverIndex].slotIndex >= 0) ? _cards[_hoverIndex].slotIndex : _hoverIndex;
						bool isLeft = sIdx < hoveredSlot;
						bool isRight = sIdx > hoveredSlot;
						if (isRight)
						{
							var cam = Camera.main;
							if (cam != null && Mathf.Abs(hoverZ) > 1e-6f)
							{
								Vector3 rightScr = cam.transform.right; rightScr.y = 0f; rightScr.Normalize();
								target += rightScr * (hoverZ * pzR);
							}
						}
						else if (isLeft)
						{
							var cam = Camera.main;
							if (cam != null && Mathf.Abs(hoverZLeft) > 1e-6f)
							{
								Vector3 rightScr = cam.transform.right; rightScr.y = 0f; rightScr.Normalize();
								target += (-rightScr) * (hoverZLeft * pzL);
							}
						}
						if (i == _hoverIndex)
							target += liftDir * (hoverX * py);
					}
					if (!vels.TryGetValue(c, out var vel)) vel = Vector3.zero;
					c.transform.position = Vector3.SmoothDamp(c.transform.position, target, ref vel, 0.06f);
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
			float yaw = Mathf.Atan2(fwdXZ.x, fwdXZ.z) * Mathf.Rad2Deg;
			if (flipForward) yaw += 180f;
			rot = Quaternion.Euler(90f, yaw + yawAdjustDeg + lineYawOffsetDeg, 0f);
			pos = pWorld + Vector3.up * offsetUp + fwdXZ * offsetForward;
			if (stackDepthByIndex && Camera.main != null && slots > 0)
			{
				int order = reverseDepthOrder ? (slots - 1 - index) : index;
				pos += -Camera.main.transform.forward * (order * depthPerSlot);
			}
			return true;
		}

		public bool TryGetPoseByUnits(float units, out Vector3 pos, out Quaternion rot)
		{
			Vector3 dirL = lineLocalDirection.sqrMagnitude < 1e-6f ? Vector3.right : lineLocalDirection.normalized;
			Vector3 local = dirL * (units * lineSpacing);
			local.y = lineLocalY;
			Vector3 pWorld = transform.TransformPoint(local);
			Vector3 fwdW = transform.TransformDirection(dirL);
			Vector3 fwdXZ = new Vector3(fwdW.x, 0f, fwdW.z);
			if (fwdXZ.sqrMagnitude < 1e-6f) fwdXZ = transform.forward;
			fwdXZ.Normalize();
			float yaw = Mathf.Atan2(fwdXZ.x, fwdXZ.z) * Mathf.Rad2Deg;
			if (flipForward) yaw += 180f;
			rot = Quaternion.Euler(90f, yaw + yawAdjustDeg + lineYawOffsetDeg, 0f);
			pos = pWorld + Vector3.up * offsetUp + fwdXZ * offsetForward;
			if (stackDepthByIndex && Camera.main != null)
			{
				int order = reverseDepthOrder ? (int)(-units) : (int)(units);
				pos += -Camera.main.transform.forward * (order * depthPerSlot);
			}
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

		public bool TryReturnCardToHome(CardView3D card)
		{
			if (card == null) return false;
			if (!card.IsDragging) card.BeginSmoothReturnToHome(returnAheadZ, returnPhase1, returnPhase2);
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
	}
}



