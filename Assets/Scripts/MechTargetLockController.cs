using System;
using UnityEngine;

// Run after the camera rig and Cinemachine have moved the gameplay camera.
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class MechTargetLockController : MonoBehaviour
{
    [SerializeField] float lockRange = 60f;
    [SerializeField] LayerMask targetLayers = ~0;
    [SerializeField, Range(0f, 0.49f)] float screenPadding = 0.04f;
    [Tooltip("Aim-point tolerance as a fraction of the camera's smaller screen dimension.")]
    [SerializeField, Range(0f, 0.25f)] float targetSwitchRadius = 0.025f;
    [SerializeField] Camera gameplayCamera;

    MechTargetable currentTarget;
    Collider[] candidates = new Collider[32];

    public MechTargetable CurrentTarget => currentTarget;
    public Camera GameplayCamera => gameplayCamera;
    public event Action<MechTargetable, MechTargetable> TargetChanged;

    void Awake()
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
    }

    void OnDisable()
    {
        SetTarget(null);
    }

    void LateUpdate()
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;

        SelectTarget(gameplayCamera);
    }

    void SelectTarget(Camera viewCamera)
    {
        if (viewCamera == null)
        {
            SetTarget(null);
            return;
        }

        MechTargetable nearestTarget = null;
        MechTargetable centeredTarget = null;
        MechTargetable rayTarget = null;
        float nearestScore = float.MaxValue;
        float centeredScore = float.MaxValue;
        float rayDistance = float.MaxValue;
        bool keepCurrentTarget = false;
        Vector2 screenCenter = viewCamera.pixelRect.center;
        float switchRadius = targetSwitchRadius * Mathf.Min(viewCamera.pixelWidth, viewCamera.pixelHeight);
        Ray centerRay = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int candidateCount = QueryCandidates();

        for (int i = 0; i < candidateCount; i++)
        {
            Collider candidate = candidates[i];
            MechTargetable targetable = candidate.GetComponentInParent<MechTargetable>();
            if (!IsValidTarget(targetable, viewCamera))
                continue;

            keepCurrentTarget |= targetable == currentTarget;
            Vector2 screenPoint = viewCamera.WorldToScreenPoint(targetable.AimPosition);
            float score = (screenPoint - screenCenter).sqrMagnitude;
            if (score < nearestScore)
            {
                nearestScore = score;
                nearestTarget = targetable;
            }

            if (score <= switchRadius * switchRadius && score < centeredScore)
            {
                centeredScore = score;
                centeredTarget = targetable;
            }

            // Crossing any part of an enemy's collider counts, not just its aim point.
            if (candidate.Raycast(centerRay, out RaycastHit hit, Mathf.Infinity)
                && hit.distance < rayDistance)
            {
                rayDistance = hit.distance;
                rayTarget = targetable;
            }
        }

        // Acquire automatically, then stay until center aim crosses another enemy
        // or the selected enemy leaves the screen/range.
        SetTarget(rayTarget != null ? rayTarget
            : centeredTarget != null ? centeredTarget
            : keepCurrentTarget ? currentTarget
            : nearestTarget);
        Array.Clear(candidates, 0, candidateCount);
    }

    int QueryCandidates()
    {
        // Reuse storage once warmed up, but grow rather than silently drop enemies
        // when a horde exceeds the initial capacity.
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(transform.position, lockRange,
            candidates, targetLayers, QueryTriggerInteraction.Ignore)) == candidates.Length)
        {
            Array.Resize(ref candidates, candidates.Length * 2);
        }
        return count;
    }

    void SetTarget(MechTargetable nextTarget)
    {
        // Reference equality also clears a destroyed Unity object and notifies listeners.
        if (ReferenceEquals(currentTarget, nextTarget))
            return;
        MechTargetable previousTarget = currentTarget;
        currentTarget = nextTarget;
        TargetChanged?.Invoke(previousTarget, nextTarget);
    }

    void OnValidate()
    {
        lockRange = Mathf.Max(0.01f, lockRange);
        screenPadding = Mathf.Clamp(screenPadding, 0f, 0.49f);
        targetSwitchRadius = Mathf.Clamp(targetSwitchRadius, 0f, 0.25f);
    }

    bool IsValidTarget(MechTargetable targetable, Camera viewCamera)
    {
        if (targetable == null || !targetable.isActiveAndEnabled
            || targetable.transform.IsChildOf(transform))
            return false;

        if ((targetable.AimPosition - transform.position).sqrMagnitude > lockRange * lockRange)
            return false;

        Vector3 viewportPosition = viewCamera.WorldToViewportPoint(targetable.AimPosition);
        // Padding affects acquisition only. Keep a selected enemy until it actually exits.
        float padding = targetable == currentTarget ? 0f : screenPadding;
        return viewportPosition.z >= viewCamera.nearClipPlane
            && viewportPosition.z <= viewCamera.farClipPlane
            && viewportPosition.x >= padding && viewportPosition.x <= 1f - padding
            && viewportPosition.y >= padding && viewportPosition.y <= 1f - padding;
    }
}
