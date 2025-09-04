using UnityEngine;

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
			// 物理检测：对被 hover 卡牌构造一个向世界 Z- 方向延伸的包围体进行额外判定
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
			// 额外的“下延伸”检测：将该牌碰撞箱沿世界 Z- 方向扩展一段，仍算命中
			var card = _cards != null && hoveredIndex >=0 && hoveredIndex < _cards.Length ? _cards[hoveredIndex] : null;
			if (card == null) return false;
			var col = card.GetComponentInChildren<Collider>(); if (col == null) return false;
			Bounds b = col.bounds; b.Expand(new Vector3(0f, 0f, hoverExtendZBackward)); b.center += new Vector3(0f, 0f, -hoverExtendZBackward*0.5f);
			// 用简单的 AABB-光线测试近似：如果射线与扩展后的包围盒相交，也认为在“下延伸区”
			float tmin = 0f, tmax = 1000f;
			for (int axis = 0; axis < 3; axis++)
			{
				float ro = axis==0?ray.origin.x:(axis==1?ray.origin.y:ray.origin.z);
				float rd = axis==0?ray.direction.x:(axis==1?ray.direction.y:ray.direction.z);
				float minA = axis==0?b.min.x:(axis==1?b.min.y:b.min.z);
				float maxA = axis==0?b.max.x:(axis==1?b.max.y:b.max.z);
				if (Mathf.Abs(rd) < 1e-6f) { if (ro < minA || ro > maxA) return false; }
				else {
					float t1 = (minA - ro) / rd; float t2 = (maxA - ro) / rd; if (t1>t2){var tmp=t1;t1=t2;t2=tmp;}
					tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2); if (tmin>tmax) return false;
				}
			}
			return true;
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
	}
}


