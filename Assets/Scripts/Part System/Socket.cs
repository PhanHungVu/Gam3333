// Socket.cs
using UnityEngine;

public enum PartCategory { Head, ArmL, ArmR, Legs, ShoulderL, ShoulderR, Torso }

public class Socket : MonoBehaviour
{
    public PartCategory socketType;
    [System.Serializable]
    public struct WeaponAttachment
    {
        public MechWeaponAnimationSet animations;
        [Tooltip("Correction in authored part units, before the player's part scale is applied.")]
        public Vector3 position;
        public Vector3 eulerAngles;
        public bool overrideMuzzle;
        [Tooltip("Optional muzzle pose in its authored parent's space, used only while equipped here.")]
        public Vector3 muzzlePosition;
        public Vector3 muzzleEulerAngles;
    }
    [Tooltip("Optional compatibility poses for older weapon rigs. Unlisted/new weapons use the authored socket directly.")]
    [SerializeField] WeaponAttachment[] weaponAttachments = new WeaponAttachment[0];

    public Pose AttachmentFor(Part part)
    {
        var driver = part != null ? part.GetComponent<MechWeaponAnimator>() : null;
        if (driver != null && driver.Animations != null)
            foreach (var entry in weaponAttachments)
                if (entry.animations == driver.Animations) return new Pose(entry.position, Quaternion.Euler(entry.eulerAngles));
        return Pose.identity;
    }

    public bool TryMuzzlePose(Part part, out Pose pose)
    {
        var driver = part != null ? part.GetComponent<MechWeaponAnimator>() : null;
        if (driver != null && driver.Animations != null)
            foreach (var entry in weaponAttachments)
                if (entry.animations == driver.Animations && entry.overrideMuzzle)
                { pose = new Pose(entry.muzzlePosition, Quaternion.Euler(entry.muzzleEulerAngles)); return true; }
        pose = Pose.identity; return false;
    }
}
