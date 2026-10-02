using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class PartSwapSelectPodScript : MonoBehaviour
{
    EquipManager manager;
    PartSwapHUDScript partswapper;
    Part selectedPart;
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Equip()
    {
        manager.TryEquip(selectedPart);
        partswapper.ClosePartList();
    }

    public void SetStakeholders(EquipManager m, PartSwapHUDScript p, Part part)
    {
        manager = m;
        partswapper = p;
        selectedPart = part;
    }
}
