using UnityEngine;

namespace EndfieldFrontierTCG.Hand
{
	[CreateAssetMenu(menuName = "EndfieldFrontier/HandZonePreset", fileName = "HandZonePreset")]
	public class HandZonePreset : ScriptableObject
	{
		// Layout (Line)
		public int slots;
		public float offsetUp;
		public float offsetForward;
		public bool flipForward;
		public float yawAdjustDeg;
		public float snapDistance;
		// Depth
		public bool stackDepthByIndex;
		public float depthPerSlot;
		public bool reverseDepthOrder;
		// Line Params
		public float lineSpacing;
		public Vector3 lineLocalDirection = new Vector3(1,0,0);
		public float lineLocalY;
		public float lineYawOffsetDeg;
		// Hover
		public float hoverX; public float hoverXLerp;
		public float hoverZ; public float hoverZLerp;
		public float hoverZLeft; public float hoverZLeftLerp;
		public bool invertHoverSide;
		public float enterFromBelowSlackPx;
		public float hoverBottomExtendPx;
		// Return
		public float returnAheadZ; public float returnPhase1; public float returnPhase2;
	}
}


