using UnityEngine;

// Explicit clip references keep runtime playback independent of Blender action names.
[CreateAssetMenu(menuName = "Mech/Weapon Animation Set")]
public sealed class MechWeaponAnimationSet : ScriptableObject
{
    public AnimationClip neutral;
    public AnimationClip fire;
    public AnimationClip reload;
    [Header("Attack Audio")]
    public AudioClip attackSound;
    [Range(0f, 1f)] public float attackVolume = 0.75f;
    [Min(0f)] public float blendDuration = 0.04f;
    [Tooltip("For parts whose combat implementation is still pending. Plays poses only; no damage or projectile.")]
    public bool animationOnly;
}
