# Pre-mission flow — Q1–Q4

The standalone Unity UI scene has data-driven offers and three separate views. Q2 adds physical paper placement and sequential posting; Q3 adds the pull-forward briefing and return transitions; Q4 adds starting-torso model preview and selection. Main and side quests are always equally selectable. A main quest's larger paper and final posting animation give it visual importance, with no progression lock or prerequisite behavior.

## Open and test

1. Open `Assets/Scenes/MainMenuScene.unity` and enter Play mode.
2. Click **Quest Board**. The existing **Gym**, **Zoo**, and **Exit** entries remain available.
3. Side notices post one at a time, then the larger main notice posts last. Select any settled notice to pull it forward into a dedicated briefing. **Decline** or **Escape** returns it to its saved board position and rotation.
4. **Accept Quest** opens the separate starting-torso view. Drag the existing Golem Torso model to rotate it. Browse using the left/right controls when more than one valid torso is configured; the counter, name, description, health contribution and preview update together.
5. **Back to Details** or **Escape** retains the selected quest and torso.
6. **Confirm Torso** calls the exposed gameplay function below. The UI stays on this view because gameplay is intentionally not connected.
7. Back/Escape from the board returns to the main menu.

You can also open `Assets/Scenes/PreMissionScene.unity` directly and enter Play mode. This scene has no gameplay player or physical board interaction. Mouse selection and keyboard UI navigation/Submit are supported; Escape steps back one view.

## Paper layout and transitions

- The first offered main quest occupies its own larger reserved region; ordinary notices fill separate cells beside it. On narrow layouts the featured region moves above the others. Paper colors, borders, and category labels distinguish main and side quests. No notice intentionally overlaps another.
- Each new board chooses one of three authored designs: **Aligned**, **Framed**, or **Rows**. Spacing and row alignment stay consistent, incomplete rows are centered, and tilts follow a restrained preset rather than independent random jitter. Rows places the main paper on the right. Returning from details preserves the chosen design, offers, and paper positions; resizing refits the same composition.
- Mouse hover or actual keyboard navigation subtly lifts a paper. Automatic initial/return focus only sets the keyboard navigation target, so it does not leave the main or returned notice permanently raised. Pointer hover is resynchronized after posting/return, including when the cursor is stationary. The original click area remains stable. Completed notices are selectable even while later ones are posting; selecting early settles the unfinished papers so all offers remain on return.
- One reusable travelling paper moves from the selected notice into the centered briefing paper. Its printed text grows proportionally with the paper during the pull and shrinks on return, using the selected notice's rendered typography as its reference instead of a fixed small font cap. The original notice is hidden while inspecting its quest. Decline restores its saved position and keyboard navigation target. The briefing displays mission category/type, title, description, estimated time, Gni reward, available parts, and enemies, with a scrollable body.
- Accept and torso Back use short fades. They preserve both selections and do not replay a board/paper sequence. During a transition, two layers can briefly be visible; input is guarded until it finishes, preventing rapid clicks/Escape from skipping screens.
- Animation uses unscaled time. Posting waits for the initial canvas frames, and animation steps are capped so a loading hitch cannot consume the first paper's whole entrance. Disabling/re-enabling the UI mid-post or mid-transition settles the current session's target view safely, rather than leaving an orphan paper or restarting the sequence.

Tune placement on **PreMissionNoticeLayout** under `Quest Board View/Offers`: featured width, spacing, and the **Designs** array (name, main side, and authored angles). Tune **PreMissionPaperPresentation** on the UI prefab root: posting duration/gap/offset/settle distance, pull duration, return duration, and view fade duration. All visuals use ordinary Unity UI transforms, colors, opacity, outlines, and shadows.

## Starting torso selection

The shipped catalogue uses the existing **Golem Torso** `PartData` and `Models/Golem1Models/Golem1Torso.glb`. Its health contribution is read from the existing data (100); Q4 adds no new gameplay parts or invented stat variants. With one option, the counter reads **1 / 1** and the browse controls are disabled. Additional configured options enable wraparound browsing automatically.

To add a starting torso:

1. Create/reuse a `PartData` asset with a unique, nonblank part ID and **Part Type = Torso**. Its name, description and Health stat drive the text.
2. Add it to **QuestBoardCatalogue → Starter Torsos** in the desired browse order.
3. Optionally add a matching entry under **Starter Torso Visuals**: assign the same torso data and an existing model/prefab; set Rotation if it needs a different default orientation.

Preview mapping is presentation data only. The renderer copies existing mesh transforms, shared meshes/materials and, if present, a static baked skinned-mesh pose. It never instantiates source `Part`, player, socket, collider, Rigidbody, AI or animation behaviours. The mapping is not the equippable gameplay prefab; the gameplay developer still installs equipment through the empty hook below.

**StarterTorsoPreview** on `Starting Torso View/Torso Model Preview/Model` exposes framing padding, resolution, background, drag sensitivity and preview layer (31 by default). A dedicated camera and masked lights render an owned, distant stage into a RawImage. Resources are created while this view is visible, redrawn for model/rotation/size changes, and released when it is hidden or destroyed. Shared source assets are never destroyed or edited.

Mouse drag rotates the preview without changing the selection. Pointer buttons and standard keyboard UI navigation/Submit browse options; browse-button focus stays on that button for repeated submissions. Back preserves the quest and selected torso. Confirm submits both exact data assets once and locks browsing until Back. Missing model mapping shows a text fallback while retaining a valid torso choice; an empty torso catalogue shows **0 / 0** and disables confirmation/browsing.

## Gameplay developer: exposed empty mission-start function

**`MissionStartHook.StartMission(QuestDefinition selectedQuest, PartData startingTorso)` is public, virtual, and intentionally empty. You must connect mission gameplay here.**

Source: `Assets/Scripts/PreMission/MissionStartHook.cs`.

```csharp
public virtual void StartMission(QuestDefinition selectedQuest, PartData startingTorso)
{
}
```

The flow controller calls this function only after a quest is selected/accepted and a valid starter torso is selected. It passes the actual quest asset and the existing torso `PartData` asset, not UI GameObjects. The quest contains its ID, title, description, category, mission type, estimated minutes, Gni reward, available parts/enemy prefab references, and optional `missionSettings` data.

To connect gameplay, either implement this method or create a `MissionStartHook` subclass and override it. If using a subclass, replace the default hook component and assign your component to **PreMissionFlowController → Mission Start Hook** on `Assets/Prefabs/UI/PreMission/PreMissionUI.prefab`.

The gameplay implementation owns validation against actual mission capabilities, persistence of the chosen data before loading, loading/starting the appropriate mission, installing the starting torso, applying enemy/part settings, and eventual completion/rewards. There is no automatic gameplay scene load, hidden singleton, default mission filename, equipment change, currency payment, or quest-completion behavior in this UI flow. Q4 keeps this public empty function and its signature unchanged.

Repeated confirmation on the current torso view is ignored. Back to Details clears that submission guard so the player can deliberately return and confirm again. If the hook throws, the UI reports failure, logs the exception, and permits retry. An asynchronous gameplay implementation should handle its own loading/failure UI.

**Equipment note:** `EquipManager` already handles torso attachment and transfers equipped limbs when replacing a torso. Its current starting-loadout pass does not replace an already occupied torso slot. Install the selected starting torso deliberately, accounting for socket compatibility, before initial health/stat setup. This milestone does not change that gameplay initialization.

## Data and scene assets

- `Assets/PreMission/PrototypeBoardCatalogue.asset`: configurable minimum/maximum offer count (defaults 4–6), quest pool, optional main-offer inclusion, starter-torso list, and optional torso visual mappings.
- `Assets/PreMission/Quests/PrototypeQuest_*.asset`: six clearly identified prototype briefings. They demonstrate data/UI only; none implements a mission type or reward payout.
- `Assets/Prefabs/UI/PreMission/PreMissionUI.prefab`: editable uGUI layout, controller, empty gameplay hook, and return-to-menu callback.
- `Assets/Prefabs/UI/PreMission/QuestNotice.prefab`: reusable paper, category accent, border, shadow, button, and text. A single nested instance is reused as the travelling paper.
- `Assets/Scenes/PreMissionScene.unity`: UI prefab instance, UI camera, and Input System EventSystem.

Create more definitions through **Assets → Create → Mech → Pre-Mission → Quest** and add them to the catalogue. Use unique, nonblank quest IDs. Unavailable definitions, nulls, and duplicate IDs are excluded from generation. Offer count is clamped to the number of eligible definitions. Main inclusion affects generation only and never disables other offers.

Starter entries reuse `PartData`; only unique, nonblank IDs with `Part Type = Torso` are accepted. The existing **Golem Torso** remains the sole shipped starter and now has its existing model preview. Additional torso choices used by verification exist only as temporary test fixtures.

`QuestSelectionSession` owns offer references, selected quest, torso index, view, and the confirmation guard independently of UI objects. Opening/closing views or disabling/re-enabling the UI preserves the session. `PreMissionFlowController.CreateFreshBoard()` explicitly creates a new session; it is not called by ordinary navigation. Leaving and reopening the scene starts a fresh board. Sessions are not saved between game runs.

## Milestone boundaries

- **Q1 complete:** quest data, randomized stable offers, always-available main/side selection, responsive views, navigation, torso data placeholder, and empty public gameplay handoff.
- **Q2 complete:** larger dramatic main notice without obscuring side quests, reserved paper positions/rotation, hover/focus feedback, and side-first/main-last posting animations.
- **Q3 complete:** foreground paper movement, centered scrollable briefing, notice return transitions, guarded navigation, and interruption recovery.
- **Q4 complete:** existing torso model preview, drag rotation, browse/counter/details, selection persistence, keyboard/pointer controls, fallback/empty states, and exact data handoff. Actual gameplay connection remains the gameplay developer's responsibility through the hook above.
- **Q5:** transition polish and optional subtle information projection.

No art, shaders, UI frameworks, combat systems, player initialization, or existing mission objectives were added or changed.

## Creation tool and verification

**Tools → Mech → Pre-Mission → Create Prototype** is an explicit setup tool, not an import/startup hook. It creates a missing prototype scene, assets, menu entry, build-scene registration, and the Q2/Q3 presentation. If the scene already exists, it preserves existing work and returns without rebuilding. **Tools → Mech → Pre-Mission → Upgrade Q2 and Q3 Presentation** explicitly upgrades the two shared UI prefabs in place, preserving their GUIDs, quest assets, gameplay-hook reference, and scene/menu wiring. The ordinary editable Unity assets are included; running either tool is not required to play them.

**Tools → Mech → Pre-Mission → Upgrade Q4 Torso Selection** updates the existing torso view and adds its missing default Golem model mapping, preserving configured torso choices and existing visual mappings. Fresh prototype creation includes this upgrade. The shipped assets are already upgraded. The existing glTFast importer must be available to resolve the model; no package changes or downloads are needed in the working project.

`Assets/Tests/Editor/PreMissionChecks.cs` checks generation eligibility/uniqueness, main/side access, count edge cases, torso validation/wrapping, navigation, stable notice objects, Escape, confirmation guards, the intentionally empty handoff, and menu round trips. Q2/Q3 coverage adds sequential posting/main-last ordering, relative main-paper size, rotated non-overlapping bounds, pointer raycasts/clicks, hover, saved position/rotation restoration, early selection during posting, paused animation, duplicate input, and disable/re-enable interruption recovery.

Q1 baseline verified on 2026-10-25 in an isolated Unity 6000.5.8f1 project: compilation succeeded and all **83 data/editor and Play Mode checks passed**, with exit code 0. The editor also emitted the existing `UnityEditor.Search.SearchDatabase.EnumerateAll` startup exception; the pre-mission checks completed successfully. The working gameplay scenes were not run or modified by this verification.

Q2/Q3 verified on 2026-10-25 in the same disposable Unity version: **111 data/editor and Play Mode checks passed**, exit code 0, including the maximum six-offer board, pointer clicks on main/side notices and Accept/Decline, interruption cases, and the unchanged empty gameplay handoff. Board and briefing renders were inspected at 1600×900 and 1024×768, plus main-quest details and a paper-pull frame. The known Editor Search startup exception also appeared in this run. The verified prefabs were copied back with their existing GUIDs; the live catalogue retains its normal 4–6 offer range.

Follow-up presentation fixes verified on 2026-10-25: **137 checks passed**, exit code 0, in the isolated Unity project. New checks reproduce a loading hitch during the first slide, verify enlarged printed type during zoom, limit seeded layouts to the three authored designs, and use an isolated Input System mouse to verify visible hover/exit on the initial main notice and after returning selected side/main notices. They also verify hover with a stationary cursor after returning. The enlarged-text transition, all three presets, the first slide, and hover renders were inspected. The known Editor Search startup exception still appears independently. This follow-up does not implement Q4/Q5 or change the empty mission-start hook.

Q2/Q3 modify `PreMissionFlowController`, `PreMissionNoticeLayout`, `QuestNoticeView`, both shared UI prefabs, the setup tool, this README, and the regression runner. New code is `PreMissionPaperPresentation.cs` and `Editor/PreMissionPresentationSetup.cs`. Quest definitions/catalogue, gameplay scenes, menu/build wiring, torso data, and `MissionStartHook.cs` remain as established in Q1.

Run only in a disposable Unity 6000.5.8f1 copy with the project's Input System/uGUI dependencies and the required scripts/assets:

```text
Unity.exe -batchmode -projectPath <disposable-project> -executeMethod PreMissionChecks.RunBatch -logFile <test-log> -screen-width 1600 -screen-height 900
```

Do not add `-quit`; the asynchronous runner enters Play mode and exits itself. With graphics enabled it writes board/details/torso/menu captures to `<disposable-project>/PreMissionCaptures`. Final interactive checks include mouse hit areas, keyboard focus, scrolling long briefings, and readability at different resolutions.

Use `-executeMethod PreMissionChecks.RunSixOffersBatch` for the maximum-density fixture. It changes the **disposable copy's** catalogue to exactly six offers before running the same checks; never run it against the working project. Pointer raycasts/click handlers, hover, and Escape have automated coverage; animation feel, actual device keyboard navigation/Submit, long-briefing scroll comfort, and other aspect ratios still benefit from an interactive editor pass.

Q4 adds `StarterTorsoPreview.cs`, `Editor/StarterTorsoSetup.cs`, and `Tests/Editor/StarterTorsoChecks.cs`; it updates the catalogue schema/default visual mapping, selection index readout, flow controller, shared UI prefab, creation tool and regression runner. The test fixture temporarily supplies extra torso data, a source with gameplay/physics components, and a missing-model choice to verify wrapping, keyboard Submit focus, safe renderer-only copying, drag rendering, resource cleanup, selection restoration, empty states, and one exact non-default quest/torso callback to a recording hook. The default `MissionStartHook.cs`, actual gameplay scenes, existing part stats/assets, model files, and Q2/Q3 paper code remain unchanged. Q5 remains pending.

Q4 verified on 2026-10-25 in the disposable Unity 6000.5.8f1 project: compilation succeeded and **159 data/editor and Play Mode checks passed**, exit code 0. Tests check actual model pixels and reject missing-shader pink output, exercise pointer controls and keyboard Submit through the UI input module, and verify the exact non-default quest/torso pair reaches a recording hook once. Single/multiple/no-model/empty views and rotated previews were rendered; the shipped torso screen was visually inspected at 1600×900 and 1024×768. The known Editor Search startup exception still appears independently. Actual device drag feel, working-project URP lighting and other resolutions remain interactive review items. The live data retains one authored torso and 4–6 quest offers; temporary fixture choices were not copied back.
