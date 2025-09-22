using UnityEngine;

public class RadialItem : MonoBehaviour
{
    [Tooltip("该条目在圆上的角度微调（度）")]
    public float angleOffsetDeg = 0f;

    [Tooltip("该条目的半径微调（像素/单位）")]
    public float radiusOffset = 0f;

    public enum RotationOverride
    {
        Inherit = -1, // 继承 RadialLayoutGroup 的旋转模式
        KeepUpright = 0,
        Tangent     = 1,
        FaceCenter  = 2,
        Fixed       = 3  // 使用下方 fixedZRotation
    }

    [Tooltip("可覆盖父组件的旋转模式")]
    public RotationOverride rotationOverride = RotationOverride.Inherit;

    [Tooltip("当选择 Fixed 覆盖时生效")]
    public float fixedZRotation = 0f;
}