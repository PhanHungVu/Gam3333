using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class PartSwapSelectPodScript : MonoBehaviour
{
    EquipManager manager;
    PartSwapHUDScript partswapper;
    Part closest;
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Equip()
    {
        manager.TryEquip(closest);
        partswapper.ClosePartList();
    }

    public void SetStakeholders(EquipManager m, PartSwapHUDScript p, Part part)
    {
        manager = m;
        partswapper = p;
        closest = part;
    }
}
