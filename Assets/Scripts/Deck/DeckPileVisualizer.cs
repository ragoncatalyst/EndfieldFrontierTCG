using UnityEngine;

namespace EndfieldFrontierTCG.Deck
{
    public class DeckPileVisualizer : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] private Transform pileBody;    // cube or mesh to scale
        [SerializeField] private float cardThickness = 0.0025f;
        [SerializeField] private float minHeight = 0.003f;
        [SerializeField] private float maxHeight = 0.12f;
        [SerializeField] private float topOffset = 0.0f;    // pushes top mesh up if needed

        private int _currentCount = 0;

        public void SetCount(int count)
        {
            _currentCount = Mathf.Max(0, count);
            UpdateVisual();
        }

        private void UpdateVisual()
        {
            if (pileBody == null) return;

            float height = Mathf.Clamp(_currentCount * cardThickness, minHeight, maxHeight);
            Vector3 scale = pileBody.localScale;
            scale.y = height;
            pileBody.localScale = scale;

            Vector3 pos = pileBody.localPosition;
            pos.y = height * 0.5f + topOffset;
            pileBody.localPosition = pos;
        }
    }
}
