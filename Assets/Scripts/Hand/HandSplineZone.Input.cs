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
			return topMost;
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


