using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// TEMPORARY prototype objective: walk to the item to collect it, then walk back to
// the return point to win. Uses distance checks instead of triggers, so it needs no
// colliders and doesn't interact with the part/pickup systems. Replace after prototype.
public sealed class TempLevelObjective : MonoBehaviour
{
    [Header("Scene objects")]
    [SerializeField] Transform item;
    [SerializeField] Transform returnPoint;
    [Tooltip("Optional. A disabled UI object (e.g. 'Level Complete' text) shown on win.")]
    [SerializeField] GameObject winScreen;

    [Header("Radii (horizontal distance)")]
    [SerializeField, Min(0.1f)] float pickupRadius = 3f;
    [SerializeField, Min(0.1f)] float returnRadius = 4f;

    [Header("Behaviour")]
    [Tooltip("Hide the return point until the item is collected.")]
    [SerializeField] bool hideReturnUntilCollected = true;
    [Tooltip("Pause the game (timeScale 0) when the level is won.")]
    [SerializeField] bool pauseOnWin = true;
    public TMP_Text toolTipText;
    public SceneManagerScript sceneManager;
    public KeyCode interactKey = KeyCode.F;
    Key inputKey;

    enum State { NotCollected, Collected, Won }
    State state = State.NotCollected;
    Transform player;
    public bool IsComplete => state == State.Won;
    public event Action Completed;

    void Start()
    {
        Enum.TryParse(interactKey.ToString(), out inputKey);
        var found = GameObject.FindWithTag("Player");
        if (found != null) player = found.transform;
        else Debug.LogWarning("TempLevelObjective: no object tagged 'Player' found.", this);

        if (winScreen != null) winScreen.SetActive(false);
        if (returnPoint != null && hideReturnUntilCollected) returnPoint.gameObject.SetActive(false);
        SetTooltip("Collect the yellow cube objective and return to the spawn point.");
    }

    void Update()
    {
        if (player == null) return;
        if (state == State.Won)
        {
            if (MissionOutcomeController.Find(gameObject.scene) != null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[inputKey].wasPressedThisFrame && sceneManager != null)
                sceneManager.LoadScene("MainMenuScene");
            return;
        }

        if (state == State.NotCollected && item != null && Near(item, pickupRadius))
        {
            state = State.Collected;
            item.gameObject.SetActive(false);
            if (returnPoint != null) returnPoint.gameObject.SetActive(true);
            //Debug.Log("Objective collected - return to the spawn point.");
            SetTooltip("Objective collected - return to the spawn point.");
        }
        else if (state == State.Collected && returnPoint != null && Near(returnPoint, returnRadius))
        {
            state = State.Won;
            var outcome = MissionOutcomeController.Find(gameObject.scene);
            if (outcome != null) outcome.ReportSuccess();
            else
            {
                if (winScreen != null) winScreen.SetActive(true);
                if (pauseOnWin) Time.timeScale = 0f;
            }
            //Debug.Log("Level complete!");
            SetTooltip("Level complete! Press " + interactKey + " to go back to the main menu");
            Completed?.Invoke();
        }
    }

    void SetTooltip(string message)
    {
        if (toolTipText != null) toolTipText.text = message;
    }

    // Horizontal only, so differences in pivot height between the mech and the markers don't matter.
    bool Near(Transform target, float radius)
    {
        Vector3 offset = target.position - player.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= radius * radius;
    }

    // Shows the radii in the Scene view when the Objective object is selected.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        if (item != null) Gizmos.DrawWireSphere(item.position, pickupRadius);
        Gizmos.color = Color.green;
        if (returnPoint != null) Gizmos.DrawWireSphere(returnPoint.position, returnRadius);
    }
}
