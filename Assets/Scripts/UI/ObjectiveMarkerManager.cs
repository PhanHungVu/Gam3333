using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One marker per main objective in the level. The objectives are the level's chunks, named
// "Chunk_<objective>" (Chunk_KOTH, Chunk_Escort, Chunk_End...), and the player's spawn chunk is
// skipped. Each marker sits on its objective while that objective is inside the camera view; when
// it leaves the view the marker drops to half size and pins itself to the screen edge nearest the
// objective, so it keeps pointing the player the right way until the objective comes back into view.
[DisallowMultipleComponent]
[DefaultExecutionOrder(210)]
public sealed class ObjectiveMarkerManager : MonoBehaviour
{
    [Header("Marker")]
    [Tooltip("Marker prefab, instanced once per objective. Its distance text is filled in at runtime.")]
    [SerializeField] RectTransform markerPrefab;
    [Header("Objectives")]
    [Tooltip("Level objects named with this prefix are main objectives, e.g. Chunk_KOTH.")]
    [SerializeField] string objectivePrefix = "Chunk_";
    [Tooltip("Chunks that belong to the level but are not objectives, such as the player's spawn.")]
    [SerializeField] string[] ignoredNames = { "Chunk_Start" };
    [Tooltip("Seconds to keep looking for objectives while the level is still being generated.")]
    [SerializeField, Min(0f)] float searchSeconds = 8f;
    [Header("Placement")]
    [Tooltip("Screen border kept free, as a fraction of the screen.")]
    [SerializeField, Range(0f, 0.4f)] float edgeMargin = 0.08f;
    [Tooltip("Scale used while the objective is off screen. 0.5 is half size.")]
    [SerializeField, Range(0.1f, 1f)] float offScreenScale = 0.5f;
    [Header("Icons")]
    [Tooltip("Marker sprites picked by objective name: KOTH uses the sprite named objectiveMarkerKOTH.")]
    [SerializeField] Sprite[] icons;
    [SerializeField] string iconPrefix = "objectiveMarker";
    [Header("Camera")]
    [SerializeField] Camera gameplayCamera;

    sealed class Marker
    {
        public Transform target;
        public RectTransform rect;
        public Image icon;
        public TMP_Text distance;
    }

    // Only one manager drives markers, in case a scene carries one as well as this canvas.
    static ObjectiveMarkerManager active;

    readonly List<Marker> markers = new List<Marker>();
    Canvas canvas;
    Transform player;
    float searchUntil;
    float nextSearch;

    public bool HasMarkers => markers.Count > 0;

    void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        // Authored inside the cursor prefab, this object must not inherit the cursor's fade or tag
        // along with the cursor every frame, so it moves up to the canvas root before spawning.
        if (canvas != null && transform.parent != canvas.transform)
            transform.SetParent(canvas.transform, false);
        if (gameplayCamera == null) gameplayCamera = Camera.main;
    }

    void OnEnable()
    {
        if (active != null && active != this) { enabled = false; return; }
        active = this;
    }

    void OnDisable()
    {
        if (active == this) active = null;
        ClearMarkers();
    }

    // The level chunks can be generated a frame or two after the HUD, so keep looking for a while.
    void Start()
    {
        searchUntil = Time.unscaledTime + searchSeconds;
        Rescan();
    }

    void LateUpdate()
    {
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        // Keep scanning through the generation window: chunks arrive over several frames, so the
        // first scan rarely sees all of them. Rescan() only adds what is still missing.
        if (Time.unscaledTime < searchUntil && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + 0.5f;
            Rescan();
        }
        UpdateMarkers();
    }

    // Finds every objective in the loaded level and keeps one marker per objective.
    public void Rescan()
    {
        if (markerPrefab == null) return;
        var found = new List<Transform>();
        foreach (var candidate in FindObjectsByType<Transform>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
            string name = ObjectiveName(candidate.name);
            if (name == null) continue;
            found.Add(candidate);
        }

        // Drop markers whose objective is gone (level regenerated or unloaded).
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            if (markers[i].target != null && found.Contains(markers[i].target)) continue;
            if (markers[i].rect != null) Destroy(markers[i].rect.gameObject);
            markers.RemoveAt(i);
        }
        // Add one marker per objective that does not have one yet.
        foreach (var target in found)
        {
            bool known = false;
            foreach (var marker in markers) known |= marker.target == target;
            if (!known) Spawn(target);
        }
    }

    // "Chunk_KOTH" and "Chunk_KOTH(Clone)" both give "KOTH"; the spawn chunk and anything not named
    // after the prefix give null.
    string ObjectiveName(string objectName)
    {
        string name = (objectName ?? string.Empty).Trim();
        const string clone = "(Clone)";
        if (name.EndsWith(clone, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - clone.Length).TrimEnd();
        if (!name.StartsWith(objectivePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var ignored in ignoredNames)
            if (!string.IsNullOrEmpty(ignored) && string.Equals(name, ignored.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;
        return name.Substring(objectivePrefix.Length);
    }

    void Spawn(Transform target)
    {
        string name = ObjectiveName(target.name);
        var rect = Instantiate(markerPrefab, transform, false);
        rect.name = "Objective Marker " + name;
        var marker = new Marker
        {
            target = target,
            rect = rect,
            icon = rect.GetComponent<Image>(),
            distance = rect.GetComponentInChildren<TMP_Text>(true),
        };
        // A missing icon keeps the art the prefab was authored with.
        if (marker.icon != null)
        {
            var sprite = IconFor(name);
            if (sprite != null) marker.icon.sprite = sprite;
        }
        markers.Add(marker);
    }

    // Marker art is named after the objective it marks: "KOTH" uses "objectiveMarkerKOTH".
    public Sprite IconFor(string objective)
    {
        if (string.IsNullOrEmpty(objective) || icons == null) return null;
        string wanted = iconPrefix + objective;
        foreach (var icon in icons)
            if (icon != null && string.Equals(icon.name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                return icon;
        return null;
    }

    void ClearMarkers()
    {
        foreach (var marker in markers)
            if (marker.rect != null) Destroy(marker.rect.gameObject);
        markers.Clear();
    }

    void UpdateMarkers()
    {
        if (gameplayCamera == null) return;
        var parentRect = (RectTransform)transform;
        Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        Vector3 eye = PlayerPosition();

        foreach (var marker in markers)
        {
            bool alive = marker.target != null && marker.target.gameObject.activeInHierarchy;
            if (marker.rect.gameObject.activeSelf != alive) marker.rect.gameObject.SetActive(alive);
            if (!alive) continue;

            Vector3 world = marker.target.position;
            Vector3 viewport = gameplayCamera.WorldToViewportPoint(world);
            bool onScreen = viewport.z > 0f
                && viewport.x >= edgeMargin && viewport.x <= 1f - edgeMargin
                && viewport.y >= edgeMargin && viewport.y <= 1f - edgeMargin;

            Vector2 screenPoint;
            if (onScreen)
            {
                screenPoint = gameplayCamera.WorldToScreenPoint(world);
                marker.rect.localScale = Vector3.one;
            }
            else
            {
                // Behind the camera mirrors viewport x/y, so flip the direction back before using it.
                Vector2 direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
                if (viewport.z <= 0f) direction = -direction;
                if (direction.sqrMagnitude < 1e-6f) direction = viewport.z <= 0f ? Vector2.down : Vector2.up;

                // Push the direction out until it meets the screen border that is nearest to it.
                float half = Mathf.Max(0.01f, 0.5f - edgeMargin);
                float reach = Mathf.Max(Mathf.Abs(direction.x) / half, Mathf.Abs(direction.y) / half);
                Vector2 edge = new Vector2(0.5f, 0.5f) + direction / Mathf.Max(reach, 1e-4f);
                screenPoint = new Vector2(edge.x * Screen.width, edge.y * Screen.height);
                marker.rect.localScale = Vector3.one * offScreenScale;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, screenPoint, canvasCamera, out Vector2 local))
                marker.rect.localPosition = new Vector3(local.x, local.y, 0f);
            else
                marker.rect.position = screenPoint;

            if (marker.distance != null)
                marker.distance.text = Mathf.Max(0, Mathf.RoundToInt(Vector3.Distance(eye, world))) + "m";
        }
    }

    // Distance is measured from the player, not the camera, which trails behind the mech.
    Vector3 PlayerPosition()
    {
        if (player == null)
        {
            var rig = CombatHudBinding.FindPlayer();
            if (rig != null) player = rig.transform;
        }
        if (player != null) return player.position;
        return gameplayCamera != null ? gameplayCamera.transform.position : transform.position;
    }
}
