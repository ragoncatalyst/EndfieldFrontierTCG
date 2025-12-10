using System.Collections;
using UnityEngine;

// Handles two-phase return, home pose storage and related utilities
public partial class CardView3D : MonoBehaviour
{
    // Public API: store a home pose (world pos/rot)
    public void SetHomePose(Vector3 pos, Quaternion rot)
    {
        _handRestPosition = pos;
        _handRestPosition.y = GetFinalHandY();
        _homeParent = null;
        _homePos = _handRestPosition;
        _homeRot = Quaternion.Euler(rot.eulerAngles.x, rot.eulerAngles.y, 0f);
        _homeLocalPos = pos;
        _homeLocalRot = _homeRot;
        _homeSet = true;
    }

    // Store home from zone, but normalize Y to canonical finalY
    public void SetHomeFromZone(Transform zone, Vector3 worldPos, Quaternion worldRot)
    {
        _homeParent = zone;
        if (zone != null)
        {
            _homeLocalPos = zone.InverseTransformPoint(worldPos);
            _homeLocalRot = Quaternion.Inverse(zone.rotation) * worldRot;
        }
        else
        {
            _homeLocalPos = worldPos;
            _homeLocalRot = worldRot;
        }
        Vector3 adjusted = new Vector3(worldPos.x, GetFinalHandY(), worldPos.z);
        _homePos = adjusted;
        _homeRot = Quaternion.Euler(worldRot.eulerAngles.x, worldRot.eulerAngles.y, 0f);
        _homeSet = true;
    }

    // Get world home pose (updates cached if parent present)
    private void GetHomeWorldPose(out Vector3 pos, out Quaternion rot)
    {
        if (_homeParent != null && _homeParent.gameObject.activeInHierarchy)
        {
            pos = _homeParent.TransformPoint(_homeLocalPos);
            rot = _homeParent.rotation * _homeLocalRot;
            _homePos = pos; _homeRot = rot;
        }
        else
        {
            pos = _homePos; rot = _homeRot;
        }
    }

    public (Vector3, Quaternion) GetHomePose() { GetHomeWorldPose(out Vector3 p, out Quaternion r); return (p, r); }

    public void SnapTo(Vector3 pos, Quaternion rot)
    {
        pos.y = GetFinalHandY();
        _handRestPosition = pos;
        StopAllCoroutines();
        transform.position = pos;
        transform.rotation = rot;
        if (body != null) { body.isKinematic = true; body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll; }
        SetHomePose(pos, rot);
        _state = DragState.Idle;
    }

    public void BeginSmoothReturnToHome(float aheadZ, float phase1Time, float phase2Time)
    {
        if (!_homeSet) return;
        if (_returnHomeCo != null) StopCoroutine(_returnHomeCo);
        _returnHomeCo = StartCoroutine(ReturnToHomeTwoPhase(aheadZ, Mathf.Max(0.01f, phase1Time), Mathf.Max(0.01f, phase2Time)));
    }

    public void ReturnHomeUnified()
    {
        // prefer HandSplineZone, but fall back to local return
        var zone = GetComponentInParent<EndfieldFrontierTCG.Hand.HandSplineZone>();
        try { if (zone != null && zone.TryReturnCardToHome(this)) return; } catch {}
        if (_homeSet) BeginSmoothReturnToHome(0.15f, returnPhase1Duration, returnPhase2Duration);
        else StartCoroutine(ReleaseDrop());
    }

    public void ForceReturnYNow() { _forceReturnYNow = true; }

    private IEnumerator ReturnToHomeTwoPhase(float aheadZ, float t1, float t2)
    {
        IsReturningHome = true;
        Vector3 target = _handRestPosition;
        float savedTargetY = _targetY;
        SetTargetY(0f);
        float finalY = GetFinalHandY();
        target.y = finalY;

        // Phase 1: XZ move towards aheadZ offset while Y becomes finalY
        float t = 0f;
        Vector3 start = transform.position;
        Vector3 temp = new Vector3(target.x, finalY, target.z + aheadZ);
        while (t < t1)
        {
            if (_forceReturnYNow)
            {
                finalY = GetFinalHandY();
                target.y = finalY; _handRestPosition.y = finalY; _homePos.y = finalY; _forceReturnYNow = false;
            }
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / t1);
            transform.position = new Vector3(Mathf.Lerp(start.x, temp.x, a), Mathf.Lerp(start.y, finalY, a), Mathf.Lerp(start.z, temp.z, a));
            yield return null;
        }

        // Phase 2: move Z back to target.z and set rotation
        t = 0f; start = transform.position;
        while (t < t2)
        {
            if (_forceReturnYNow)
            {
                finalY = GetFinalHandY();
                target.y = finalY; _handRestPosition.y = finalY; _homePos.y = finalY; _forceReturnYNow = false;
            }
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / t2);
            transform.position = new Vector3(Mathf.Lerp(start.x, target.x, a), finalY, Mathf.Lerp(start.z, target.z, a));
            yield return null;
        }

        transform.position = new Vector3(target.x, finalY, target.z);
        SetTargetY(savedTargetY);
        IsReturningHome = false; _returnHomeCo = null; _state = DragState.Idle; yield return null;
    }

    private IEnumerator ReleaseDrop()
    {
        Vector3 start = transform.position;
        Vector3 end = new Vector3(transform.position.x, 0f, transform.position.z);
        Quaternion startR = transform.rotation;
        Quaternion endR = Quaternion.Euler(90f, transform.rotation.eulerAngles.y, 0f);
        float dur = 0.2f; float t = 0f; Vector3 vel = Vector3.zero;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            Vector3 targ = Vector3.Lerp(start, end, p);
            transform.position = Vector3.SmoothDamp(transform.position, targ, ref vel, 0.05f);
            transform.rotation = Quaternion.Slerp(startR, endR, p);
            yield return null;
        }
        transform.position = end; transform.rotation = endR; _state = DragState.Idle; yield return null;
    }
}
