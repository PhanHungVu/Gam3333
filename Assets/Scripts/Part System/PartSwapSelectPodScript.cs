using UnityEngine;

public class PartSwapSelectPodScript : MonoBehaviour
{
    EquipManager manager;
    PartSwapHUDScript partswapper;
    Part selectedPart;
    PartCategory targetCategory;
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Equip()
    {
        if (manager != null && manager.TryEquip(selectedPart, targetCategory)) partswapper.ClosePartList();
    }

    public void SetStakeholders(EquipManager m, PartSwapHUDScript p, Part part, PartCategory category)
    {
        manager = m;
        partswapper = p;
        selectedPart = part;
        targetCategory = category;
    }
}
