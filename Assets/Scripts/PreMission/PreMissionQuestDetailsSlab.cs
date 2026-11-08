using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Scene-owned, physical replacement for the paper briefing. Navigation/data stay
// in PreMissionFlowController; the artist's clips own all slab/button motion.
[DefaultExecutionOrder(100), DisallowMultipleComponent]
public sealed class PreMissionQuestDetailsSlab : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] Transform animatedSlab;
    [SerializeField] AnimationClip fallClip;
    [Header("Briefing arrival audio")]
    [SerializeField] AudioSource appearanceAudio;
    [SerializeField] AudioClip appearanceSound;
    [SerializeField, Range(0f, 1f)] float appearanceVolume = 1f;
    [SerializeField] TMP_Text titleText, questTypeText, descriptionText;
    [SerializeField] PreMissionQuestSlabAction acceptAction, declineAction;
    [Tooltip("The former briefing area in normalized camera coordinates.")]
    [SerializeField] Rect viewportArea = new Rect(.52f, .06f, .46f, .88f);
    [Tooltip("Resting presentation height above the board, clear of its decorative terrain.")]
    [SerializeField] float tabletopHeight = 2.4f;
    [SerializeField] Bounds restingBounds = new Bounds(Vector3.zero, new Vector3(6.2f, 1.6f, 6.2f));
    PreMissionFlowController flow;
    PreMissionStoneBoard board;
    Renderer[] renderers;
    bool shown, interactive;
    Vector2 lastPointer;
    PreMissionQuestSlabAction keyboardAction;
    public bool IsShown => shown;
    public bool IsInteractive => interactive && shown && isActiveAndEnabled
        && flow != null && flow.isActiveAndEnabled && flow.Session != null
        && flow.Session.View == PreMissionView.Details && !flow.IsTransitioning
        && board != null && board.IsOperational;
    public TMP_Text TitleText => titleText;
    public TMP_Text QuestTypeText => questTypeText;
    public TMP_Text DescriptionText => descriptionText;
    public PreMissionQuestSlabAction AcceptAction => acceptAction;
    public PreMissionQuestSlabAction DeclineAction => declineAction;
    public Transform AnimatedSlab => animatedSlab;
    public Animator Animator => animator;
    bool ReducedMotion => board != null && board.Motion != null && board.Motion.Feedback != null && board.Motion.Feedback.ReducedMotion;

    public void Bind(PreMissionFlowController owner, PreMissionStoneBoard surface)
    {
        flow = owner; board = surface;
        renderers = animatedSlab.GetComponentsInChildren<Renderer>(true);
        acceptAction.Bind(this); declineAction.Bind(this);
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Hide();
    }
    public void SetContent(string title, string type, string description, string info, string reward)
    {
        titleText.text = title; questTypeText.text = type;
        descriptionText.text = string.Join("\n\n", new[] { description, info, reward });
    }
    public IEnumerator Enter()
    {
        bool appearing = !shown;
        SetInteractive(false); shown = true; SetVisible(true); RefreshPlacement();
        ResetButtons(); Play("questDescSlabAnimFall", 0);
        if (appearing && appearanceAudio != null && appearanceSound != null)
        {
            appearanceAudio.Stop();
            appearanceAudio.PlayOneShot(appearanceSound, appearanceVolume);
        }
        float elapsed = 0, duration = Mathf.Max(.01f, fallClip.length);
        while (elapsed < duration && !ReducedMotion)
        {
            yield return null;
            elapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
        }
        Play("questDescSlabAnimIdle", 0);
    }
    public void Settle(bool visible)
    {
        if (this == null || animatedSlab == null || acceptAction == null || declineAction == null) return;
        visible &= isActiveAndEnabled;
        shown = visible; SetInteractive(false); ResetButtons();
        Play(visible ? "questDescSlabAnimIdle" : "questDescSlabAnimPreFall", 0);
        SetVisible(visible); RefreshPlacement();
    }
    public void Hide() => Settle(false);
    public void SetInteractive(bool value)
    {
        if (this == null) return;
        interactive = value;
        if (acceptAction != null) acceptAction.SetAvailable(value && shown);
        if (declineAction != null) declineAction.SetAvailable(value && shown);
        if (!value) ResetButtons();
    }
    void SetVisible(bool value)
    {
        if (renderers == null) renderers = animatedSlab.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers) if (renderer != null) renderer.enabled = value;
    }
    void ResetButtons()
    {
        keyboardAction = null;
        if (acceptAction == null || declineAction == null) return;
        if (EventSystem.current != null && (EventSystem.current.currentSelectedGameObject == acceptAction.gameObject
            || EventSystem.current.currentSelectedGameObject == declineAction.gameObject)) EventSystem.current.SetSelectedGameObject(null);
        acceptAction.ResetHover(); declineAction.ResetHover();
        Play("questDescAcceptBtnIdle", 1); Play("questDescDeclineBtnIdle", 2);
    }
    void Play(string state, int layer)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
        animator.Play(state, layer, 0); animator.Update(0);
    }
    public void Hover(PreMissionQuestSlabAction action, bool value)
    {
        if (!IsInteractive) value = false;
        string prefix = action == acceptAction ? "questDescAcceptBtn" : "questDescDeclineBtn";
        Play(prefix + (value ? (ReducedMotion ? "HIdle" : "Highlight") : "Idle"), action == acceptAction ? 1 : 2);
    }
    public void Invoke(PreMissionQuestSlabAction action)
    {
        if (!IsInteractive) return;
        SetInteractive(false);
        if (action == acceptAction) flow.AcceptQuest(); else flow.Back();
    }
    void Update()
    {
        if (!IsInteractive) return;
        var mouse = Mouse.current;
        if (mouse != null && (mouse.position.ReadValue() - lastPointer).sqrMagnitude > 1)
        {
            keyboardAction = null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
        if (mouse != null) lastPointer = mouse.position.ReadValue();
        var keyboard = Keyboard.current; if (keyboard == null) return;
        if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame)
        {
            keyboardAction = keyboard.leftArrowKey.wasPressedThisFrame ? declineAction
                : keyboard.rightArrowKey.wasPressedThisFrame ? acceptAction : keyboardAction == declineAction ? acceptAction : declineAction;
            EventSystem.current?.SetSelectedGameObject(keyboardAction.gameObject);
        }
        // The EventSystem submits a keyboard-selected action. Without a selected
        // control, Enter retains the old default Accept behavior.
        if (keyboardAction == null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Invoke(acceptAction);
    }
    void LateUpdate()
    {
        if (!shown) return;
        RefreshPlacement();
        // Camera fitting and authored button clips both move colliders. UI runs
        // on unscaled time, including when no physics FixedUpdate is happening.
        Physics.SyncTransforms();
    }
    public void RefreshPlacement()
    {
        if (board == null || board.BoardCamera == null) return;
        var camera = board.BoardCamera; var rotation = board.transform.rotation;
        // Keep the authored slab parallel to the tabletop. Only its outer anchor
        // adapts to the camera; animation curves retain their original space.
        transform.rotation = rotation;
        Vector3 right = camera.transform.right, up = camera.transform.up;
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
        {
            var point = rotation * (restingBounds.center + Vector3.Scale(restingBounds.extents, new Vector3(x, y, z)));
            var projected = new Vector2(Vector3.Dot(point, right), Vector3.Dot(point, up));
            min = Vector2.Min(min, projected); max = Vector2.Max(max, projected);
        }
        float scale = Mathf.Min(camera.orthographicSize * 2 * camera.aspect * viewportArea.width / Mathf.Max(.01f, max.x - min.x),
            camera.orthographicSize * 2 * viewportArea.height / Mathf.Max(.01f, max.y - min.y));
        transform.localScale = Vector3.one * scale;
        var plane = new Plane(board.transform.up, board.transform.TransformPoint(Vector3.up * tabletopHeight));
        var ray = camera.ViewportPointToRay(viewportArea.center);
        if (plane.Raycast(ray, out float distance)) transform.position = ray.GetPoint(distance) - rotation * restingBounds.center * scale;
    }
    void OnDisable()
    {
        if (!Application.isPlaying) return;
        if (appearanceAudio != null) appearanceAudio.Stop();
        if (animator != null && acceptAction != null && declineAction != null && animatedSlab != null) Hide();
        if (flow != null && flow.isActiveAndEnabled && flow.Session != null) flow.RecoverPresentation();
    }
    void OnEnable()
    {
        if (Application.isPlaying && flow != null && flow.isActiveAndEnabled && flow.Session != null) flow.RecoverPresentation();
    }
}
