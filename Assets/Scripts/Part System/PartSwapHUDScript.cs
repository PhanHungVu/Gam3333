using TMPro;
using UnityEngine;

public class PartSwapHUDScript : MonoBehaviour
{
    GameObject partSwapHUDPrefab;
    private Part[] totalPartList = new Part[3];//0 = new nearby part, 1/2 = equipped parts (1 = left, 2 = right)
    EquipManager manager;
    public GameObject partSwapHudPodPrefab;
    public CameraControlThirdPerson cameraControl;
    GameObject newPartPos;
    GameObject equippedPartPos1;
    GameObject equippedPartPos2;
    GameObject equippedPartPos3;
    bool isOpen;
    bool cameraWasEnabled;
    CursorLockMode previousCursorLock;
    bool previousCursorVisible;
    public bool IsAvailable => partSwapHUDPrefab != null && partSwapHudPodPrefab != null;

    private void Awake()
    {
        manager = GetComponent<EquipManager>();
        GameObject cam = GameObject.FindWithTag("MainCamera");
        cameraControl = cam != null ? cam.GetComponent<CameraControlThirdPerson>() : null;
        partSwapHUDPrefab = GameObject.Find("Canvas/PartSwapHUD");
        if (partSwapHUDPrefab == null) return;
        partSwapHUDPrefab.SetActive(false);
        equippedPartPos1 = partSwapHUDPrefab.transform.Find("EquippedPartPos1").gameObject;
        equippedPartPos2 = partSwapHUDPrefab.transform.Find("EquippedPartPos2").gameObject;
        equippedPartPos3 = partSwapHUDPrefab.transform.Find("EquippedPartPos3").gameObject;
        newPartPos = partSwapHUDPrefab.transform.Find("NewPartPos").gameObject;
    }

    // Update is called once per frame
    void Update()
    {
        if (isOpen && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame) ClosePartList();
    }

    void OnDisable() { if (isOpen) ClosePartList(); }

    public void ShowPartList()
    {
        var nearbyPart = manager.GetSelectedPart();
        if (!IsAvailable || nearbyPart == null) return;
        ClearActivePods();
        if (!isOpen)
        {
            cameraWasEnabled = cameraControl != null && cameraControl.enabled;
            previousCursorLock = Cursor.lockState; previousCursorVisible = Cursor.visible;
            if (cameraControl != null) cameraControl.enabled = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            isOpen = true;
        }
        partSwapHUDPrefab.SetActive(true);
        for (int i = 0; i < totalPartList.Length; i++)
        {
            totalPartList[i] = null;
        }
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
        GameObject newPartSwapPod = Instantiate(partSwapHudPodPrefab, partPos); 
        TMP_Text[] texts = newPartSwapPod.GetComponentsInChildren<TMP_Text>();

        //set button for equipped part to swap with new part, do not activate new button
        var selectedPart = manager.GetSelectedPart();
        PartCategory category = selectedPart.partType;
        bool paired = category == PartCategory.ArmL || category == PartCategory.ArmR || category == PartCategory.ShoulderL || category == PartCategory.ShoulderR;
        if (category == PartCategory.ArmL || category == PartCategory.ArmR) category = i == 2 ? PartCategory.ArmR : PartCategory.ArmL;
        else if (category == PartCategory.ShoulderL || category == PartCategory.ShoulderR) category = i == 2 ? PartCategory.ShoulderR : PartCategory.ShoulderL;
        newPartSwapPod.GetComponent<PartSwapSelectPodScript>().SetStakeholders(manager, this, selectedPart, category);

        foreach (TMP_Text text in texts)
        {
            if (text.gameObject.name == "TitleText")
            {
                if (i == 0)
                {
                    text.text = "New Part";
                    UnityEngine.UI.Button btn = newPartSwapPod.GetComponentInChildren<UnityEngine.UI.Button>();
                    if (btn != null) btn.interactable = false;
                }
                else if (i == 1 && paired)
                {
                    text.text = "Left Part";
                }
                else if (i == 2 && paired)
                {
                    text.text = "Right Part";
                }
                else
                {
                    text.text = "Equipped Part";
                }
            }
            if (text.gameObject.name == "PartNameText")
            {
                text.text = totalPartList[i] != null && totalPartList[i].data != null ? totalPartList[i].data.partName : "Empty";
            }
            if (text.gameObject.name == "PartTypeText")
            {
                text.text = category.ToString();
            }
            if (text.gameObject.name == "StatNameText")
            {
                text.text = string.Empty;
                if (totalPartList[i] != null && totalPartList[i].data != null) foreach (var stat in totalPartList[i].data.stats)
                {
                    text.text += stat.type.ToString() + "\n";
                }
            }
            if (text.gameObject.name == "StatNumText")
            {
                text.text = string.Empty;
                if (totalPartList[i] != null && totalPartList[i].data != null) foreach (var stat in totalPartList[i].data.stats)
                {
                    text.text += stat.value.ToString() + "\n";
                }
            }
        }
    }
    public void ClearActivePods()
    {
        if (partSwapHUDPrefab == null) return;
        foreach (Transform child in partSwapHUDPrefab.transform)
        {
            if (child.gameObject.name == "NewPartPos" || child.gameObject.name == "EquippedPartPos1" || child.gameObject.name == "EquippedPartPos2" || child.gameObject.name == "EquippedPartPos3")
            {
                foreach (Transform podChild in child)
                {
                    podChild.gameObject.SetActive(false);
                    Destroy(podChild.gameObject);
                }
            }
        }
    }

    public void ClosePartList()
    {
        ClearActivePods();
        if (partSwapHUDPrefab != null) partSwapHUDPrefab.SetActive(false);
        if (!isOpen) return;
        if (cameraControl != null) cameraControl.enabled = cameraWasEnabled;
        Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible;
        isOpen = false;
    }
}
