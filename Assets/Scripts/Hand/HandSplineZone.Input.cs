using UnityEngine;
using EndfieldFrontierTCG.CA;

namespace EndfieldFrontierTCG.Hand
{
	public partial class HandSplineZone : MonoBehaviour
	{
		private bool IsPointerOnTopOfCard(int hoveredIndex)
		{
			var cam = Camera.main; if (cam == null) return false;
			if (TryGetCardScreenBounds(hoveredIndex, out float minX, out float maxX, out float minY, out float maxY))
			{
				float mx = Input.mousePosition.x;
				float my = Input.mousePosition.y;
				if (mx >= minX && mx <= maxX && my >= (minY - hoverBottomExtendPx) && my <= maxY) return true;
			}
			// 物理检测：额外做一个沿世界 Z- 方向位移的射线测试，直接针对被 hover 的卡牌 AABB
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			var hits = Physics.RaycastAll(ray, 1000f);
			if (hits == null || hits.Length == 0) return false;
			float minDist = float.PositiveInfinity;
			float distHovered = float.PositiveInfinity;
			for (int i = 0; i < hits.Length; i++)
			{
				minDist = Mathf.Min(minDist, hits[i].distance);
				var cv = hits[i].collider != null ? hits[i].collider.GetComponentInParent<CardView3D>() : null;
				if (cv != null && cv.handIndex == hoveredIndex)
					distHovered = Mathf.Min(distHovered, hits[i].distance);
			}
			if (float.IsPositiveInfinity(distHovered)) return false;
			bool topMost = distHovered <= minDist + 1e-3f;
			if (topMost) return true;
			// 额外的“下延伸”检测：在世界 Z- 方向平移一条射线再测试一次
			var card = _cards != null && hoveredIndex >=0 && hoveredIndex < _cards.Length ? _cards[hoveredIndex] : null;
			if (card == null) return false;
			var col = card.GetComponentInChildren<Collider>(); if (col == null) return false;
			Vector3 shift = new Vector3(0f, 0f, -hoverExtendZBackward);
			Ray ray2 = new Ray(ray.origin + shift, ray.direction);
			var hits2 = Physics.RaycastAll(ray2, 1000f);
			for (int i=0;i<hits2.Length;i++)
			{
				var cv = hits2[i].collider != null ? hits2[i].collider.GetComponentInParent<CardView3D>() : null;
				if (cv != null && cv.handIndex == hoveredIndex) return true;
			}
			return false;
		}

		private bool TryGetCardScreenBounds(int index, out float minX, out float maxX, out float minY, out float maxY)
		{
			minX = maxX = minY = maxY = 0f;
			if (_cards == null || index < 0 || index >= _cards.Length) return false;
			var cam = Camera.main; if (cam == null) return false;
			var card = _cards[index]; if (card == null) return false;
			var col = card.GetComponentInChildren<Collider>(); if (col == null) return false;
			var b = col.bounds; Vector3 c=b.center,e=b.extents;
			minX = float.PositiveInfinity; maxX = float.NegativeInfinity;
			minY = float.PositiveInfinity; maxY = float.NegativeInfinity;
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
				minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
				minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
			}
			return true;
		}

		// 仅用于“从下方进入”场景：允许在进入后的下边缘带宽内保持 hover
		private bool IsWithinFromBelowBand(int index)
		{
			if (_cards == null || index < 0 || index >= _cards.Length) return false;
			var cam = Camera.main; if (cam == null) return false;
			if (!TryGetCardScreenBounds(index, out float minX, out float maxX, out float minYNow, out float maxYNow)) return false;
			float minYEnter = minYNow;
			if (_enterMinYByIndex != null && _enterMinYByIndex.TryGetValue(index, out float rec)) minYEnter = rec;
			float bandTop = minYEnter + hoverBottomExtendPx + enterFromBelowSlackPx;
			float mx = Input.mousePosition.x; float my = Input.mousePosition.y;
			return (mx >= minX && mx <= maxX && my <= bandTop);
		}

		private int FindPseudoHoverIndex()
		{
			if (_cards == null || _cards.Length == 0) return -1;
			var cam = Camera.main; if (cam == null) return -1;
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			// 把鼠标指针投到手牌所在的 Y 平面上，得到一个世界点
			float yPlane = transform.position.y;
			Plane plane = new Plane(Vector3.up, new Vector3(0f, yPlane, 0f));
			if (!plane.Raycast(ray, out float enter)) return -1;
			Vector3 pt = ray.GetPoint(enter);
			int best = -1; float bestZ = float.PositiveInfinity;
			for (int i = 0; i < _cards.Length; i++)
			{
				var c = _cards[i]; if (c == null) continue;
				var col = c.GetComponentInChildren<Collider>(); if (col == null) continue;
				Bounds b = col.bounds;
				// X 在盒内，且指针 Z 小于卡牌底边 Z（即在其正下方）
				float minX = b.min.x, maxX = b.max.x, minZ = b.min.z;
				if (pt.x >= minX && pt.x <= maxX && pt.z < minZ)
				{
					// 选择距离最近的那张（minZ 与指针 Z 的差值最小）
					float dz = (minZ - pt.z);
					if (dz < bestZ) { bestZ = dz; best = c.handIndex; }
				}
			}
			return best;
		}

		private bool IsInPseudoArea(int index)
		{
			if (_cards == null || index < 0 || index >= _cards.Length) return false;
			var cam = Camera.main; if (cam == null) return false;
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			float yPlane = transform.position.y;
			Plane plane = new Plane(Vector3.up, new Vector3(0f, yPlane, 0f));
			if (!plane.Raycast(ray, out float enter)) return false;
			Vector3 pt = ray.GetPoint(enter);
			var c = _cards[index]; if (c == null) return false;
			var col = c.GetComponentInChildren<Collider>(); if (col == null) return false;
			Bounds b = col.bounds;
			float minX = b.min.x, maxX = b.max.x, minZ = b.min.z;
			return (pt.x >= minX && pt.x <= maxX && pt.z < minZ);
		}
	}
}


