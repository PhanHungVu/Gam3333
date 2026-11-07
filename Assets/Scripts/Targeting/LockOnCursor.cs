using UnityEngine;
using UnityEngine.UI;

// The reticle that follows the locked target (or the screen centre). It also owns the cursor art:
// camera lock-on (MMB) shows its own reticle, an enemy the cursor has locked onto by itself shows the
// lock-on reticle, and everything else shows the plain cursor.
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
    [Tooltip("Sprite used while nothing is locked and the camera is free.")]
    [SerializeField] Sprite freeCursorSprite;
    [Tooltip("Sprite used while the cursor is automatically locked onto an enemy.")]
    [SerializeField] Sprite lockOnSprite;
    [Tooltip("Sprite used while camera lock is toggled on with MMB.")]
    [SerializeField] Sprite camLockOnSprite;
    [Header("Cursor Sounds")]
    [Tooltip("StoneHit1: plays when the cursor changes to the free cursor sprite.")]
    public AudioClip StoneHit1;
    [Tooltip("StoneHit2: plays when the cursor changes to the lock-on sprite.")]
    public AudioClip StoneHit2;
    [Tooltip("StoneHit3: plays when the cursor changes to the camera lock-on sprite.")]
    public AudioClip StoneHit3;
    [Tooltip("Seconds before another cursor sound may play. The sprite still changes immediately.")]
    [SerializeField, Min(0f)] float stoneCooldown = 1.5f;

    CanvasGroup cursorVisibility;
    AudioSource stoneVoice;
    float nextStoneTime;
    bool reportedMissingCamera;

    public Sprite CurrentSprite => cursorImage != null ? cursorImage.sprite : null;
    // Camera lock mode, then an automatically acquired enemy, then the plain cursor.
    public bool IsCamLock => targetLockController != null && targetLockController.IsHardLockMode;
    public bool IsLockedOn => targetLockController != null && !targetLockController.IsFreeAim
        && targetLockController.CurrentTarget != null;

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
        if (gameplayCamera == null) gameplayCamera = FindMainCamera();
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
        if (gameplayCamera == null) gameplayCamera = FindMainCamera();
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

    // Camera lock-on outranks the automatic lock so pressing F always reads as the camera reticle.
    // Only the sprite is swapped, so the image's colour and size stay as the designer set them, and a
    // stone hit plays for the sprite the cursor moves to: free, lock-on or camera lock-on.
    void ApplyCursorSprite()
    {
        if (cursorImage == null) return;
        Sprite wanted = IsCamLock ? camLockOnSprite : IsLockedOn ? lockOnSprite : freeCursorSprite;
        if (wanted == null || cursorImage.sprite == wanted) return;
        cursorImage.sprite = wanted;
        PlayStone(wanted == camLockOnSprite ? StoneHit3 : wanted == lockOnSprite ? StoneHit2 : StoneHit1);
    }

    // One 2D voice on the cursor, so the hit does not depend on where the gameplay camera sits.
    // Sounds are rate-limited to one per stoneCooldown; the sprite itself still changes on every swap.
    void PlayStone(AudioClip clip)
    {
        if (clip == null) return;
        if (Time.unscaledTime < nextStoneTime) return;
        nextStoneTime = Time.unscaledTime + stoneCooldown;
        if (stoneVoice == null)
        {
            stoneVoice = gameObject.AddComponent<AudioSource>();
            stoneVoice.playOnAwake = false;
            stoneVoice.loop = false;
            stoneVoice.spatialBlend = 0f;
        }
        stoneVoice.PlayOneShot(clip);
    }

    // The cursor draws in screen space, so it needs the scene's gameplay camera. It finds the Main
    // Camera prefab instance itself: by name first, then by the MainCamera tag, then by the camera
    // rig that prefab carries. A scene without one is reported once, not every frame.
    Camera FindMainCamera()
    {
        Camera camera = null;
        var named = GameObject.Find("Main Camera");
        if (named != null) camera = named.GetComponentInChildren<Camera>(true);
        if (camera == null) camera = Camera.main;
        if (camera == null)
        {
            var rig = FindAnyObjectByType<CameraControlThirdPerson>();
            if (rig != null) camera = rig.GetComponentInChildren<Camera>(true);
        }
        if (camera == null && !reportedMissingCamera)
        {
            reportedMissingCamera = true;
            Debug.Log("Main Camera prefab not found.", this);
        }
        else if (camera != null)
        {
            reportedMissingCamera = false;
        }
        return camera;
    }

    void OnDisable()
    {
        if (cursorVisibility != null)
            cursorVisibility.alpha = 0f;
    }
}
