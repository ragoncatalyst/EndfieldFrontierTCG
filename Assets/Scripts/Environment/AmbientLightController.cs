using UnityEngine;

namespace EndfieldFrontierTCG.Environment
{
    [ExecuteAlways]
    public class AmbientLightController : MonoBehaviour
    {
        [Range(0f, 1f)] public float intensity = 0.2f;
        public Color ambientColor = new Color(0.8f, 0.8f, 0.8f, 1f);

        private void OnEnable() { Apply(); }
        private void OnValidate() { Apply(); }

        private void Apply()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor * intensity;
        }
    }
}


