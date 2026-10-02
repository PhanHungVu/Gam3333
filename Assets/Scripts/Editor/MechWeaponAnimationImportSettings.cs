using System;
using UnityEngine;

// Editor-only import selections. Add families and assign clips through the Inspector.
[CreateAssetMenu(menuName = "Mech/Editor/Weapon Animation Imports")]
public sealed class MechWeaponAnimationImportSettings : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public PartData part;
        public string rigPath;
        public string referenceMesh;
        public string bodyBone;
        public string placementBone;
        public AnimationClip neutral, fire, reload;
        public MechWeaponAnimationSet target;
    }
    public GameObject source;
    public Entry[] weapons;
}
