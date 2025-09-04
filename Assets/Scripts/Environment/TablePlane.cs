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
		public float SurfaceY = -10f;

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
	}
}


