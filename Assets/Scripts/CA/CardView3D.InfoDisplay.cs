using UnityEngine;

// Handles info display and simple bounds/placement helpers
public partial class CardView3D : MonoBehaviour
{
    public const float DefaultPlacementHalfThickness = 0.01f;

    // Minimal Bind to keep usage elsewhere safe
    public void Bind(object data)
    {
        if (data == null) return;
        if (NameText != null) NameText.gameObject.SetActive(false);
        if (HPText != null) HPText.gameObject.SetActive(false);
        if (ATKText != null) ATKText.gameObject.SetActive(false);
    }

    public void SetInfoVisible(bool visible)
    {
        if (NameText != null) NameText.gameObject.SetActive(visible);
    }

    public void ApplyHoverColliderExtend(bool enable)
    {
        if (box == null) return;
        if (enable) box.size = box.size + new Vector3(0f, 0f, 0.06f);
    }

    public void GetPlacementExtents(Quaternion r, out float minY, out float maxY) { minY = -DefaultPlacementHalfThickness; maxY = DefaultPlacementHalfThickness; }

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        if (box != null)
        {
            bounds = box.bounds;
            return true;
        }
        var rs = GetComponentsInChildren<Renderer>(true);
        if (rs != null && rs.Length > 0)
        {
            bounds = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) bounds.Encapsulate(rs[i].bounds);
            return true;
        }
        bounds = new Bounds(transform.position, Vector3.zero);
        return false;
    }

    public void PlayEventSequence(EndfieldFrontierTCG.Board.EventPlayZone zone)
    {
        // placeholder — actual event playback is out of scope for simplified view
    }
}
