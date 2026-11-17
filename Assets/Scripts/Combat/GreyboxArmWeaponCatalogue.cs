using UnityEngine;

[CreateAssetMenu(menuName = "Mech/Greybox Arm Weapon Catalogue")]
public sealed class GreyboxArmWeaponCatalogue : ScriptableObject
{
    public Part[] weapons = new Part[0];
}
