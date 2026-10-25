# Pre-mission flow — Q1–Q5 complete

The standalone Unity UI scene has data-driven offers and three separate views. Q2 adds physical paper placement and sequential posting; Q3 adds the pull-forward briefing and return transitions; Q4 adds starting-torso model preview and selection. Q5.1 varies posting entrances, Q5.2 refines paper handling, Q5.3 adds briefing reveals, torso/screen transitions and a removable second test torso, and Q5.4 adds intro skipping and lifecycle/reliability checks. Main and side quests are always equally selectable. A main quest's larger paper and final posting animation give it visual importance, with no progression lock or prerequisite behavior.

**Scope: Main Menu → Quest Board → Quest Details → Starting Torso → exposed empty `MissionStartHook.StartMission(quest, torso)`.** Our work ends at this UI handoff. The gameplay developer owns all mission integration and gameplay scene loading; those tasks are excluded from every remaining phase and its completion criteria.

## Open and test

1. Open `Assets/Scenes/MainMenuScene.unity` and enter Play mode.
2. Click **Quest Board**. The existing **Gym**, **Zoo**, and **Exit** entries remain available.
3. Side notices post one at a time, then the larger main notice posts last. **Skip Posting (Space)** or the **Space** key finishes the entry/posting intro immediately. Select any settled notice to pull it forward into a dedicated briefing. **Decline** or **Escape** returns it to its saved board position and rotation.
4. After the briefing reveals, **Accept Quest** opens the separate starting-torso view. Drag the existing torso model to rotate it. Browse **Golem Torso (100 health)** and **Heavy Torso (Test) (150 health)** with the left/right controls; the counter, name, description, health contribution and preview update together.
5. **Back to Details** or **Escape** retains the selected quest and torso.
6. **Confirm Torso** gives brief feedback, then calls the exposed gameplay function below once with the exact selected data. The UI stays on this view because gameplay is intentionally not connected.
7. Back/Escape from the board returns to the main menu.

You can also open `Assets/Scenes/PreMissionScene.unity` directly and enter Play mode. This scene has no gameplay player or physical board interaction. Mouse selection and keyboard UI navigation/Submit are supported; Escape steps back one view.

## Paper layout and transitions

- The first offered main quest occupies its own larger reserved region; ordinary notices fill separate cells beside it. On narrow layouts the featured region moves above the others. Paper colors, borders, and category labels distinguish main and side quests. No notice intentionally overlaps another.
- Each new board chooses one of three authored designs: **Aligned**, **Framed**, or **Rows**. Spacing and row alignment stay consistent, incomplete rows are centered, and tilts follow a restrained preset rather than independent random jitter. Rows places the main paper on the right. Returning from details preserves the chosen design, offers, and paper positions; resizing refits the same composition.
- Mouse hover or actual keyboard navigation subtly lifts a paper. Automatic initial/return focus only sets the keyboard navigation target, so it does not leave the main or returned notice permanently raised. Pointer hover is resynchronized after posting/return, including when the cursor is stationary. The original click area remains stable. Completed notices are selectable even while later ones are posting; selecting early settles the unfinished papers so all offers remain on return.
- One reusable travelling paper moves from the selected notice into the centered briefing paper. Its printed text grows proportionally with the paper during the pull and shrinks on return, using the selected notice's rendered typography as its reference instead of a fixed small font cap. The original notice is hidden while inspecting its quest. Decline restores its saved position and keyboard navigation target. The briefing displays mission category/type, title, description, estimated time, Gni reward, available parts, and enemies, with a scrollable body.
- Accept and torso Back use complementary short slide/fades. They preserve both selections and the briefing scroll position without replaying the board/paper sequence or information reveal. During a transition, two layers can briefly be visible; input is guarded until it finishes, preventing rapid clicks/Escape from skipping screens.
- Animation uses unscaled time. Posting waits for the initial canvas frames, and animation steps are capped so a loading hitch cannot consume the first paper's whole entrance. Disabling/re-enabling the UI mid-post or mid-transition settles the current session's target view safely, rather than leaving an orphan paper or restarting the sequence.

Tune placement on **PreMissionNoticeLayout** under `Quest Board View/Offers`: featured width, spacing, and the **Designs** array (name, main side, and authored angles). Tune posting on **PreMissionPaperPresentation** on the UI prefab root as described below; its existing pull duration, return duration, and view fade duration remain independently configurable. Visuals use ordinary Unity UI transforms, masks, colors, opacity, outlines, and shadows.

## Q5.1 — Quest posting animations

The old diagonal slide has been replaced by three entrances:

- **Short Drop:** starts up to 48 UI units above its assigned position, drops vertically, overshoots by 4 units with a small compression, then settles. Top-row drops are limited to the available space inside the offers area to keep them clear of the board heading/message.
- **Zoom Out/In:** stays centered and scales relative to its layout through **1.10 → 0.96 → 1.02 → 1.00**.
- **Painted Reveal:** stays at its final position and size while a soft clipping edge reveals the complete paper, accent and text from top to bottom. This first version is a smooth wipe, rather than character typing or separately stepped bands.

A seeded shuffle bag assigns all three styles before repeating and prevents identical adjacent entrances, including across bag boundaries. Assignment follows the actual side-first/main-last order, uses a separate `System.Random`, and is stored on each notice for this board. Main quests can receive any style; a configurable timing multiplier gives their final entrance slightly more weight. All settled notices remain selectable. Returning from details retains styles and poses without replaying the intro.

The stable notice root remains at its layout position/rotation/size. Only the existing **Paper Visual** child receives temporary posting movement or scale. The new **Posting Reveal** wrapper has a `RectMask2D`; compensating the content size while its clip grows prevents the paper or TMP text from stretching. Its mask is disabled after posting, so settled shadows and hover are not clipped. No shared materials, shaders, particles, fonts, palette or artwork are changed.

Inspector controls on **PreMissionPaperPresentation**:

| Setting | Shipped value | Purpose |
| --- | --- | --- |
| Drop / Zoom / Painted Duration | 0.30 / 0.30 / 0.48 s | Entrance time before final settling |
| Settle Duration | 0.10 s | Final compression/scale recovery; brief hold after painting |
| Posting Gap | 0.07 s | Pause between notices |
| Main Posting Time Multiplier | 1.12 | Scales entrance and settle time for main quests |
| Drop Height / Settle Distance | 48 / 4 UI units | Starting height and impact overshoot; height is limited by notice size and available headroom |
| Impact Compression | 0.025 | Restrained drop squash/stretch |
| Zoom Start / Undershoot / Rebound Scale | 1.10 / 0.96 / 1.02 | Relative scale multipliers |
| Painted Edge Softness | 6 | Vertical UI mask softness |

Posting reuses the existing unscaled coroutine and loading-hitch step cap. `FinishPosting()` stops the posting coroutine, restores all roots/visuals, removes partial clipping, enables controls and invokes completion at most once. Selecting a settled notice already calls it. Q5.4 also exposes it through **Skip Posting (Space)**. Disabling/re-enabling the full UI settles the current session view using the recovery path below.

## Q5.2 — Physical quest interaction

Posted notices ease into/out of a small hover over 0.12 seconds, for both pointer hover and deliberate keyboard navigation. Automatic UI focus remains a navigation target only. The visual lifts 3 UI units and scales to 1.025; the hit/layout root stays fixed. Hover stops updating while posting, handling a paper, or locking the board for a transition.

Selection captures the currently visible paper, including its hover position, rotation, scale and rendered text size. The controller populates the briefing without first hiding the board, so the handoff no longer resets the visible hover pose. One existing travelling proxy immediately compresses/releases over 0.08 seconds, then lifts and approaches the centered briefing over 0.42 seconds. Rotation straightens and printed type scales with the document. The captured hover stays relative to the layout root; the current animation sample can be reapplied after a Canvas refit without advancing time, preserving alignment as coordinates change. The briefing crossfades in at the end of the approach; Accept/Decline unlock only when the handoff finishes. No additional proxy is instantiated.

Decline/Escape follows the pickup path in reverse over 0.36 seconds, then applies a restrained 0.08-second settling compression at the saved board pose. The original paper stays hidden until the proxy has settled. Its root is never reparented; its parent, sibling order, board position/size/rotation, quest identity and Q5.1 style remain intact. Other papers remain stationary. The proxy is hidden, controls recover and stationary-pointer hover is resynchronized on return. Posting does not replay.

Settings on **QuestNoticeView** in `QuestNotice.prefab`: **Hover Duration** (0.12 s), **Hover Scale** (1.025), and **Hover Lift** (3). Settings on **PreMissionPaperPresentation** in `PreMissionUI.prefab`:

| Setting | Shipped value |
| --- | --- |
| Click Feedback Duration / Compression | 0.08 s / 0.03 |
| Pull Duration | 0.42 s, after click feedback |
| Pickup Lift / Lift Scale | 10 UI units / 1.035 |
| Return Duration / Return Settle Duration | 0.36 / 0.08 s |
| Return Settle Distance / Compression | 2 UI units / 0.012 |

The existing transition guard ignores competing selection, Accept and Back/Escape until an interaction finishes. The selected notice is marked as moving and cannot receive a second click; hover does not overwrite its animation. Cancellation stops the same presentation coroutines and releases the handled notice. Disabling/re-enabling the full UI settles the session's target view. Explicitly creating a fresh board cancels the old interaction before destroying notices, and destroying the UI ends its coroutines with its owned proxy. Q5.1 algorithms/timings, artwork and gameplay data are unchanged.

## Starting torso selection

The shipped catalogue has two choices: the unchanged **Golem Torso** (100 health contribution) and **Heavy Torso (Test)** (150). Both map to the exact same `Models/Golem1Models/Golem1Torso.glb`, with distinct `PartData` references/IDs. The counter reads **1 / 2** or **2 / 2**, and browsing wraps. With only one configured valid option, controls still disable automatically.

The new `Assets/PreMission/PartData_HeavyTorso_Test.asset` is development content: ID `starter_heavy_torso_test`, same part type/subtype and other data as the original, with its Health entry multiplied by 1.5. The original data/model are untouched. `StatType` has no weight entry; no weight, physics or movement system is introduced. The UI displays the actual selected data's health contribution, not an equipped player's total HP.

To remove the test option, remove its entry from both **Starter Torsos** and **Starter Torso Visuals** on the catalogue, then delete its asset if no longer needed. Explicitly rerunning the Q5.3 upgrade recreates/re-adds missing demo content.

To add a starting torso:

1. Create/reuse a `PartData` asset with a unique, nonblank part ID and **Part Type = Torso**. Its name, description and Health stat drive the text.
2. Add it to **QuestBoardCatalogue → Starter Torsos** in the desired browse order.
3. Optionally add a matching entry under **Starter Torso Visuals**: assign the same torso data and an existing model/prefab; set Rotation if it needs a different default orientation.

Preview mapping is presentation data only. The renderer copies existing mesh transforms, shared meshes/materials and, if present, a static baked skinned-mesh pose. It never instantiates source `Part`, player, socket, collider, Rigidbody, AI or animation behaviours. The mapping is not the equippable gameplay prefab; the gameplay developer still installs equipment through the empty hook below.

**StarterTorsoPreview** on `Starting Torso View/Torso Choice/Torso Model Preview/Model` exposes framing padding, resolution, background, drag sensitivity and preview layer (31 by default). A dedicated camera and masked lights render an owned, distant stage into a RawImage. Resources are created while this view is visible, redrawn for model/rotation/size changes, and released when it is hidden or destroyed. Switching data entries with the same model/orientation reuses the existing geometry/camera and retains drag rotation; render textures may resize when the Canvas aspect changes. Shared source assets are never destroyed or edited.

Mouse drag rotates the preview without changing the selection. Pointer buttons and standard keyboard UI navigation/Submit browse options; browse-button focus stays on that button for repeated submissions. Back preserves the quest and selected torso. Confirm submits both exact data assets once and locks browsing until Back. Missing model mapping shows a text fallback while retaining a valid torso choice; an empty torso catalogue shows **0 / 0** and disables confirmation/browsing.

## Q5.3 — Briefing, navigation and confirmation

After the Q5.2 paper reaches the foreground, five groups unveil over one second: **title/type → objective/description → duration/parts/enemies → reward → controls**. Each group fades in over 0.28 seconds, with 0.18 seconds between group starts, making their arrival visibly staggered. The existing scroll viewport/content/layout are reused for the body groups; long descriptions remain scrollable. No quest data is regenerated. Accept/Decline unlock after the sequence; returning from torso selection retains the revealed briefing and scroll position.

Board entry fades in over 0.60 seconds before the unchanged Q5.1 posting sequence. Details → Torso moves/fades forward; Torso → Details reverses that motion. The original view positions are restored exactly after each transition or cancellation.

Torso browsing moves/fades the existing preview, name, description and counter together. Next exits left/enters right; Previous reverses it. The selected data changes once at the hidden midpoint, and the existing preview is updated there. Competing arrows, confirmation, Back and model dragging are ignored during the short transition. No input queue accumulates. An interrupted outgoing browse retains the old choice; an interrupted incoming browse retains the already committed new choice. The UI restores a matching settled presentation.

Confirm reserves the exact quest/torso references immediately, gives a short button compression and selection pulse, then invokes the same empty hook once. Repeated input cannot resubmit. Disabling the controller/UI before feedback completes cancels the unsent callback and clears the reservation, allowing a deliberate retry when reopened. Once submitted, browsing stays locked until Back, as before. No gameplay scene is named or loaded.

All settings live on **PreMissionPaperPresentation**; existing Q5.1/Q5.2 settings are retained:

| Setting | Shipped value |
| --- | --- |
| Board Entry Duration | 0.60 s |
| View Fade Duration / View Travel | 0.26 s / 28 UI units |
| Briefing Fade Duration / Briefing Stagger | 0.28 / 0.18 s (1.00 s total for five groups) |
| Torso Browse Duration / Travel | 0.26 s / 36 UI units |
| Confirmation Duration / Pulse | 0.36 s / 0.025 |

Animation remains unscaled. The three existing groups are still the only settled screens; no additional UI controller, animation package, shader, particle system, art or font is introduced.

## Q5.4 — Reliability, usability and final polish

**Skip Posting (Space)** is available during entry/posting and accepts pointer clicks or standard UI Submit. The Space shortcut works independently of focus. Skip restores every notice's final transform, opacity, clipping and interaction without rerolling offers or styles; the button disappears and the normal board hint returns. The button receives initial navigation focus during the intro. A one-frame guard prevents the same skip input from also opening a newly enabled notice. Escape/Back still returns from the board to the menu.

The flow uses a consistent input policy: competing navigation is ignored during transitions; completed posting notices can still be selected early; submitted torso choices remain locked until Back; and explicit `CreateFreshBoard()` is the only in-scene regeneration path. The user's slower **0.60 s board entry** and **1.00 s briefing reveal** remain unchanged, as do all three posting styles and Q5.2/Q5.3 motion settings.

Disabling the controller, presenter or entire UI stops pending presentation work and settles the session's target view. Disabled components lock controls and reject public navigation calls; re-enabling restores controls and the same selections without replaying the intro. A component disabled before startup defers its first board until enabled. An outgoing torso browse retains the old choice if interrupted before commit; an incoming browse retains the committed choice. Unsent confirmation is cancelled and can be deliberately retried. Button listeners are removed on controller destruction.

`PreMissionFlowController.RecoverPresentation()` is the interruption/recovery entry point. The presenter's lower-level `Cancel()` is used by this recovery path and fresh-board setup. It is not a separate navigation action. The existing hook failure path reports failure, clears the launch reservation and permits a retry with the same selected assets. Programmatic invalid briefing fade/stagger values are clamped to keep reveal alpha finite; Inspector defaults remain the tuned values above.

The final suite checks all visible settled controls against Canvas bounds and exercises actual model rendering, all three screens, pickup, forward navigation and torso browsing during Canvas refits at **1280×720, 1600×900, 1920×1080 and 1024×768**. Offers, notice objects, posting styles, selected quest/torso and exact settled poses remain coherent through repeated navigation. The shipped model, data/health values, colors, fonts and gameplay hook are preserved.

## Gameplay developer: exposed empty mission-start function

**`MissionStartHook.StartMission(QuestDefinition selectedQuest, PartData startingTorso)` is public, virtual, and intentionally empty. You must connect mission gameplay here.**

Source: `Assets/Scripts/PreMission/MissionStartHook.cs`.

**Inspection requirement:** identify how the selected mission and starting torso data are stored, validated and passed to the exposed gameplay function, and document how gameplay code can receive them without assuming a particular scene. Reviewing this data contract is in scope; implementing the receiving gameplay system remains the gameplay developer's responsibility.

```csharp
public virtual void StartMission(QuestDefinition selectedQuest, PartData startingTorso)
{
}
```

The flow controller calls this function after a quest is selected/accepted, a valid starter torso is selected, and confirmation feedback completes. It passes the actual quest asset and selected torso `PartData` asset, not UI GameObjects. The quest contains its ID, title, description, category, mission type, estimated minutes, Gni reward, available parts/enemy prefab references, and optional `missionSettings` data.

Current data path: `QuestSelectionSession.SelectedQuest / SelectedTorso` → `TryRequestLaunch(out quest, out torso)` → confirmation feedback → `MissionStartHook.StartMission(quest, torso)`. `PreMissionFlowController.ConfirmTorso()` reserves the validated references and passes those exact assets to the hook after feedback completes. The session checks the current view, both choices and the submission guard. Gameplay code can receive the arguments by overriding the hook. The UI session is transient and provides no cross-scene storage; the receiving implementation owns retaining the data for whatever gameplay entry point it chooses.

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

Starter entries reuse `PartData`; only unique, nonblank IDs with `Part Type = Torso` are accepted. The original **Golem Torso** and removable **Heavy Torso (Test)** are the two shipped choices. Extra no-model and alternate-model verification options exist only as temporary test fixtures.

`QuestSelectionSession` owns offer references, selected quest, torso index, view, and the confirmation guard independently of UI objects. Opening/closing views or disabling/re-enabling the UI preserves the session. `PreMissionFlowController.CreateFreshBoard()` explicitly creates a new session; it is not called by ordinary navigation. Leaving and reopening the scene starts a fresh board. Sessions are not saved between game runs.

## Milestone boundaries

- **Q1 complete:** quest data, randomized stable offers, always-available main/side selection, responsive views, navigation, torso data placeholder, and empty public gameplay handoff.
- **Q2 complete:** larger dramatic main notice without obscuring side quests, reserved paper positions/rotation, hover/focus feedback, and side-first/main-last posting animations.
- **Q3 complete:** foreground paper movement, centered scrollable briefing, notice return transitions, guarded navigation, and interruption recovery.
- **Q4 complete:** existing torso model preview, drag rotation, browse/counter/details, selection persistence, keyboard/pointer controls, fallback/empty states, and exact data handoff. Actual gameplay connection remains the gameplay developer's responsibility through the hook above.
- **Q5.1 complete:** three seeded posting variations, side-first/main-last sequencing, restrained main timing emphasis, exact transform restoration and mask cleanup. This is phase 1 within Q5, not a new Q1 milestone.
- **Q5.2 complete — Physical Quest Interaction:** configurable eased hover, click compression/release, continuous pickup from the visible paper, reverse return and settling, stable hierarchy/poses, guarded input and interruption cleanup. Enlarged printed text and Q5.1 posting are preserved.
- **Q5.3 complete — Quest Details, Torso Selection and Screen Transitions:** ordered briefing groups with preserved scrolling, complementary screen transitions, directional torso browsing, separate same-model 150-health Heavy test data, confirmation feedback and exact scene-independent data handoff. Weight is unsupported by the existing data and remains unimplemented.
- **Q5.4 complete — Reliability and Final Polish:** explicit intro skipping, protected skip input, controller/presenter startup and interruption recovery, disabled-action guards, listener cleanup, failure/retry coverage, repeated navigation and motion/refit checks at four resolutions. Q5's requested feature implementation is complete; remaining manual device/renderer review is listed below.

**Gameplay boundary for Q5:** confirmation animates and invokes the same public empty `MissionStartHook.StartMission(quest, torso)` once. Keep its implementation empty. Verification covers passing the exact quest/torso data once to temporary recording hooks; connecting or testing gameplay scene loading belongs to the gameplay developer. Q5 does not add quest restrictions or main-first progression. The revised four-phase interaction plan replaces the earlier broad Q5/projection placeholder; particles, a custom shader or a projection effect are optional future presentation choices, not requirements for these phases.

No art, shaders, UI frameworks, combat systems, player initialization, or existing mission objectives were added or changed.

## Creation tool and verification

**Tools → Mech → Pre-Mission → Create Prototype** is an explicit setup tool, not an import/startup hook. It creates a missing prototype scene, assets, menu entry, build-scene registration, and the Q2/Q3 presentation. If the scene already exists, it preserves existing work and returns without rebuilding. **Tools → Mech → Pre-Mission → Upgrade Q2 and Q3 Presentation** explicitly upgrades the two shared UI prefabs in place, preserving their GUIDs, quest assets, gameplay-hook reference, and scene/menu wiring. The ordinary editable Unity assets are included; running either tool is not required to play them.

**Tools → Mech → Pre-Mission → Upgrade Q4 Torso Selection** updates the existing torso view and adds its missing default Golem model mapping, preserving configured torso choices and existing visual mappings. Fresh prototype creation includes this upgrade. The shipped assets are already upgraded. The existing glTFast importer must be available to resolve the model; no package changes or downloads are needed in the working project.

**Tools → Mech → Pre-Mission → Upgrade Q5.1 Quest Posting** adds the reveal wrapper to the existing notice prefab and saves the posting settings without resetting configured transitions. It preserves prefab GUIDs and scene/data/menu references. The Q2/Q3 upgrader recognizes the wrapped Paper Visual, so repeated upgrades do not duplicate it. Fresh prototype creation includes Q5.1; the shipped editable prefabs are already upgraded.

**Tools → Mech → Pre-Mission → Upgrade Q5.3 Transitions and Test Torso** groups existing briefing controls/content, wraps the existing torso choice for movement and adds missing Heavy test data/catalogue mapping. It preserves existing asset GUIDs, source torso data/model, mission hook and previous motion settings. The older Q4 upgrade recognizes the wrapped preview/counter and does not duplicate them. Fresh creation includes Q5.3, and shipped assets are already upgraded.

**Tools → Mech → Pre-Mission → Upgrade Q5.4 Skip Controls** adds/wires the skip button using a copy of the existing board button style, alongside the normal hint. It preserves prior layout and animation settings. Fresh creation includes this explicit upgrade; the shipped UI prefab already contains the controls.

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

Q4 adds `StarterTorsoPreview.cs`, `Editor/StarterTorsoSetup.cs`, and `Tests/Editor/StarterTorsoChecks.cs`; it updates the catalogue schema/default visual mapping, selection index readout, flow controller, shared UI prefab, creation tool and regression runner. The test fixture temporarily supplies extra torso data, a source with gameplay/physics components, and a missing-model choice to verify wrapping, keyboard Submit focus, safe renderer-only copying, drag rendering, resource cleanup, selection restoration, empty states, and one exact non-default quest/torso callback to a recording hook. At Q4 completion, the default `MissionStartHook.cs`, actual gameplay scenes, existing part stats/assets, model files, and Q2/Q3 paper code remained unchanged.

Q4 verified on 2026-10-25 in the disposable Unity 6000.5.8f1 project: compilation succeeded and **159 data/editor and Play Mode checks passed**, exit code 0. Tests check actual model pixels and reject missing-shader pink output, exercise pointer controls and keyboard Submit through the UI input module, and verify the exact non-default quest/torso pair reaches a recording hook once. Single/multiple/no-model/empty views and rotated previews were rendered; the shipped torso screen was visually inspected at 1600×900 and 1024×768. The known Editor Search startup exception still appears independently. Actual device drag feel, working-project URP lighting and other resolutions remain interactive review items. The live data retains one authored torso and 4–6 quest offers; temporary fixture choices were not copied back.

Q5.1 changes:

- Runtime: `PreMissionPaperPresentation.cs` distributes and plays the three entrances; `QuestNoticeView.cs` separates the stable root from temporary visual poses and handles the whole-notice mask.
- Assets: `QuestNotice.prefab` gains the disabled-by-default reveal wrapper; `PreMissionUI.prefab` stores the posting controls. Scene, menu, quest and torso data references retain their existing GUIDs.
- Editor: new `Editor/PreMissionPostingSetup.cs`; the prototype creation tool includes it, and the existing Q2/Q3 upgrader finds the wrapped visual safely.
- Verification: new `Tests/Editor/QuestPostingChecks.cs`; the existing regression runner invokes it and observes the first entrance across all styles. No shader, particle system, material or art asset is added.

The Q5.1 automated fixture checks seeded coverage and non-repetition across 30 seeds; observes the actual three entrance paths on both main and side notices; checks that animated roots stay at their layout poses; renders a half-painted paper to verify top/bottom visibility; and exercises repeated completion, partial-reveal cleanup, decline/return identity, interruption and final bounds at common PC resolutions. It also runs the earlier quest, hover, torso and empty-hook regression checks. The six-offer fixture and extra torso options stay in the disposable project only.

Q5.1 verified on 2026-10-25 in the disposable Unity 6000.5.8f1 project: compilation succeeded and **254 data/editor and automated Play Mode checks passed**. All three entrance captures and the half-painted notice were visually inspected; final boards were rendered at 1280×720, 1600×900 and 1920×1080 with non-overlapping bounds checks. Visual review also led to limiting top-row drops to available headroom; the final regression run includes that adjustment and checks it during motion. The known Editor Search startup exception remains separate from the completed checks. The verified notice prefab was copied back with its original GUID; the live catalogue still uses 4–6 offers and one authored torso.

At Q5.1 completion, no full game/player build or manual Play Mode session in the working project had been run. Interactive review items were posting rhythm and softness with real input, live-project URP appearance, repeated quest selection while later notices post, and feel at the listed resolutions. Q5.2–Q5.4 were pending at that point; the empty mission hook and existing gameplay scenes were unchanged.

Q5.2 changes `QuestNoticeView.cs`, `PreMissionPaperPresentation.cs`, `PreMissionFlowController.cs`, both existing UI prefabs, the regression runner and this README; it adds `Tests/Editor/QuestInteractionChecks.cs`. Inspector values are documented in the Q5.2 section above. No scene, quest/torso data, model, artwork, shader, particle system, gameplay hook or equipment code changes are part of Q5.2. The updated Heavy Torso request is recorded for Q5.3; no dummy torso asset has been added in this phase.

Q5.2 verified on 2026-10-25 in the disposable Unity 6000.5.8f1 project: compilation succeeded and **314 data/editor and automated Play Mode checks passed**. The suite includes Q5.1's three posting styles/sequencing, the existing torso and empty-hook regressions, and four repeated pointer/keyboard-Submit pickup/decline cycles across side and main quests. New checks observe click compression, release, lift and return settling; verify matching hover/text size at handoff; retain notice roots, parents and sibling order; confirm other papers remain stationary; and exercise rapid competing input, stationary-pointer hover recovery, disable/re-enable during approach and return settling, fresh-board cancellation before notice destruction, and whole-UI destruction during handling.

Visual review exposed Canvas-refit alignment and font-reference issues, both corrected before the final run. The current paper sample is reapplied in the current Canvas coordinates, and typography is refreshed against the refitted source paper when its reference size changes. Tests verify the restored click pose and that rendered travelling text fits vertically after refitting. Click, lift, return-settle and enlarged mid-pull captures were inspected. The suite also renders settled boards at 1280×720, 1600×900 and 1920×1080 with bounds/non-overlap checks. The known Editor Search startup exception appears independently of the completed checks.

At Q5.2 completion, no full game/player build or manual Play Mode run in the working project had been performed. Remaining interactive review included real mouse/keyboard animation feel, live-project URP appearance and actual window resizing during handling. Q5.3 and Q5.4 were pending at that point. The gameplay handoff stayed scene-independent and intentionally empty.

Q5.3 changes:

- `PreMissionFlowController.cs` fills the separated briefing fields, commits torso browsing at the hidden midpoint and owns the delayed/cancellable confirmation reservation.
- `PreMissionPaperPresentation.cs` reuses the existing unscaled animation helper for the five information groups, board entry, complementary screen movement, torso browsing and confirmation pulse. The board is hidden after the paper arrives, before the briefing information begins appearing.
- `StarterTorsoPreview.cs` reuses its stage for same-model data changes and respects parent input locks while dragging.
- `PreMissionUI.prefab` retains its GUID and existing objects while adding the reveal groups/labels and full-size torso-choice wrapper. `PrototypeBoardCatalogue.asset` retains its normal 4–6 offers and adds the separate Heavy test asset plus its shared model mapping.
- New `PreMission/PartData_HeavyTorso_Test.asset`, `Editor/PreMissionTransitionSetup.cs` and `Tests/Editor/PreMissionTransitionChecks.cs`. The creation tool includes Q5.3; Q4 setup recognizes the new wrapper. Earlier tests now wait for asynchronous browse/confirmation completion rather than expecting instant changes.

No existing mission scene, original torso data/stat, model, art asset, package, equipment/player-spawn system or `MissionStartHook.cs` implementation is changed. The prototype Heavy asset is deliberately removable; no real player's equipped stats or gameplay initialization are implemented here.

Q5.3 verified on 2026-10-25 in the disposable Unity 6000.5.8f1 project: compilation succeeded and **340 data/editor and automated Play Mode checks passed**. Coverage includes all prior posting/physical-paper regressions, the five reveal stages after the board/proxy are hidden, long-briefing scroll growth and scroll retention, complementary forward/back directions, both torso browse directions, midpoint data commit and rapid-input locking, retained same-model geometry/camera/rotation, browse interruption before/after commit, cancelled confirmation/retry, and exact Original/Heavy quest-plus-torso callbacks once after feedback. New transitions were exercised with `Time.timeScale = 0`. The recording hook received the actual original 100-health asset and separate 150-health Heavy asset; the active UI scene remained unchanged.

Briefing/stagger, long-content bottom, incoming torso, settled Heavy and confirmation captures were visually inspected at 1600×900. The regression suite also renders settled boards at 1280×720, 1600×900 and 1920×1080, and details/torso at 1024×768. Source scripts and migrated assets match the tested copy; the live catalogue retains 4–6 offers. The known Editor Search startup exception appeared independently of the completed checks.

At Q5.3 completion, no full game/player build or manual Play Mode session in the working project had been run. Real-device rhythm/keyboard comfort, live-project URP appearance and resizing during transitions remained interactive review items for Q5.4. Gameplay integration stayed outside this UI task.

Timing follow-up on 2026-10-25: slowed board entry from 0.16 to 0.60 seconds and the five-group briefing reveal from 0.26 to 1.00 seconds, using 0.28-second fades with 0.18-second staggered starts. Updated both component defaults and the shipped UI prefab. The existing isolated Unity suite compiled and passed all **340 checks** again with these longer timings.

Q5.4 changes `PreMissionFlowController.cs`, `PreMissionPaperPresentation.cs`, `PreMissionUI.prefab`, the creation tool, regression runner and this README. New files are `Editor/PreMissionReliabilitySetup.cs` and `Tests/Editor/PreMissionReliabilityChecks.cs`. The existing button style is reused for Skip Posting. No mission scene, quest/torso definition, model, art asset, package or gameplay-hook implementation is changed by this phase.

Q5.4 verified on 2026-10-25 in the disposable Unity 6000.5.8f1 project: compilation succeeded and **444 data/editor and automated Play Mode checks passed**. The suite includes all prior posting/hover/physical-paper/torso/empty-hook regressions plus pointer, Space and UI-Submit skipping; no same-frame selection after skip; partial painted-mask cleanup; disabled-component navigation guards; startup with controller or presenter disabled; repeated close/open and destruction; interruption during pickup, information reveal, both screen directions and both sides of torso commit; and a deliberately injected hook exception followed by a successful exact-data retry. A runtime log audit reports unexpected Unity errors as failures. The injected exception was explicitly expected, and no unexpected runtime errors were observed. The known Editor Search startup issue remains separate.

At **1280×720, 1600×900, 1920×1080 and 1024×768**, the same session was taken through board → details → torso → details → board repeatedly, with rapid competing browse inputs and actual model-pixel checks. Captures refit the Canvas during paper pickup, forward screen movement and torso browsing; travelling text was checked for vertical fit, settled controls for Canvas bounds, and board notices for bounds/non-overlap. Skip-entry, briefing, torso and mid-motion captures were visually inspected. The shipped prefab matches the tested copy apart from whitespace; the live catalogue retains its 4–6 offer range and original/Heavy choices.

**Q1–Q5 feature implementation is complete within the UI-only scope.** No full game/player build or manual Play Mode run in the working project was performed. Remaining manual review is real mouse/keyboard/gamepad comfort, animation rhythm, working-project URP appearance and OS window resizing; render-target/Canvas refit coverage is automated. Gameplay loading, equipment application, spawning and progression remain the gameplay developer's responsibility through the public empty hook.
