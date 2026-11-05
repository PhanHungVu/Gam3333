using UnityEngine;
using UnityEngine.UI;

// The reticle that follows the locked target (or the screen centre). It also owns the cursor art:
// lock-on mode shows the lock-on reticle sprite, everything else shows the plain cursor sprite.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class LockOnCursor : MonoBehaviour
{
    [SerializeField] RectTransform cursorRect;
    [SerializeField] Canvas canvas;
    [SerializeField] Camera gameplayCamera;
    [SerializeField] MechTargetLockController targetLockController;
    [SerializeField] bool showAtScreenCenterWhenUnlocked = true;
    [Header("Cursor Art")]
    [Tooltip("Image that draws the cursor. Defaults to the Image on this object.")]
    [SerializeField] Image cursorImage;
    [Tooltip("Sprite used while the camera is not in lock-on mode.")]
    [SerializeField] Sprite freeCursorSprite;
    [Tooltip("Sprite used while lock-on (hard lock) mode is on.")]
    [SerializeField] Sprite lockOnSprite;

    CanvasGroup cursorVisibility;

    public Sprite CurrentSprite => cursorImage != null ? cursorImage.sprite : null;
    public bool IsLockOn => targetLockController != null && targetLockController.IsHardLockMode;

    public void Bind(MechTargetLockController controller, Camera viewCamera)
    {
        targetLockController = controller;
        gameplayCamera = viewCamera;
        ApplyCursorSprite();
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
        if (cursorImage == null && cursorRect != null)
            cursorImage = cursorRect.GetComponent<Image>();
        if (cursorImage == null && cursorRect != null)
            cursorImage = cursorRect.GetComponentInChildren<Image>();
        if (cursorRect != null)
        {
            cursorVisibility = cursorRect.GetComponent<CanvasGroup>();
            if (cursorVisibility == null)
                cursorVisibility = cursorRect.gameObject.AddComponent<CanvasGroup>();
            cursorVisibility.blocksRaycasts = false;
            cursorVisibility.interactable = false;
        }
        ApplyCursorSprite();
    }

    void LateUpdate()
    {
        if (cursorRect == null)
            return;
        ApplyCursorSprite();
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

    // Lock-on mode has its own reticle art; free aim keeps the plain cursor. Only the sprite is
    // swapped, so the image's colour and size stay as the designer set them.
    void ApplyCursorSprite()
    {
        if (cursorImage == null) return;
        Sprite wanted = IsLockOn ? lockOnSprite : freeCursorSprite;
        if (wanted != null && cursorImage.sprite != wanted) cursorImage.sprite = wanted;
    }

    void OnDisable()
    {
        if (cursorVisibility != null)
            cursorVisibility.alpha = 0f;
    }
}
