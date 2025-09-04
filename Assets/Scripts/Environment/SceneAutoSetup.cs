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
		public Transform preferLookTarget; // assign HandSplineZone transform if available
		public Vector3 cameraPosition = new Vector3(0f, 1.5f, -4.5f);
		public Vector3 cameraEuler = new Vector3(15f, 0f, 0f);
		public float cameraFov = 40f;

		private void OnEnable()
		{
			Apply();
		}

		private void Update()
		{
			// Keep enforced during edit and play
			Apply();
		}

		private void Apply()
		{
			// Ensure plane has TablePlane and sits at tableY
			var planeGo = GameObject.Find("Plane");
			if (planeGo != null)
			{
				var tp = planeGo.GetComponent<TablePlane>();
				if (tp == null) tp = planeGo.AddComponent<TablePlane>();
				if (tp != null)
				{
					tp.SurfaceY = tableY;
					// Application of Y happens inside TablePlane via ExecuteAlways
				}
			}

			// Position main camera to see the hand area
			var cam = Camera.main;
			if (cam != null)
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
				cam.fieldOfView = cameraFov;
				cam.nearClipPlane = 0.05f;
				cam.farClipPlane = 200f;
				cam.cullingMask = ~0; // Everything
				// Optional: look-at if target given
				if (preferLookTarget != null)
					cam.transform.LookAt(preferLookTarget.position + Vector3.up * 0.2f);
			}
		}
	}
}


