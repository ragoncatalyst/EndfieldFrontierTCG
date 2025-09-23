using System.Collections.Generic;
using UnityEngine;
using EndfieldFrontierTCG.CA;

namespace EndfieldFrontierTCG.Board
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class EventPlayZone : MonoBehaviour
    {
        private static readonly List<EventPlayZone> _activeZones = new List<EventPlayZone>();

        [Header("Placement")]
        [Tooltip("若指定则使用该锚点的位置/旋转作为落点基准。为空时使用碰撞框中心。")]
        public Transform landingAnchor;
        [Tooltip("落点相对基准的附加偏移(世界坐标)。常用于把卡牌抬离平面一点点。")]
        public Vector3 landingOffset = new Vector3(0f, 0.02f, 0f);
        [Tooltip("是否覆盖卡牌落点的旋转。开启后会使用 landingRotationEuler。")]
        public bool overrideLandingRotation = true;
        [Tooltip("落点局部欧拉角。最终旋转 = (锚点或本物体) * Euler。")]
        public Vector3 landingRotationEuler = new Vector3(90f, 0f, 0f);

        [Header("Display Phase")]
        [Tooltip("事件牌展示位置锚点。如果留空且启用自动查找，会按名称寻找。")]
        public Transform displayAnchor;
        [Tooltip("当 displayAnchor 为空时是否尝试自动查找。")]
        public bool autoFindDisplayAnchor = true;
        [Tooltip("自动查找时的对象名称。")]
        public string displayAnchorName = "EventCardDisplayZone";
        [Tooltip("从落点移动到展示区的时间（秒）。")]
        public float displayMoveDuration = 0.6f;
        [Tooltip("移动到展示区的插值曲线 (0→1)。")]
        public AnimationCurve displayMoveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("在展示区停留的时间（秒）。")]
        public float displayHoldDuration = 2f;

        [Header("Exit")]
        [Tooltip("若指定则直接飞向该点。否则使用 exitDirection / exitDistance。")]
        public Transform exitAnchor;
        [Tooltip("退出移动方向。若使用局部空间则以 EventZone 的朝向为基准。")]
        public Vector3 exitDirection = new Vector3(-1f, 0f, 0f);
        [Tooltip("退出方向是否在本地空间中解释。")]
        public bool exitDirectionInLocalSpace = true;
        [Tooltip("是否改用相机的左侧方向（覆盖 exitDirection 设置）。")]
        public bool exitUsesCameraLeft = true;
        [Tooltip("从落点沿退出方向移动的距离（米）。")]
        public float exitDistance = 8f;

        [Header("Timing / Curves")]
        [Tooltip("落点阶段持续时间（秒）。")]
        public float settleDuration = 0.35f;
        [Tooltip("落点阶段的插值曲线 (0→1)。")]
        public AnimationCurve settleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("飞离阶段持续时间（秒）。")]
        public float exitDuration = 0.45f;
        [Tooltip("飞离阶段的插值曲线 (0→1)。")]
        public AnimationCurve exitCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Lifecycle")]
        [Tooltip("飞离完成后是否自动销毁卡牌。若关闭则只禁用卡牌。")]
        public bool destroyOnExit = true;
        [Tooltip("飞离后再延迟多少秒销毁/禁用卡牌。")]
        public float destroyAfterExitDelay = 3f;

        private Collider _collider;

        public static IReadOnlyList<EventPlayZone> ActiveZones => _activeZones;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            if (_collider == null)
            {
                Debug.LogError("EventPlayZone requires a Collider.", this);
                enabled = false;
                return;
            }
            _collider.isTrigger = true;
            TryResolveDisplayAnchor();
        }

        private void OnEnable()
        {
            if (!_activeZones.Contains(this)) _activeZones.Add(this);
            TryResolveDisplayAnchor();
        }

        private void OnDisable()
        {
            _activeZones.Remove(this);
        }

        public bool ContainsCard(CardView3D card)
        {
            if (card == null || _collider == null) return false;

            if (ContainsPoint(card.transform.position)) return true;

            if (card.TryGetWorldBounds(out var bounds))
            {
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                for (int ix = -1; ix <= 1; ix += 2)
                {
                    for (int iy = -1; iy <= 1; iy += 2)
                    {
                        for (int iz = -1; iz <= 1; iz += 2)
                        {
                            Vector3 corner = center + Vector3.Scale(extents, new Vector3(ix, iy, iz));
                            if (ContainsPoint(corner)) return true;
                        }
                    }
                }
            }

            return false;
        }

        public bool ContainsPoint(Vector3 worldPos)
        {
            if (_collider == null) return false;
            var bounds = _collider.bounds;

            // 投影到碰撞体中心高度，便于把“悬停在上方”的卡识别为命中
            Vector3 projected = new Vector3(worldPos.x, bounds.center.y, worldPos.z);
            if (bounds.Contains(projected)) return true;

            // 允许一定容差
            Vector3 closest = _collider.ClosestPoint(worldPos);
            float horizontalDist = Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(worldPos.x, worldPos.z));
            float tolerance = Mathf.Max(0.02f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.15f);
            return horizontalDist <= tolerance;
        }

        public void GetPlacementForCard(CardView3D card, out Vector3 landingPos, out Quaternion landingRot, out Vector3 exitPos)
        {
            Vector3 basePos;
            Quaternion baseRot;

            if (landingAnchor != null)
            {
                basePos = landingAnchor.position;
                baseRot = landingAnchor.rotation;
            }
            else if (_collider != null)
            {
                var b = _collider.bounds;
                basePos = new Vector3(b.center.x, b.max.y, b.center.z);
                baseRot = transform.rotation;
            }
            else
            {
                basePos = transform.position;
                baseRot = transform.rotation;
            }

            landingRot = overrideLandingRotation ? baseRot * Quaternion.Euler(landingRotationEuler) : (card != null ? card.transform.rotation : baseRot);
            landingPos = basePos + landingOffset;

            float minY, maxY;
            if (card != null)
            {
                card.GetPlacementExtents(landingRot, out minY, out maxY);
            }
            else
            {
                minY = -CardView3D.DefaultPlacementHalfThickness;
                maxY = CardView3D.DefaultPlacementHalfThickness;
            }
            float pivotToBottom = Mathf.Max(0f, -minY);
            landingPos += Vector3.up * pivotToBottom;

            exitPos = GetExitPosition(landingPos);
        }

        public bool TryGetDisplayPose(out Vector3 pos, out Quaternion rot)
        {
            if (displayAnchor == null && autoFindDisplayAnchor)
            {
                TryResolveDisplayAnchor();
            }

            if (displayAnchor != null)
            {
                pos = displayAnchor.position;
                rot = displayAnchor.rotation;
                return true;
            }

            pos = transform.position;
            rot = transform.rotation;
            return false;
        }

        public bool HasLandingCollider => _collider != null;

        public Vector3 GetExitPosition(Vector3 fromPosition)
        {
            if (exitAnchor != null)
            {
                return exitAnchor.position;
            }

            Vector3 dir;
            if (exitUsesCameraLeft && Camera.main != null)
            {
                dir = -Camera.main.transform.right;
            }
            else
            {
                dir = exitDirection.sqrMagnitude > 1e-6f ? exitDirection.normalized : Vector3.left;
                if (exitDirectionInLocalSpace) dir = transform.TransformDirection(dir);
            }
            return fromPosition + dir * Mathf.Max(0.01f, exitDistance);
        }

        public static EventPlayZone FindZoneForCard(CardView3D card)
        {
            EventPlayZone bestZone = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < _activeZones.Count; i++)
            {
                var zone = _activeZones[i];
                if (zone == null || !zone.isActiveAndEnabled) continue;
                if (!zone.ContainsCard(card)) continue;

                float dist = Vector3.SqrMagnitude(card.transform.position - zone.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestZone = zone;
                }
            }

            return bestZone;
        }

        private void TryResolveDisplayAnchor()
        {
            if (displayAnchor != null || !autoFindDisplayAnchor) return;
            if (string.IsNullOrEmpty(displayAnchorName)) return;
            var go = GameObject.Find(displayAnchorName);
            if (go != null) displayAnchor = go.transform;
        }
    }
}
