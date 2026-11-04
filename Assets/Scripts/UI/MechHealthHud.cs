using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
[DefaultExecutionOrder(350)]
public sealed class MechHealthHud : MonoBehaviour
{
    [Header("Player Binding")]
    [Tooltip("Assign the scene player's CombatHealth. If empty, auto binding looks for MechPlayerRig, never an enemy.")]
    [SerializeField] CombatHealth health;
    [SerializeField] bool autoFindPlayer = true;
    CombatHealth subscribedHealth;
    EquipManager equipment;
    float nextPlayerSearch;
    [Header("Editable UI References")]
    [SerializeField] Canvas canvas;
    [Tooltip("Hides only the health meter, leaving the shared Canvas and movement UI active.")]
    [SerializeField] CanvasGroup visibility;
    [Tooltip("Supports a horizontally Filled Image or a regular/Sliced Image that shrinks with health.")]
    [SerializeField] Image fill;
    [Tooltip("Glow stretches horizontally with health; the front Image is cropped independently.")]
    [SerializeField] Image glow;
    [Tooltip("Lost-health bar behind the front Image: it holds the health just lost, then drains into the front.")]
    [SerializeField] Image whiteBar;
    [SerializeField] Text label;
    [Tooltip("Optional replacement for the legacy Text label. Either label may be left empty.")]
    [SerializeField] TMP_Text tmpLabel;
    [Header("Lost Health (white bar)")]
    [Tooltip("Seconds without new damage before the lost-health bar starts draining.")]
    [SerializeField, Min(0f)] float whiteBarDelay = 0.5f;
    [Tooltip("Lost-health drain speed in Fill Amount per second. 0.4 empties a full bar in 2.5 s.")]
    [SerializeField, Min(0.01f)] float whiteBarDrainSpeed = 0.4f;
    [Header("Fill Tint")]
    [Tooltip("Disable to preserve the designer's Image colour and sprite appearance.")]
    [SerializeField] bool tintFillByHealth = true;
    [SerializeField] Color healthyColor = new Color(0.3f, 0.95f, 0.5f);
    [SerializeField] Color lowHealthColor = new Color(1f, 0.3f, 0.25f);
    [SerializeField, Range(0f, 1f)] float lowHealthThreshold = 0.25f;
    // Ratios are kept here so both bars work with Filled images (cropped) and plain ones (shrunk).
    float frontFill = 1f;
    float lostFill = 1f;
    float lastDamageTime = float.NegativeInfinity;
    public Canvas Canvas => canvas;
    public CombatHealth Health => health;
    public bool IsVisible => visibility != null ? visibility.alpha > 0f : canvas != null && canvas.enabled;
    public float NormalizedHealth => frontFill;
    public float LostHealth => lostFill;
    public string Readout => tmpLabel != null ? tmpLabel.text : label != null ? label.text : string.Empty;

    void Awake()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (visibility == null) visibility = GetComponent<CanvasGroup>();
        if (whiteBar == null)
        {
            // Keeps older copies of the prefab working without the reference being wired by hand.
            var found = transform.Find("BarWhite");
            if (found != null) whiteBar = found.GetComponent<Image>();
        }
        if (canvas == null || fill == null)
            Debug.LogWarning("Place Player Health HUD under a Canvas and assign its BarFront Image.", this);
        FindPlayerIfNeeded();
        Refresh();
    }
    void OnEnable()
    {
        FindPlayerIfNeeded();
        UpdateBinding();
        Refresh();
    }
    void OnDisable()
    {
        Unsubscribe();
        SetVisible(false);
    }
    public void Bind(CombatHealth playerHealth)
    {
        health = playerHealth;
        UpdateBinding();
        Refresh();
    }
    void LateUpdate()
    {
        if (health == null && autoFindPlayer && Time.unscaledTime >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.unscaledTime + 0.5f;
            FindPlayerIfNeeded();
        }
        UpdateBinding();
        UpdateVisibility();
        TickLostHealth();
    }
    void FindPlayerIfNeeded()
    {
        if (health != null || !autoFindPlayer) return;
        var player = FindAnyObjectByType<MechPlayerRig>();
        if (player != null) health = player.GetComponent<CombatHealth>();
    }
    void UpdateBinding()
    {
        if (!isActiveAndEnabled || subscribedHealth == health) return;
        Unsubscribe();
        subscribedHealth = health;
        if (subscribedHealth != null)
        {
            subscribedHealth.Damaged += OnDamaged;
            subscribedHealth.AvailabilityChanged += OnAvailabilityChanged;
            equipment = subscribedHealth.GetComponent<EquipManager>();
            if (equipment != null) equipment.EquipmentChanged += Refresh;
        }
        Refresh();
    }
    void Unsubscribe()
    {
        if (subscribedHealth != null)
        {
            subscribedHealth.Damaged -= OnDamaged;
            subscribedHealth.AvailabilityChanged -= OnAvailabilityChanged;
        }
        if (equipment != null) equipment.EquipmentChanged -= Refresh;
        subscribedHealth = null; equipment = null;
    }
    void OnAvailabilityChanged(bool _) => Refresh();
    void UpdateVisibility()
    {
        SetVisible(isActiveAndEnabled && health != null && health.isActiveAndEnabled && !health.IsDead);
    }
    void SetVisible(bool visible)
    {
        if (visibility != null) visibility.alpha = visible ? 1f : 0f;
        // Compatibility for an older standalone Canvas; never disable a shared parent.
        else if (canvas != null && canvas.transform == transform) canvas.enabled = visible;
    }
    void OnDamaged(float _) => Refresh();
    void Refresh()
    {
        UpdateVisibility();
        if (health == null) return;
        float ratio = Mathf.Clamp01(health.CurrentHealth / Mathf.Max(1f, health.MaximumHealth));
        // Damage: the lost-health bar keeps the ratio the front had before this hit. Healing snaps
        // both bars up together, because there is no lost health left over to show.
        if (ratio < frontFill) lastDamageTime = Time.unscaledTime;
        else lostFill = ratio;
        frontFill = ratio;
        if (lostFill < frontFill) lostFill = frontFill;
        if (fill != null)
        {
            SetFill(fill, frontFill);
            if (tintFillByHealth) fill.color = frontFill <= lowHealthThreshold ? lowHealthColor : healthyColor;
        }
        if (glow != null)
        {
            Vector3 scale = glow.rectTransform.localScale;
            glow.rectTransform.localScale = new Vector3(frontFill, scale.y, scale.z);
        }
        SetFill(whiteBar, lostFill);
        string readout = "HEALTH  " + Mathf.CeilToInt(health.CurrentHealth) + " / " + Mathf.CeilToInt(health.MaximumHealth);
        if (label != null) label.text = readout;
        if (tmpLabel != null) tmpLabel.text = readout;
    }
    // The lost-health bar waits for a moment without damage, then drains into the front bar at a
    // fixed rate instead of snapping, so a hit reads as one lost chunk fading away.
    void TickLostHealth()
    {
        if (whiteBar == null || lostFill <= frontFill) return;
        if (Time.unscaledTime - lastDamageTime < whiteBarDelay) return;
        lostFill = Mathf.MoveTowards(lostFill, frontFill, whiteBarDrainSpeed * Time.unscaledDeltaTime);
        SetFill(whiteBar, lostFill);
    }
    static void SetFill(Image image, float amount)
    {
        if (image == null) return;
        if (image.type == Image.Type.Filled) image.fillAmount = amount;
        else image.rectTransform.anchorMax = new Vector2(amount, image.rectTransform.anchorMax.y);
    }
}
