using UnityEngine;

// Handles render order and simple rearrange helpers
public partial class CardView3D : MonoBehaviour
{
    private int[] _handBaseSortingOrders;
    private Renderer[] _allRenderers;

    // Apply a render-order offset to all child renderers
    public void ApplyHandRenderOrder(int order)
    {
        if (_allRenderers == null || _allRenderers.Length == 0) _allRenderers = GetComponentsInChildren<Renderer>(true);
        if (_allRenderers == null) return;
        if (_handBaseSortingOrders == null || _handBaseSortingOrders.Length != _allRenderers.Length)
        {
            _handBaseSortingOrders = new int[_allRenderers.Length];
            for (int i = 0; i < _allRenderers.Length; i++) _handBaseSortingOrders[i] = _allRenderers[i] != null ? _allRenderers[i].sortingOrder : 0;
        }
        for (int i = 0; i < _allRenderers.Length; i++)
        {
            var r = _allRenderers[i]; if (r == null) continue;
            r.sortingOrder = _handBaseSortingOrders[i] + order;
        }
    }

    // Minimal API kept for compatibility
    public void AlignToSlotSurface(object slot) { /* no-op in simplified rearrange */ }
}
