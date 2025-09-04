using UnityEngine;

namespace EndfieldFrontierTCG.Hand
{
	public partial class HandSplineZone : MonoBehaviour
	{
		[Header("Presets")] public HandZonePreset activePreset;

		[ContextMenu("Preset/Export From Inspector")]
		public void ExportPreset()
		{
			if (activePreset == null)
			{
				Debug.LogWarning("[HandSplineZone] No activePreset assigned.");
				return;
			}
			activePreset.slots = slots;
			activePreset.offsetUp = offsetUp;
			activePreset.offsetForward = offsetForward;
			activePreset.flipForward = flipForward;
			activePreset.yawAdjustDeg = yawAdjustDeg;
			activePreset.snapDistance = snapDistance;
			activePreset.stackDepthByIndex = stackDepthByIndex;
			activePreset.depthPerSlot = depthPerSlot;
			activePreset.reverseDepthOrder = reverseDepthOrder;
			activePreset.lineSpacing = lineSpacing;
			activePreset.lineLocalDirection = lineLocalDirection;
			activePreset.lineLocalY = lineLocalY;
			activePreset.lineYawOffsetDeg = lineYawOffsetDeg;
			activePreset.hoverX = hoverX; activePreset.hoverXLerp = hoverXLerp;
			activePreset.hoverZ = hoverZ; activePreset.hoverZLerp = hoverZLerp;
			activePreset.hoverZLeft = hoverZLeft; activePreset.hoverZLeftLerp = hoverZLeftLerp;
			activePreset.invertHoverSide = invertHoverSide;
			activePreset.enterFromBelowSlackPx = enterFromBelowSlackPx;
			activePreset.hoverBottomExtendPx = hoverBottomExtendPx;
			activePreset.returnAheadZ = returnAheadZ; activePreset.returnPhase1 = returnPhase1; activePreset.returnPhase2 = returnPhase2;
			#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(activePreset);
			UnityEditor.AssetDatabase.SaveAssets();
			#endif
			if (debugLogs) Debug.Log("[HandSplineZone] Exported preset from Inspector");
		}

		[ContextMenu("Preset/Apply To Inspector")]
		public void ApplyPreset()
		{
			if (activePreset == null)
			{
				Debug.LogWarning("[HandSplineZone] No activePreset assigned.");
				return;
			}
			slots = activePreset.slots;
			offsetUp = activePreset.offsetUp;
			offsetForward = activePreset.offsetForward;
			flipForward = activePreset.flipForward;
			yawAdjustDeg = activePreset.yawAdjustDeg;
			snapDistance = activePreset.snapDistance;
			stackDepthByIndex = activePreset.stackDepthByIndex;
			depthPerSlot = activePreset.depthPerSlot;
			reverseDepthOrder = activePreset.reverseDepthOrder;
			lineSpacing = activePreset.lineSpacing;
			lineLocalDirection = activePreset.lineLocalDirection;
			lineLocalY = activePreset.lineLocalY;
			lineYawOffsetDeg = activePreset.lineYawOffsetDeg;
			hoverX = activePreset.hoverX; hoverXLerp = activePreset.hoverXLerp;
			hoverZ = activePreset.hoverZ; hoverZLerp = activePreset.hoverZLerp;
			hoverZLeft = activePreset.hoverZLeft; hoverZLeftLerp = activePreset.hoverZLeftLerp;
			invertHoverSide = activePreset.invertHoverSide;
			enterFromBelowSlackPx = activePreset.enterFromBelowSlackPx;
			hoverBottomExtendPx = activePreset.hoverBottomExtendPx;
			returnAheadZ = activePreset.returnAheadZ; returnPhase1 = activePreset.returnPhase1; returnPhase2 = activePreset.returnPhase2;
			if (debugLogs) Debug.Log("[HandSplineZone] Applied preset to Inspector");
		}
	}
}


