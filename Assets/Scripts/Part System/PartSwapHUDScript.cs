using TMPro;
using UnityEngine;

public class PartSwapHUDScript : MonoBehaviour
{
    [SerializeField] private GameObject partSwapHUDPrefab;
    private Part[] totalPartList = new Part[3];//0 = new nearby part, 1/2 = equipped parts (1 = left, 2 = right)
    [SerializeField] private EquipManager manager;
    public GameObject partSwapHudPodPrefab;
    CameraControlThirdPerson cameraControl;
    GameObject newPartPos;
    GameObject equippedPartPos1;
    GameObject equippedPartPos2;
    GameObject equippedPartPos3;

    private void Awake()
    {
        manager = GetComponent<EquipManager>();
        cameraControl = GetComponent<CameraControlThirdPerson>();
        partSwapHUDPrefab = GameObject.Find("Canvas/PartSwapHUD");
        partSwapHUDPrefab.SetActive(false);
        equippedPartPos1 = partSwapHUDPrefab.transform.Find("EquippedPartPos1").gameObject;
        equippedPartPos2 = partSwapHUDPrefab.transform.Find("EquippedPartPos2").gameObject;
        equippedPartPos3 = partSwapHUDPrefab.transform.Find("EquippedPartPos3").gameObject;
        newPartPos = partSwapHUDPrefab.transform.Find("NewPartPos").gameObject;
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void ShowPartList()
    {
        ClearActivePods();
        cameraControl.SetCursorLocked(false);
        partSwapHUDPrefab.SetActive(true);
        for (int i = 0; i < totalPartList.Length; i++)
        {
            totalPartList[i] = null;
        }
        var nearbyPart = manager.GetNearbyPart();
        if (nearbyPart == null) return;
        totalPartList[0] = nearbyPart;
        PartCategory category = totalPartList[0].partType;
        if (category == PartCategory.ArmL || category == PartCategory.ArmR)
        {
            totalPartList[1]= manager.GetEquipped(PartCategory.ArmL);
            totalPartList[2] = manager.GetEquipped(PartCategory.ArmR);
        }
        else if (category == PartCategory.ShoulderL || category == PartCategory.ShoulderR)
        {
            totalPartList[1] = manager.GetEquipped(PartCategory.ShoulderL);
            totalPartList[2] = manager.GetEquipped(PartCategory.ShoulderR);
        }
        else
        {
            totalPartList[1] = manager.GetEquipped(category);
        }

        InitPod(newPartPos, 0);
        if (category == PartCategory.ArmL || category == PartCategory.ArmR || category == PartCategory.ShoulderL || category == PartCategory.ShoulderR)
        {
            InitPod(equippedPartPos1, 1);
            InitPod(equippedPartPos2, 2);
        }
        else
        {
            InitPod(equippedPartPos3, 1);
        }
    }

    public void InitPod(GameObject gObject, int i)
    {
        Transform partPos = gObject.transform;
        GameObject newPartSwapHud = Instantiate(partSwapHudPodPrefab, partPos);
        TMP_Text[] texts = newPartSwapHud.GetComponentsInChildren<TMP_Text>();

        foreach (TMP_Text text in texts)
        {
            if (text.gameObject.name == "TitleText")
            {
                if (i == 0)
                {
                    text.text = "New Part";
                }
                if (i == 1)
                {
                    text.text = "Left Part";
                }
                if (i == 2)
                {
                    text.text = "Right Part";
                }
                if (i == 3)
                {
                    text.text = "Equipped Part";
                }
            }
            if (text.gameObject.name == "PartNameText")
            {
                text.text = totalPartList[i].data.partName;
            }
            if (text.gameObject.name == "PartTypeText")
            {
                text.text = totalPartList[i].partType.ToString();
            }
            if (text.gameObject.name == "StatNameText")
            {
                foreach (var stat in totalPartList[i].data.stats)
                {
                    text.text += stat.type.ToString() + "\n";
                }
            }
            if (text.gameObject.name == "StatNumText")
            {
                foreach (var stat in totalPartList[i].data.stats)
                {
                    text.text += stat.value.ToString() + "\n";
                }
            }
        }
    }
    public void ClearActivePods()
    {
        foreach (Transform child in partSwapHUDPrefab.transform)
        {
            if (child.gameObject.name == "NewPartPos" || child.gameObject.name == "EquippedPartPos1" || child.gameObject.name == "EquippedPartPos2" || child.gameObject.name == "EquippedPartPos3")
            {
                foreach (Transform podChild in child)
                {
                    Destroy(podChild.gameObject);
                }
            }
        }
    }
}
