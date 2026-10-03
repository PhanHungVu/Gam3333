using UnityEngine;

// A reusable walking voice follows resolved motion, never input alone.
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class MechMovementAudio : MonoBehaviour
{
    [SerializeField] AudioClip movementSound;
    [SerializeField, Range(0f, 1f)] float volume = 0.3f;
    [SerializeField, Min(0.01f)] float referenceSpeed = 6f;
    MechMovementController movement;
    EnemyAgent enemy;
    AudioSource source;

    void Awake()
    {
        movement = GetComponent<MechMovementController>();
        enemy = GetComponent<EnemyAgent>();
    }

    void LateUpdate()
    {
        if (movement != null)
        {
            var data = movement.AnimationData;
            // GroundBoost is the existing sprint/run state, which still uses Walk.
            // Only dash and assault boost replace the walking action on this rig.
            bool walking = movement.isActiveAndEnabled && data.IsGrounded
                && data.State != MechMovementController.MovementState.QuickBoost
                && data.State != MechMovementController.MovementState.AssaultBoost;
            UpdatePlayback(data.Speed, walking);
        }
        else UpdatePlayback(enemy != null ? Vector3.ProjectOnPlane(enemy.Velocity, Vector3.up).magnitude : 0f,
            enemy != null && enemy.isActiveAndEnabled);
    }

    void UpdatePlayback(float speed, bool walking)
    {
        if (!isActiveAndEnabled || !walking || speed <= 0.1f || movementSound == null
            || volume <= 0f || Time.timeScale == 0f)
        {
            if (source != null) source.Stop();
            return;
        }
        if (source == null)
        {
            var audioObject = new GameObject("Leg Movement Audio");
            audioObject.transform.SetParent(transform, false);
            source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = 30f;
            source.spatialBlend = movement != null ? 0.35f : 1f;
        }
        if (source.clip != movementSound) { source.Stop(); source.clip = movementSound; }
        source.volume = volume;
        source.pitch = Mathf.Clamp(speed / referenceSpeed, 0.65f, 1.6f);
        if (!source.isPlaying) source.Play();
    }

    void OnDisable() { if (source != null) source.Stop(); }
}
