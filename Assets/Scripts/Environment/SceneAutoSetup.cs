using UnityEngine;

namespace EndfieldFrontierTCG.Environment
{
	// Drop this on any GameObject in the scene. It will enforce a minimal viewable setup
	// so the plane sits at y=-10 and the camera actually sees the hand zone/cards.
	[DefaultExecutionOrder(-500)]
	[ExecuteAlways]
	public class SceneAutoSetup : MonoBehaviour
	{
		public float tableY = -10f;
		[Tooltip("是否强制将 TablePlane.SurfaceY 设为 tableY（每帧）。关闭后可在 Inspector 自由修改 SurfaceY")]
		public bool enforceTableY = false;
		public Transform preferLookTarget; // assign HandSplineZone transform if available
		public Vector3 cameraPosition = new Vector3(0f, 5f, -4.5f);
		public Vector3 cameraEuler = new Vector3(15f, 0f, 0f);
		public float cameraFov = 40f;
		[Tooltip("是否持续锁定主相机的位置与朝向（默认不锁定）")]
		public bool enforceCamera = false;

		private void OnEnable()
		{
			Apply();
			// 若不锁定相机，则仅在启用时把主相机 Y 设为 5，不做持续覆盖
			if (!enforceCamera)
			{
				var cam = Camera.main;
				if (cam != null)
				{
					var p = cam.transform.position;
					cam.transform.position = new Vector3(p.x, 5f, p.z);
				}
			}
		}

		private void Update()
		{
			// Keep enforced during edit and play
			Apply();
		}

		private void Apply()
		{
			// Ensure plane has TablePlane; optionally enforce SurfaceY
			var planeGo = GameObject.Find("Plane");
			if (planeGo != null)
			{
				var tp = planeGo.GetComponent<TablePlane>();
				if (tp == null) tp = planeGo.AddComponent<TablePlane>();
				if (tp != null)
				{
					if (enforceTableY)
					{
						tp.SurfaceY = tableY;
						// Application of Y happens inside TablePlane via ExecuteAlways
					}
				}
			}

			// Position main camera to see the hand area（可选）
			var cam = Camera.main;
			if (cam != null)
			{
				if (enforceCamera)
				{
					if (!Application.isPlaying)
					{
						// In edit mode, avoid fighting the user too much: only fix if greatly off
						if (Mathf.Abs(cam.transform.position.y - cameraPosition.y) > 0.01f)
							cam.transform.position = cameraPosition;
						cam.transform.rotation = Quaternion.Euler(cameraEuler);
					}
					else
					{
						cam.transform.position = cameraPosition;
						cam.transform.rotation = Quaternion.Euler(cameraEuler);
					}
				}
				cam.fieldOfView = cameraFov;
				cam.nearClipPlane = 0.05f;
				cam.farClipPlane = 200f;
				cam.cullingMask = ~0; // Everything
				// Optional: look-at if target given
				if (preferLookTarget != null && enforceCamera)
					cam.transform.LookAt(preferLookTarget.position + Vector3.up * 0.2f);
			}
		}
	}
}


