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
    [Tooltip("Supports a horizontally Filled Image or a regular/Sliced Image that shrinks with health.")]
    [SerializeField] Image fill;
    [SerializeField] Text label;
    [Tooltip("Optional replacement for the legacy Text label. Either label may be left empty.")]
    [SerializeField] TMP_Text tmpLabel;
    [Header("Fill Tint")]
    [Tooltip("Disable to preserve the designer's Image colour and sprite appearance.")]
    [SerializeField] bool tintFillByHealth = true;
    [SerializeField] Color healthyColor = new Color(0.3f, 0.95f, 0.5f);
    [SerializeField] Color lowHealthColor = new Color(1f, 0.3f, 0.25f);
    [SerializeField, Range(0f, 1f)] float lowHealthThreshold = 0.25f;
    public Canvas Canvas => canvas;
    public CombatHealth Health => health;
    public float NormalizedHealth => fill == null ? 0f
        : fill.type == Image.Type.Filled ? fill.fillAmount : fill.rectTransform.anchorMax.x;
    public string Readout => tmpLabel != null ? tmpLabel.text : label != null ? label.text : string.Empty;

    void Awake()
    {
        if (canvas == null || fill == null)
            Debug.LogWarning("Assign Canvas and Fill on the standalone Player Health HUD prefab.", this);
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
        if (canvas != null) canvas.enabled = false;
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
        if (canvas != null) canvas.enabled = isActiveAndEnabled && health != null
            && health.isActiveAndEnabled && !health.IsDead;
    }
    void OnDamaged(float _) => Refresh();
    void Refresh()
    {
        UpdateVisibility();
        if (health == null) return;
        float ratio = Mathf.Clamp01(health.CurrentHealth / Mathf.Max(1f, health.MaximumHealth));
        if (fill != null)
        {
            if (fill.type == Image.Type.Filled) fill.fillAmount = ratio;
            else fill.rectTransform.anchorMax = new Vector2(ratio, fill.rectTransform.anchorMax.y);
            if (tintFillByHealth) fill.color = ratio <= lowHealthThreshold ? lowHealthColor : healthyColor;
        }
        string readout = "HEALTH  " + Mathf.CeilToInt(health.CurrentHealth) + " / " + Mathf.CeilToInt(health.MaximumHealth);
        if (label != null) label.text = readout;
        if (tmpLabel != null) tmpLabel.text = readout;
    }
}
