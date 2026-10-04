using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Exercises the real merged swap UI and GPU-rendered part models.
public static class PartModelPreviewSceneChecks
{
    const string Pending = "PartModelPreviewSceneChecks.Pending";
    static int phase, checks;
    static float next = -1f;
    static EquipManager equipment;
    static PartSwapHUDScript selector;
    static Part mace, crossbow, heavy, axe;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Update;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene("Assets/Scenes/Merge Test.unity");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++; Debug.Log("PART PREVIEW PASS: " + message);
    }
    static PartModelPreview Preview(string slot) => GameObject.Find("Canvas/PartSwapHUD/" + slot).GetComponentInChildren<PartModelPreview>();
    static void Click(string slot) => GameObject.Find("Canvas/PartSwapHUD/" + slot).GetComponentInChildren<Button>().onClick.Invoke();
    static void Show(Part part) { equipment.SetSelectedPart(part); selector.ShowPartList(); }
    static void Capture(string name) => typeof(CombatFeedbackSceneChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { "part-swap-" + name });
    static void Model(string slot, Part expected)
    {
        var preview = Preview(slot);
        var image = preview.GetComponent<RawImage>();
        Check(preview.Source == expected && preview.MeshCount > 0 && image.enabled && !image.raycastTarget,
            slot + " shows its actual part and keeps the equip button clickable");
        var texture = image.texture as RenderTexture;
        Check(texture != null && texture.IsCreated(), "Card owns a model render texture");
        var canvasRect = image.canvas.GetComponent<RectTransform>();
        var corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
        Check(corners.All(c => canvasRect.rect.Contains(canvasRect.InverseTransformPoint(c))),
            "Model preview fits inside the canvas: " + slot);
        var previous = RenderTexture.active;
        var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); pixels.Apply();
            var colors = pixels.GetPixels32(); var background = colors[0];
            int changed = colors.Count(c => Math.Abs(c.r - background.r) + Math.Abs(c.g - background.g) + Math.Abs(c.b - background.b) > 30);
            Check(changed > 150, "Visible model pixels replace the white square: " + expected.name);
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
    }
    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0f) next = Time.time + 1f;
        if (Time.time < next) return;
        try
        {
            switch (phase)
            {
                case 0:
                    Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Validation uses real GPU rendering");
                    foreach (var spawner in Object.FindObjectsByType<EnemySpawnerManager>()) spawner.enabled = false;
                    foreach (var enemy in Object.FindObjectsByType<EnemyAgent>()) enemy.gameObject.SetActive(false);
                    var player = Object.FindAnyObjectByType<MechPlayerRig>().gameObject;
                    player.GetComponent<MechMovementController>().ReadPlayerInput = false;
                    player.GetComponent<MechCombatController>().ReadPlayerInput = false;
                    player.GetComponentInChildren<PickupDetector>().enabled = false;
                    equipment = player.GetComponent<EquipManager>(); selector = player.GetComponent<PartSwapHUDScript>();
                    mace = GameObject.Find("Mace Animation Pickup").GetComponent<Part>();
                    crossbow = GameObject.Find("Arm Crossbow Animation Pickup").GetComponent<Part>();
                    heavy = GameObject.Find("Heavy Legs Pickup").GetComponent<Part>();
                    axe = equipment.GetEquipped(PartCategory.ArmR);
                    int ammo = axe.RemainingAmmo; var pose = axe.transform.localToWorldMatrix;
                    Show(mace); Model("NewPartPos", mace);
                    Model("EquippedPartPos1", equipment.GetEquipped(PartCategory.ArmL)); Model("EquippedPartPos2", axe);
                    Check(axe.RemainingAmmo == ammo && axe.transform.localToWorldMatrix == pose, "Preview creation preserves live equipment transforms and ammunition");
                    Check(!mace.GetComponent<MechWeaponAnimator>().CanAnimate, "Ground pickup animation stays disabled while previewing");
                    Capture("weapons"); break;
                case 1:
                    var old = Preview("NewPartPos"); var oldTexture = old.GetComponent<RawImage>().texture as RenderTexture;
                    Click("EquippedPartPos1");
                    Check(equipment.GetEquipped(PartCategory.ArmL) == mace, "Preview button still equips into the left socket");
                    Check(old.Source == null && !oldTexture.IsCreated(), "Closing releases model rendering resources immediately");
                    Show(crossbow); Model("NewPartPos", crossbow); Model("EquippedPartPos1", mace); Model("EquippedPartPos2", axe);
                    Capture("crossbow"); break;
                case 2:
                    Click("EquippedPartPos2");
                    Check(equipment.GetEquipped(PartCategory.ArmR) == crossbow, "Preview cards preserve right-arm swapping");
                    Show(heavy); Model("NewPartPos", heavy); Model("EquippedPartPos3", equipment.GetEquipped(PartCategory.Legs));
                    Capture("legs"); break;
                case 3:
                    Click("EquippedPartPos3");
                    Check(equipment.GetEquipped(PartCategory.Legs) == heavy, "Preview cards preserve animated leg swapping");
                    equipment.Unequip(PartCategory.ArmL); Show(mace);
                    Check(Preview("EquippedPartPos1").Source == null && !Preview("EquippedPartPos1").GetComponent<RawImage>().enabled,
                        "Empty slot has no misleading white square");
                    Capture("empty"); selector.ClosePartList(); Show(crossbow); selector.ClosePartList();
                    Check(!Object.FindObjectsByType<Camera>().Any(c => c.name == "Preview Camera"), "Closed menu leaves no active preview cameras");
                    SessionState.SetBool(Pending, false); Debug.Log("PART PREVIEW CHECKS COMPLETE: " + checks); EditorApplication.Exit(0); return;
            }
            phase++; next = Time.time + 0.3f;
        }
        catch (Exception e) { SessionState.SetBool(Pending, false); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
