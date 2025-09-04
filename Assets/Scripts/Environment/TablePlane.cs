using UnityEngine;

namespace EndfieldFrontierTCG.Environment
{
	[RequireComponent(typeof(MeshRenderer))]
	[RequireComponent(typeof(MeshFilter))]
	[RequireComponent(typeof(Collider))]
	[ExecuteAlways]
	public class TablePlane : MonoBehaviour
	{
		[Header("Texture")]
		public string textureResourcePath = "Table/Desk"; // Resources/Table/Desk.png
		public Vector2 tiling = new Vector2(1, 1);
		public Vector2 offset = Vector2.zero;

		[Header("Surface")]
		public float SurfaceY = -0.5f;
		[Tooltip("让桌面自动适配摄像机视野范围（在 SurfaceY 高度平面上）")]
		public bool fitToCameraFrustum = true;
		[Tooltip("在四周额外扩展的世界边距（米）")]
		public float fitMargin = 0.2f;

		private MeshRenderer _mr;

		private void Awake()
		{
			_mr = GetComponent<MeshRenderer>();
			EnsureMaterial();
			var c = GetComponent<Collider>();
			if (c != null) c.isTrigger = false;
			ApplyY();
		}

		private void Reset()
		{
			var c = GetComponent<Collider>();
			if (c != null) c.isTrigger = false;
		}

		private void OnValidate()
		{
			if (_mr == null) _mr = GetComponent<MeshRenderer>();
			EnsureMaterial();
			ApplyY();
		}

		private void Update()
		{
			// 在编辑器与运行时持续保持桌面高度
			ApplyY();
			if (fitToCameraFrustum) FitSizeToCamera();
		}

		private void ApplyY()
		{
			var pos = transform.position;
			if (Mathf.Abs(pos.y - SurfaceY) > 0.0001f)
			{
				transform.position = new Vector3(pos.x, SurfaceY, pos.z);
			}
		}

		private void EnsureMaterial()
		{
			if (_mr == null) return;
			var mat = _mr.sharedMaterial;
			if (mat == null || mat.shader == null || mat.shader.name != "Standard")
			{
				mat = new Material(Shader.Find("Standard"));
				_mr.sharedMaterial = mat;
			}

			var tex = Resources.Load<Texture2D>(textureResourcePath);
			if (tex != null)
			{
				mat.mainTexture = tex;
				mat.mainTextureScale = tiling;
				mat.mainTextureOffset = offset;
			}

			// 接受阴影，自己不投射
			_mr.receiveShadows = true;
			_mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}

		private void FitSizeToCamera()
		{
			var cam = Camera.main; if (cam == null) return;
			float y = SurfaceY;
			// 取视口四角在 y 平面的交点
			Vector3 p00 = ViewOnPlane(cam, 0f, 0f, y, transform.position);
			Vector3 p10 = ViewOnPlane(cam, 1f, 0f, y, transform.position);
			Vector3 p01 = ViewOnPlane(cam, 0f, 1f, y, transform.position);
			Vector3 p11 = ViewOnPlane(cam, 1f, 1f, y, transform.position);
			// 计算包围盒（仅 XZ）
			float minX = Mathf.Min(p00.x, p10.x, p01.x, p11.x) - fitMargin;
			float maxX = Mathf.Max(p00.x, p10.x, p01.x, p11.x) + fitMargin;
			float minZ = Mathf.Min(p00.z, p10.z, p01.z, p11.z) - fitMargin;
			float maxZ = Mathf.Max(p00.z, p10.z, p01.z, p11.z) + fitMargin;
			float sizeX = Mathf.Max(0.01f, maxX - minX);
			float sizeZ = Mathf.Max(0.01f, maxZ - minZ);
			// 设置平面缩放（假设原始 Mesh 是 1x1，沿 XZ 缩放）
			transform.position = new Vector3((minX + maxX) * 0.5f, y, (minZ + maxZ) * 0.5f);
			transform.localScale = new Vector3(sizeX, 1f, sizeZ);
		}

		private static Vector3 ViewOnPlane(Camera cam, float vx, float vy, float yPlane, Vector3 fallback)
		{
			Ray r = cam.ViewportPointToRay(new Vector3(vx, vy, 0f));
			if (Mathf.Abs(r.direction.y) < 1e-6f) return fallback;
			float t = (yPlane - r.origin.y) / r.direction.y;
			if (t < 0f) return fallback;
			return r.origin + r.direction * t;
		}
	}
}


