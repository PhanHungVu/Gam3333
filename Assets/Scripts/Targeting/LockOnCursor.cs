using UnityEngine;

[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class LockOnCursor : MonoBehaviour
{
    [SerializeField] RectTransform cursorRect;
    [SerializeField] Canvas canvas;
    [SerializeField] Camera gameplayCamera;
    [SerializeField] MechTargetLockController targetLockController;
    [SerializeField] bool showAtScreenCenterWhenUnlocked = true;

    CanvasGroup cursorVisibility;

    public void Bind(MechTargetLockController controller, Camera viewCamera)
    {
        targetLockController = controller;
        gameplayCamera = viewCamera;
    }

    void Awake()
    {
        if (cursorRect == null)
            cursorRect = GetComponent<RectTransform>();
        if (canvas == null && cursorRect != null)
            canvas = cursorRect.GetComponentInParent<Canvas>();
        if (targetLockController == null)
            targetLockController = FindAnyObjectByType<MechTargetLockController>();
        if (gameplayCamera == null)
            gameplayCamera = targetLockController != null && targetLockController.GameplayCamera != null
                ? targetLockController.GameplayCamera : Camera.main;
        if (cursorRect != null)
        {
            cursorVisibility = cursorRect.GetComponent<CanvasGroup>();
            if (cursorVisibility == null)
                cursorVisibility = cursorRect.gameObject.AddComponent<CanvasGroup>();
            cursorVisibility.blocksRaycasts = false;
            cursorVisibility.interactable = false;
        }
    }

    void LateUpdate()
    {
        if (cursorRect == null)
            return;
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
        if (gameplayCamera == null)
        {
            cursorVisibility.alpha = 0f;
            return;
        }

        Vector2 screenPosition = gameplayCamera.pixelRect.center;
        MechTargetable target = targetLockController != null && targetLockController.isActiveAndEnabled
            ? targetLockController.CurrentTarget : null;
        bool hasVisibleTarget = false;
        if (target != null && target.isActiveAndEnabled)
        {
            Vector3 projected = gameplayCamera.WorldToScreenPoint(target.AimPosition);
            hasVisibleTarget = projected.z >= gameplayCamera.nearClipPlane
                && projected.z <= gameplayCamera.farClipPlane
                && gameplayCamera.pixelRect.Contains(new Vector2(projected.x, projected.y));
            if (hasVisibleTarget)
                screenPosition = projected;
        }

        // Hide the graphic, not this GameObject: disabling it prevents reacquisition.
        cursorVisibility.alpha = hasVisibleTarget || showAtScreenCenterWhenUnlocked ? 1f : 0f;
        Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        RectTransform parentRect = cursorRect.parent as RectTransform;
        if (parentRect != null
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, screenPosition, canvasCamera, out Vector2 localPosition))
        {
            cursorRect.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
        }
        else
        {
            cursorRect.position = screenPosition;
        }
    }

    void OnDisable()
    {
        if (cursorVisibility != null)
            cursorVisibility.alpha = 0f;
    }
}
