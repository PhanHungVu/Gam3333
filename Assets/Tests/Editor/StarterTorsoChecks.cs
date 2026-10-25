using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Temporary options exist only in the disposable Play Mode fixture, never in shipped data.
public sealed class StarterTorsoChecks
{
    readonly PreMissionFlowController flow;
    readonly Action<bool, string> check;
    readonly Action<string> capture;
    readonly Keyboard keyboard;
    readonly QuestBoardCatalogue catalogue;
    readonly List<PartData> originalTorsos;
    readonly List<StarterTorsoVisual> originalVisuals;
    readonly PartData alternate, withoutModel;
    readonly GameObject alternateModel;
    QuestDefinition quest;
    MissionStartHook originalHook;
    PreMissionRecordingHook recording;
    int phase;
    double next;
    float rotation;

    public StarterTorsoChecks(PreMissionFlowController flow, Keyboard keyboard, Action<bool, string> check, Action<string> capture)
    {
        this.flow = flow; this.check = check; this.capture = capture; this.keyboard = keyboard;
        catalogue = Field<QuestBoardCatalogue>("catalogue"); originalTorsos = catalogue.starterTorsos; originalVisuals = catalogue.starterTorsoVisuals;
        alternate = ScriptableObject.CreateInstance<PartData>(); alternate.partId = "q4-test-alternate";
        alternate.partName = "Test Alternate Torso"; alternate.partType = PartType.Torso; alternate.description = "Temporary browsing fixture.";
        alternate.stats = new List<StatEntry> { new StatEntry { type = StatType.Health, value = 140 } };
        withoutModel = ScriptableObject.CreateInstance<PartData>(); withoutModel.partId = "q4-test-no-model";
        withoutModel.partName = "Test Torso Without Preview"; withoutModel.partType = PartType.Torso;
        alternateModel = GameObject.CreatePrimitive(PrimitiveType.Cube); alternateModel.name = "Q4 Inactive Source With Gameplay Components";
        alternateModel.SetActive(false); alternateModel.transform.localScale = new Vector3(1.3f, 1f, 0.5f);
        alternateModel.AddComponent<Part>().data = alternate;
        alternateModel.GetComponent<MeshRenderer>().sharedMaterials = originalVisuals[0].model.GetComponentInChildren<MeshRenderer>(true).sharedMaterials;
        catalogue.starterTorsos = new List<PartData> { originalTorsos[0], alternate, withoutModel };
        catalogue.starterTorsoVisuals = new List<StarterTorsoVisual>(originalVisuals) { new StarterTorsoVisual { torso = alternate, model = alternateModel } };
        flow.CreateFreshBoard();
    }
    T Field<T>(string name) where T : Object => (T)new SerializedObject(flow).FindProperty(name).objectReferenceValue;
    StarterTorsoPreview Preview => Field<StarterTorsoPreview>("torsoPreview");
    void Click(string field)
    {
        var button = Field<Button>(field);
        var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position), button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        var target = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
        check(button.IsInteractable() && target == button.gameObject, "Torso pointer control receives its click: " + button.name);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
    }
    static GameObject[] Stages() => Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.name == "Starter Torso Preview Stage" && g.activeSelf).ToArray();
    public static bool HasRenderedModel(StarterTorsoPreview preview)
    {
        if (!preview.HasPreview) return false;
        preview.RenderNow();
        var old = RenderTexture.active; var sample = new Texture2D(preview.Texture.width, preview.Texture.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = preview.Texture;
            sample.ReadPixels(new Rect(0, 0, sample.width, sample.height), 0, 0); sample.Apply();
            var colors = sample.GetPixels32(); var background = colors[0]; int changed = 0, pink = 0;
            for (int i = 0; i < colors.Length; i += 16)
            {
                var c = colors[i];
                if (Mathf.Abs(c.r - background.r) + Mathf.Abs(c.g - background.g) + Mathf.Abs(c.b - background.b) > 35) changed++;
                if (c.r > 220 && c.b > 220 && c.g < 40) pink++;
            }
            return changed > 100 && pink < changed / 5;
        }
        finally { RenderTexture.active = old; Object.Destroy(sample); }
    }
    public bool Step()
    {
        if (EditorApplication.timeSinceStartup < next || flow.IsTransitioning) return false;
        switch (phase)
        {
            case 0:
                if (flow.IsPosting) return false;
                quest = flow.Session.Offers.First(); flow.SelectQuest(quest); break;
            case 1:
                flow.AcceptQuest(); break;
            case 2:
                check(flow.Session.SelectedTorsoIndex == 0 && Field<TMP_Text>("torsoCounter").text == "1 / 3", "Three valid torso options populate the selection counter");
                check(Preview.Torso == originalTorsos[0] && HasRenderedModel(Preview), "Existing Golem torso renders real model pixels with supported materials");
                capture("q4-existing-torso"); Click("previousTorsoButton"); break;
            case 3:
                check(flow.Session.SelectedTorso == withoutModel && Field<TMP_Text>("torsoCounter").text == "3 / 3", "Previous wraps from the first torso to the last");
                check(!Preview.HasPreview && Field<Button>("confirmTorsoButton").interactable, "Missing model shows a fallback without invalidating a valid torso selection");
                capture("q4-missing-model"); Click("nextTorsoButton"); break;
            case 4:
                check(flow.Session.SelectedTorso == originalTorsos[0] && Preview.HasPreview, "Next wraps back to the first torso and rebuilds its model");
                Click("nextTorsoButton"); break;
            case 5:
                check(flow.Session.SelectedTorso == alternate && Field<TMP_Text>("torsoName").text == alternate.partName
                    && Field<TMP_Text>("torsoDescription").text.Contains("140") && Preview.Torso == alternate, "Browse updates the selected data, name, description, health and preview together");
                check(Stages().Length == 1 && Stages()[0].GetComponentsInChildren<MonoBehaviour>(true)
                    .All(b => b.GetType().FullName == "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData"
                        || b.GetType().FullName == "UnityEngine.Rendering.Universal.UniversalAdditionalLightData")
                    && Stages()[0].GetComponentsInChildren<Collider>(true).Length == 0 && Stages()[0].GetComponentsInChildren<Rigidbody>(true).Length == 0,
                    "Preview retains only geometry and rendering metadata, without gameplay behaviours, physics or duplicate stages; found "
                        + Stages().Length + " stages, behaviours: " + string.Join(", ", Stages().SelectMany(s => s.GetComponentsInChildren<MonoBehaviour>(true)).Select(b => b.GetType().FullName)));
                rotation = Preview.Rotation;
                ExecuteEvents.Execute(Preview.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, delta = new Vector2(80, 0) }, ExecuteEvents.dragHandler);
                break;
            case 6:
                check(Preview.Rotation != rotation && HasRenderedModel(Preview), "Dragging rotates the selected model and renders the new view");
                capture("q4-alternate-rotated");
                EventSystem.current.SetSelectedGameObject(Field<Button>("nextTorsoButton").gameObject);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); InputSystem.Update();
                EventSystem.current.currentInputModule.Process();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
                break;
            case 7:
                check(flow.Session.SelectedTorso == withoutModel && EventSystem.current.currentSelectedGameObject == Field<Button>("nextTorsoButton").gameObject,
                    "Keyboard Submit browses and retains arrow focus instead of jumping to Confirm");
                Click("nextTorsoButton"); break;
            case 8:
                Click("nextTorsoButton"); break;
            case 9:
                Click("torsoBackButton"); break;
            case 10:
                check(flow.Session.View == PreMissionView.Details && flow.Session.SelectedQuest == quest && flow.Session.SelectedTorso == alternate,
                    "Torso Back retains the selected quest and non-default torso");
                check(!Preview.HasPreview && Preview.Texture == null && Stages().Length == 0, "Leaving torso selection releases its render texture and preview stage");
                flow.AcceptQuest(); break;
            case 11:
                check(Preview.HasPreview && Preview.Torso == alternate && flow.Session.SelectedTorso == alternate, "Reopening torso selection restores the chosen model without resetting its selection");
                originalHook = Field<MissionStartHook>("missionStartHook"); recording = flow.gameObject.AddComponent<PreMissionRecordingHook>();
                var fields = new SerializedObject(flow); fields.FindProperty("missionStartHook").objectReferenceValue = recording; fields.ApplyModifiedPropertiesWithoutUndo();
                Click("confirmTorsoButton"); flow.ConfirmTorso(); flow.NextTorso();
                check(recording.Calls == 0 && flow.Session.LaunchRequested && flow.IsTransitioning, "Confirmation reserves the choice before its single delayed handoff");
                break;
            case 12:
                check(recording.Calls == 1 && recording.Quest == quest && recording.Torso == alternate && flow.Session.SelectedTorso == alternate,
                    "The public handoff receives the exact non-default torso and quest once; selection locks after submission");
                capture("q4-submitted");
                catalogue.starterTorsos = new List<PartData>();
                flow.CreateFreshBoard(); Field<PreMissionPaperPresentation>("presentation").FinishPosting(); flow.SelectQuest(flow.Session.Offers.First()); break;
            case 13:
                flow.AcceptQuest(); break;
            case 14:
                check(flow.Session.SelectedTorso == null && Field<TMP_Text>("torsoCounter").text == "0 / 0" && !Field<Button>("confirmTorsoButton").interactable
                    && !Field<Button>("nextTorsoButton").interactable && !Field<Button>("previousTorsoButton").interactable && !Preview.HasPreview,
                    "An empty torso catalogue shows a clear empty state and cannot confirm or browse");
                capture("q4-empty");
                flow.gameObject.SetActive(false);
                catalogue.starterTorsos = originalTorsos; catalogue.starterTorsoVisuals = originalVisuals;
                var restore = new SerializedObject(flow); restore.FindProperty("missionStartHook").objectReferenceValue = originalHook; restore.ApplyModifiedPropertiesWithoutUndo();
                Object.Destroy(recording); Object.Destroy(alternateModel); Object.Destroy(alternate); Object.Destroy(withoutModel);
                return true;
        }
        phase++; next = EditorApplication.timeSinceStartup + 0.25; return false;
    }
}

public sealed class PreMissionRecordingHook : MissionStartHook
{
    public int Calls;
    public QuestDefinition Quest;
    public PartData Torso;
    public override void StartMission(QuestDefinition selectedQuest, PartData startingTorso) { Calls++; Quest = selectedQuest; Torso = startingTorso; }
}
