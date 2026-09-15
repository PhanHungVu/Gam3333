using System;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Run only in an isolated project with -batchmode -executeMethod MechFoundationChecks.RunBatch.
// Uses real Unity physics and UI projection; no mocking of the gameplay controllers.
public static class MechFoundationChecks
{
    const string PendingKey = "MechFoundationChecks.Pending";
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;

    [InitializeOnLoadMethod]
    static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
                RunChecks();
        };
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run these checks in an isolated batch-mode project.");
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    static object Call(object obj, string method, params object[] args) =>
        obj.GetType().GetMethod(method, PrivateInstance).Invoke(obj, args);
    static object Get(object obj, string field) => obj.GetType().GetField(field, PrivateInstance).GetValue(obj);
    static void Set(object obj, string field, object value) =>
        obj.GetType().GetField(field, PrivateInstance).SetValue(obj, value);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Debug.Log("MECH CHECK PASS: " + message);
        checks++;
    }
    static void Step(MechMovementController motor, Camera view, Vector2 move = default,
        bool sprint = false, bool jump = false, bool boost = false, float dt = 0.02f, bool assault = false)
    {
        Type inputType = typeof(MechMovementController).GetNestedType("MovementInput", BindingFlags.NonPublic);
        object input = Activator.CreateInstance(inputType);
        inputType.GetField("move").SetValue(input, move);
        inputType.GetField("sprint").SetValue(input, sprint);
        inputType.GetField("jump").SetValue(input, jump);
        inputType.GetField("quickBoost").SetValue(input, boost);
        inputType.GetField("assaultBoost").SetValue(input, assault);
        Call(motor, "Tick", input, view.transform, dt);
    }
    static MechTargetable Enemy(Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.position = position;
        return go.AddComponent<MechTargetable>();
    }
    static void RunChecks()
    {
        SessionState.SetBool(PendingKey, false);
        try
        {
            var view = new GameObject("Test Camera").AddComponent<Camera>();
            view.tag = "MainCamera";
            view.pixelRect = new Rect(100, 50, 1000, 700);
            CheckTargeting(view);
            CheckSoftLockIntent(view);
            CheckFreeAimModes(view);
            CheckLockVerticalLook(view);
            CheckMovement(view);
            CheckEnemyIndependentMovement(view);
            CheckGroundFeatures(view);
            CheckHardLock(view);
            Debug.Log("MECH CHECKS COMPLETE: " + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void CheckTargeting(Camera view)
    {
        var player = new GameObject("Targeting Player");
        var targeting = player.AddComponent<MechTargetLockController>();
        int changes = 0;
        targeting.TargetChanged += (previous, next) => changes++;
        targeting.ResumeTargeting();
        var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var cursor = new GameObject("Cursor", typeof(RectTransform)).GetComponent<RectTransform>();
        cursor.SetParent(canvas.transform, false);
        var tracker = cursor.gameObject.AddComponent<LockOnCursor>();
        tracker.Bind(targeting, view);
        var a = Enemy(new Vector3(-3, 0, 20));
        var b = Enemy(new Vector3(7, 0, 20));
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == a && changes == 1, "Acquire nearest world-distance target and publish one change");
        Call(targeting, "LateUpdate");
        Check(changes == 1, "Keeping a target does not repeat change events");
        view.transform.rotation = Quaternion.LookRotation(new Vector3(4, 0, 20));
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == a, "Free look preserves sticky target until center crosses an enemy");
        Call(tracker, "LateUpdate");
        Check(Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, cursor.position),
            view.WorldToScreenPoint(a.AimPosition)) < 0.1f, "Cursor overlays target after camera rotation");
        view.transform.rotation = Quaternion.LookRotation(b.AimPosition);
        targeting.ReportAimInput(Vector2.right);
        Quaternion rotation = view.transform.rotation;
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == b && changes == 2, "Center aim switches targets exactly once");
        Check(view.transform.rotation == rotation, "Selecting target never rotates gameplay camera");
        b.transform.position += Vector3.up;
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Call(tracker, "LateUpdate");
        Check(Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, cursor.position),
            view.WorldToScreenPoint(b.AimPosition)) < 0.1f, "Cursor tracks a moving target");
        b.enabled = false;
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == a, "Disabled targets are released");
        a.transform.position = new Vector3(0, 0, -20);
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Call(tracker, "LateUpdate");
        Check(targeting.CurrentTarget == null, "Target behind the camera is released");
        Check(Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, cursor.position),
            view.pixelRect.center) < 0.1f, "Unlocked cursor uses viewport center");
        Set(tracker, "showAtScreenCenterWhenUnlocked", false);
        Call(tracker, "LateUpdate");
        Check(cursor.gameObject.activeSelf && cursor.GetComponent<CanvasGroup>().alpha == 0f,
            "Hiding cursor keeps its updates alive");
        a.transform.position = view.transform.forward * 20;
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Call(tracker, "LateUpdate");
        Check(cursor.GetComponent<CanvasGroup>().alpha == 1f, "Hidden cursor reappears on reacquisition");
        var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(canvas.transform, false);
        panel.localPosition = new Vector3(80, -40, 0);
        panel.localScale = Vector3.one * 1.4f;
        cursor.SetParent(panel, false);
        Canvas.ForceUpdateCanvases();
        Call(tracker, "LateUpdate");
        Check(Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, cursor.position),
            view.WorldToScreenPoint(a.AimPosition)) < 0.1f, "Nested scaled UI preserves cursor alignment");
        a.gameObject.SetActive(false);
        int before = changes;
        Object.DestroyImmediate(a.gameObject);
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == null && changes == before + 1, "Destroyed target publishes a clear event");
        var crowd = new GameObject("Crowd");
        for (int i = 0; i < 80; i++)
        {
            var enemy = Enemy(view.transform.forward * (15 + i * 0.1f) + view.transform.right * 3);
            enemy.transform.SetParent(crowd.transform);
        }
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        var buffer = (Collider[])Get(targeting, "candidates");
        Check(buffer.Length >= 128 && targeting.CurrentTarget != null, "Horde query grows without dropping candidates");
        Call(targeting, "LateUpdate");
        Check(ReferenceEquals(buffer, Get(targeting, "candidates")), "Subsequent queries reuse collider storage");
        targeting.enabled = false;
        Check(targeting.CurrentTarget == null, "Disabling targeting clears selection");
        player.SetActive(false);
        crowd.SetActive(false);
        b.gameObject.SetActive(false);
        canvas.gameObject.SetActive(false);
        view.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    static void CheckSoftLockIntent(Camera view)
    {
        var player = new GameObject("Soft Lock Intent Player");
        player.transform.position = new Vector3(1000, 0, 0);
        view.transform.SetPositionAndRotation(player.transform.position, Quaternion.identity);
        var targeting = player.AddComponent<MechTargetLockController>();
        Set(targeting, "gameplayCamera", view);
        targeting.ResumeTargeting();
        var near = Enemy(player.transform.position + new Vector3(-5, 0, 15));
        var centered = Enemy(player.transform.position + new Vector3(0, 0, 30));
        var side = Enemy(player.transform.position + new Vector3(4, 0, 20));
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == near, "Initial soft lock uses distance from mech, not distance from screen center");
        centered.transform.position = player.transform.position + new Vector3(0, 0, 10);
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == near, "A nearer enemy crossing stationary hidden aim cannot steal soft lock");
        view.transform.rotation = Quaternion.Euler(0, 7, 0);
        targeting.ReportAimInput(Vector2.right);
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == near, "Aiming at empty space preserves a valid soft lock");
        centered.transform.position = player.transform.position + new Vector3(0, 0, 30);
        view.transform.rotation = Quaternion.identity;
        Physics.SyncTransforms();
        targeting.ReportAimInput(Vector2.left);
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == centered, "Deliberate hidden aim selects a farther enemy over the closer selection");
        near.transform.position = player.transform.position + new Vector3(0, 0, 8);
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == centered, "Aim input is consumed: next-frame enemy movement cannot switch targets");
        targeting.ToggleHardLock();
        Check(targeting.IsHardLocked && targeting.CurrentTarget == centered,
            "Hard-lock toggle preserves the visible selection even with a nearer enemy on center aim");
        targeting.ToggleHardLock();
        centered.enabled = false;
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == near, "Losing a selected enemy automatically picks nearest without mouse input");
        centered.enabled = true;
        near.transform.position = player.transform.position + new Vector3(-30, 0, 15);
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == side, "Enemy exiting screen picks nearest visible replacement rather than center-most enemy");
        Object.DestroyImmediate(side.gameObject);
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == centered, "Destroyed soft-lock target automatically advances to the next nearest enemy");
        centered.gameObject.SetActive(false);
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == null, "Soft lock clears when no visible enemy remains");
        player.SetActive(false);
        near.gameObject.SetActive(false);
        view.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    static void CheckFreeAimModes(Camera view)
    {
        var player = new GameObject("Free Aim Mode Player");
        player.transform.position = new Vector3(2000, 0, 0);
        view.transform.SetPositionAndRotation(player.transform.position, Quaternion.identity);
        var targeting = player.AddComponent<MechTargetLockController>();
        Set(targeting, "gameplayCamera", view);
        var enemy = Enemy(player.transform.position + Vector3.forward * 20);
        Physics.SyncTransforms();
        targeting.ReportAimInput(Vector2.right);
        Call(targeting, "LateUpdate");
        Check(targeting.IsFreeAim && targeting.CurrentTarget == null,
            "Startup free aim never acquires an enemy, even when the player aims at it");
        var canvas = new GameObject("Free Aim Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var cursor = new GameObject("Free Aim Cursor", typeof(RectTransform)).GetComponent<RectTransform>();
        cursor.SetParent(canvas.transform, false);
        var tracker = cursor.gameObject.AddComponent<LockOnCursor>();
        tracker.Bind(targeting, view);
        Call(tracker, "LateUpdate");
        Check(Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, cursor.position), view.pixelRect.center) < 0.1f,
            "Free aim displays the existing cursor at screen center");

        targeting.ProcessLockButton(true, true, false, 0.02f);
        Check(!targeting.IsFreeAim && !targeting.IsHardLocked && targeting.CurrentTarget == enemy,
            "First middle-mouse press immediately enters soft lock");
        targeting.ProcessLockButton(false, true, false, 2f);
        targeting.ProcessLockButton(false, false, true, 0.02f);
        Check(!targeting.IsFreeAim && !targeting.IsHardLocked,
            "Holding and releasing the restore press cannot immediately exit or toggle the restored mode");
        targeting.ProcessLockButton(true, true, false, 0.1f);
        Check(!targeting.IsHardLocked, "Beginning a hold does not prematurely toggle soft lock");
        targeting.ProcessLockButton(false, false, true, 0.02f);
        Check(targeting.IsHardLocked, "A short middle-mouse click switches soft lock to hard lock on release");

        var cameraRoot = new GameObject("Free Aim Mode Camera");
        cameraRoot.SetActive(false);
        cameraRoot.AddComponent<Camera>();
        var cameraControl = cameraRoot.AddComponent<CameraControlThirdPerson>();
        Set(cameraControl, "target", player.transform);
        cameraRoot.SetActive(true);
        var rig = (CinemachineCamera)Get(cameraControl, "cinemachineCamera");
        Call(cameraControl, "UpdateCameraOrbit", Vector2.zero, 0.02f);
        Call(cameraControl, "ApplySettings");
        Check(rig.LookAt == enemy.AimTransform, "Hard mode still directs the actual camera at its enemy");
        targeting.ProcessLockButton(true, true, false, 1.19f);
        Check(targeting.IsHardLocked, "A 1.19-second hold leaves hard lock active");
        targeting.ProcessLockButton(false, true, false, 0.02f);
        Call(cameraControl, "UpdateCameraOrbit", Vector2.zero, 0.02f);
        Call(cameraControl, "ApplySettings");
        Check(targeting.IsFreeAim && targeting.CurrentTarget == null && !targeting.IsHardLocked && rig.LookAt == rig.Follow,
            "Crossing 1.2 seconds clears target and camera follow into free aim");
        targeting.ProcessLockButton(false, true, false, 3f);
        targeting.ProcessLockButton(false, false, true, 0.02f);
        targeting.ReportAimInput(Vector2.left);
        Call(targeting, "LateUpdate");
        Check(targeting.IsFreeAim && targeting.CurrentTarget == null,
            "Long-hold release and subsequent aiming cannot reactivate targeting");
        targeting.ProcessLockButton(true, true, false, 0.02f);
        targeting.ProcessLockButton(false, false, true, 0.02f);
        Check(targeting.IsHardLocked && targeting.CurrentTarget == enemy, "A new press restores the remembered hard-lock mode");

        targeting.ProcessLockButton(true, true, false, 0.02f);
        targeting.ProcessLockButton(false, false, true, 0.02f);
        Check(!targeting.IsFreeAim && !targeting.IsHardLocked, "Short click from hard lock returns to soft lock");
        targeting.ProcessLockButton(true, true, false, 1.21f);
        targeting.ProcessLockButton(false, false, true, 0.02f);
        Check(targeting.IsFreeAim, "Soft lock also exits after the hold threshold");
        targeting.ProcessLockButton(true, true, false, 0.02f);
        targeting.ProcessLockButton(false, false, true, 0.02f);
        Check(!targeting.IsFreeAim && !targeting.IsHardLocked && targeting.CurrentTarget == enemy,
            "Restoring soft mode does not accidentally restore an older hard mode");
        targeting.ProcessLockButton(true, true, false, 0.6f);
        targeting.CancelLockButtonInput();
        targeting.ProcessLockButton(false, true, false, 1f);
        targeting.ProcessLockButton(false, false, true, 0.02f);
        Check(!targeting.IsFreeAim && !targeting.IsHardLocked,
            "Interrupted holds cannot complete or turn into taps after pause or cursor release");

        targeting.ToggleHardLock();
        targeting.EnterFreeAim();
        enemy.gameObject.SetActive(false);
        targeting.ProcessLockButton(true, true, false, 0.02f);
        targeting.ProcessLockButton(false, false, true, 0.02f);
        Call(targeting, "LateUpdate");
        Check(!targeting.IsFreeAim && targeting.CurrentTarget == null && !targeting.IsHardLocked,
            "Restoring hard mode without an enemy waits safely for a target");
        enemy.gameObject.SetActive(true);
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Check(targeting.IsHardLocked && targeting.CurrentTarget == enemy,
            "Explicitly restored hard mode locks when an eligible enemy becomes available");
        targeting.EnterFreeAim();
        enemy.gameObject.SetActive(false);
        targeting.ResumeTargeting();
        targeting.EnterFreeAim();
        enemy.gameObject.SetActive(true);
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Check(targeting.IsFreeAim && targeting.CurrentTarget == null,
            "Enemies appearing during free aim cannot acquire automatically");
        targeting.ResumeTargeting();
        Check(targeting.IsHardLocked, "Hard-mode memory survives entering free aim while waiting for an enemy");
        targeting.enabled = false;
        targeting.enabled = true;
        Call(targeting, "LateUpdate");
        Check(targeting.IsFreeAim && targeting.CurrentTarget == null, "Re-enabling the controller does not silently resume auto targeting");
        player.SetActive(false);
        enemy.gameObject.SetActive(false);
        canvas.gameObject.SetActive(false);
        cameraRoot.SetActive(false);
        view.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    static void CheckLockVerticalLook(Camera view)
    {
        var player = new GameObject("Vertical Lock Player");
        player.transform.position = new Vector3(3000, 0, 0);
        view.transform.SetPositionAndRotation(player.transform.position, Quaternion.identity);
        var targeting = player.AddComponent<MechTargetLockController>();
        Set(targeting, "gameplayCamera", view);
        var cameraRoot = new GameObject("Vertical Lock Camera");
        cameraRoot.SetActive(false);
        cameraRoot.AddComponent<Camera>();
        var cameraControl = cameraRoot.AddComponent<CameraControlThirdPerson>();
        Set(cameraControl, "target", player.transform);
        cameraRoot.SetActive(true);
        var rig = (CinemachineCamera)Get(cameraControl, "cinemachineCamera");
        Call(cameraControl, "UpdateCameraOrbit", Vector2.up * 10000, 0.02f);
        Check(Mathf.Abs((float)Get(cameraControl, "cameraPitch") + 18f) < 0.001f,
            "Free aim retains its original upward mouse-look limit");
        targeting.ResumeTargeting();
        Call(cameraControl, "UpdateCameraOrbit", Vector2.up * 10000, 0.02f);
        Check(Mathf.Abs((float)Get(cameraControl, "cameraPitch") - 9f) < 0.001f,
            "Entering soft lock from a low view restores the elevated minimum pitch");
        Call(cameraControl, "ApplySettings");
        rig.InternalUpdateCameraState(Vector3.up, -1f);
        Check(rig.State.GetFinalPosition().y > player.transform.position.y + 1.5f,
            "Lowest allowed soft-lock view places the camera above mech body height");
        Call(cameraControl, "UpdateCameraOrbit", Vector2.down * 10000, 0.02f);
        Check(Mathf.Abs((float)Get(cameraControl, "cameraPitch") - 15f) < 0.001f,
            "Large downward mouse movement stays within the soft-lock pitch band");
        Set(cameraControl, "cameraPitch", 12f);
        float yaw = (float)Get(cameraControl, "cameraYaw");
        Call(cameraControl, "UpdateCameraOrbit", new Vector2(50, 10), 0.02f);
        Check(Mathf.Abs((float)Get(cameraControl, "cameraPitch") - 11.7f) < 0.001f,
            "Soft-lock vertical adjustment uses reduced sensitivity");
        Check(Mathf.Abs((float)Get(cameraControl, "cameraYaw") - yaw - 6f) < 0.001f,
            "Soft-lock horizontal mouse look keeps its original sensitivity");
        Set(cameraControl, "lockOnVerticalRange", 0f);
        Call(cameraControl, "UpdateCameraOrbit", Vector2.up * 10000, 0.02f);
        Check(Mathf.Abs((float)Get(cameraControl, "cameraPitch") - 12f) < 0.001f,
            "Zero vertical range fully fixes soft-lock orbit pitch");
        Set(cameraControl, "lockOnVerticalRange", 3f);
        var enemy = Enemy(player.transform.position + Vector3.forward * 20);
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        targeting.ToggleHardLock();
        for (int i = 0; i < 120; i++) Call(cameraControl, "UpdateCameraOrbit", Vector2.zero, 0.02f);
        float hardPitch = (float)Get(cameraControl, "cameraPitch");
        Call(cameraControl, "UpdateCameraOrbit", Vector2.up * 10000, 0.02f);
        Call(cameraControl, "UpdateCameraOrbit", Vector2.down * 10000, 0.02f);
        Check(targeting.IsHardLocked && Mathf.Abs((float)Get(cameraControl, "cameraPitch") - hardPitch) < 0.001f,
            "Hard lock completely ignores up/down mouse movement for camera pitch");
        targeting.ToggleHardLock();
        cameraRoot.transform.rotation = Quaternion.Euler(-15, 40, 0);
        Call(cameraControl, "UpdateCameraOrbit", Vector2.zero, 0.02f);
        Check((float)Get(cameraControl, "cameraPitch") >= 9f && (float)Get(cameraControl, "cameraPitch") <= 15f,
            "Returning from hard to soft lock cannot inherit a body-level view");
        targeting.ToggleHardLock();
        targeting.EnterFreeAim();
        enemy.gameObject.SetActive(false);
        targeting.ResumeTargeting();
        Call(cameraControl, "UpdateCameraOrbit", Vector2.up * 10000, 0.02f);
        Check(!targeting.IsFreeAim && !targeting.IsHardLocked && (float)Get(cameraControl, "cameraPitch") >= 9f,
            "Remembered hard mode waiting for an enemy also limits vertical mouse look");
        targeting.EnterFreeAim();
        Call(cameraControl, "UpdateCameraOrbit", Vector2.down * 10000, 0.02f);
        Check(Mathf.Abs((float)Get(cameraControl, "cameraPitch") - 30f) < 0.001f,
            "Returning to free aim restores the full downward pitch range");
        player.SetActive(false);
        cameraRoot.SetActive(false);
        view.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    static void CheckEnemyIndependentMovement(Camera view)
    {
        var player = new GameObject("Independent Movement Player");
        player.transform.position = new Vector3(-100, 1.1f, 0);
        // Attach targeting first so this test also catches a motor caching it in Awake.
        var targeting = player.AddComponent<MechTargetLockController>();
        var motor = player.AddComponent<MechMovementController>();
        var enemyA = Enemy(new Vector3(-80, 1, 30));
        var enemyB = Enemy(new Vector3(-120, 1, -30));
        view.transform.rotation = Quaternion.Euler(15, 45, 0);
        Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
        Physics.SyncTransforms();
        for (int i = 0; i < 30; i++) Step(motor, view);

        Vector2[] inputs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        foreach (Vector2 input in inputs)
        {
            Vector3 expected = forward * input.y + right * input.x;
            foreach (MechTargetable target in new[] { enemyA, enemyB, null })
            {
                Call(targeting, "SetTarget", new object[] { target });
                Step(motor, view, input);
                Check(Vector3.Distance(motor.MoveDirection, expected) < 0.001f,
                    $"Input {input} remains camera-relative with target {(target != null ? target.name : "none")}");
                Check(Vector3.Distance(motor.FacingDirection, forward) < 0.001f,
                    "Target acquisition, switching, and clearing do not steer body facing");
            }
        }

        Call(targeting, "SetTarget", enemyA);
        Step(motor, view, Vector2.up);
        enemyA.transform.position = new Vector3(-140, 20, -20);
        Physics.SyncTransforms();
        Step(motor, view, Vector2.up, sprint: true);
        Check(Vector3.Distance(motor.MoveDirection, forward) < 0.001f,
            "Moving selected enemy cannot redirect ground boost");
        for (int i = 0; i < 30; i++) Step(motor, view);
        Step(motor, view, Vector2.right, boost: true);
        Check(motor.IsQuickBoosting && Vector3.Distance(motor.Velocity.normalized, right) < 0.02f,
            "Quick Boost launches camera-right while an enemy is selected");
        Call(targeting, "SetTarget", enemyB);
        Step(motor, view, Vector2.up);
        Check(Vector3.Distance(motor.FacingDirection, right) < 0.001f,
            "Switching enemies cannot steer an active dodge");
        for (int i = 0; i < 40; i++) Step(motor, view);
        Step(motor, view, boost: true);
        Check(motor.IsQuickBoosting && Vector3.Distance(motor.FacingDirection, forward) < 0.001f,
            "Quick Boost without direction uses camera forward, not enemy direction");
    }

    static void CheckGroundFeatures(Camera view)
    {
        view.transform.rotation = Quaternion.identity;
        var player = new GameObject("Ground Feature Player");
        player.tag = "Player";
        player.transform.position = new Vector3(60, 1.1f, -60);
        var energy = player.AddComponent<MechEnergyController>();
        var motor = player.AddComponent<MechMovementController>();
        Check(energy.Current == 100f && energy.TrySpend(20f) && energy.Current == 80f,
            "Energy starts full and spends exact costs");
        Check(!energy.TrySpend(81f) && energy.Current == 80f, "Overspending leaves energy unchanged");
        energy.TickRecovery(0.5f, true);
        Check(energy.Current == 80f, "Energy respects recovery delay");
        energy.TickRecovery(0.5f, true);
        Check(Mathf.Abs(energy.Current - 90f) < 0.01f, "Recovery integrates only time after the delay");
        energy.TickRecovery(10f, true);
        Check(energy.Current == 100f, "Regeneration clamps at capacity");
        Physics.SyncTransforms();
        for (int i = 0; i < 30; i++) Step(motor, view);
        Step(motor, view, Vector2.up, boost: true);
        Check(motor.IsQuickBoosting && energy.Current == 80f, "Quick Boost spends energy once on admission");
        for (int i = 0; i < 40; i++) Step(motor, view);
        energy.TrySpend(energy.Current);
        Step(motor, view, boost: true);
        Check(!motor.IsQuickBoosting && !motor.CanQuickBoost, "Empty energy blocks Quick Boost");
        energy.TickRecovery(10f, true);
        int started = 0, ended = 0;
        motor.AssaultBoostStarted += () => started++;
        motor.AssaultBoostEnded += () => ended++;
        for (int i = 0; i < 50; i++) Step(motor, view, Vector2.up, assault: true);
        Check(motor.IsAssaultBoosting && started == 1 && motor.State == MechMovementController.MovementState.AssaultBoost,
            "Holding Assault Boost enters its state once");
        Check(Mathf.Abs(motor.Velocity.z - 28f) < 0.02f && energy.Current < 83f && energy.Current > 81f,
            "Assault Boost reaches sustained speed and drains energy over time");
        Vector3 priorDirection = motor.FacingDirection;
        view.transform.rotation = Quaternion.Euler(0, 90, 0);
        Step(motor, view, Vector2.up, assault: true);
        float turn = Vector3.Angle(priorDirection, motor.FacingDirection);
        Check(turn > 1f && turn < 1.81f, "Assault steering is limited to 90 degrees per second");
        Step(motor, view);
        Check(!motor.IsAssaultBoosting && ended == 1, "Releasing Assault Boost cancels it");
        Step(motor, view, assault: true);
        Check(motor.IsAssaultBoosting && started == 2, "Assault Boost can restart after a release");
        Step(motor, view, Vector2.left, boost: true, assault: true);
        Check(motor.IsQuickBoosting && !motor.IsAssaultBoosting && ended == 2,
            "Quick Boost cancels Assault Boost and uses camera-relative dodge input");
        for (int i = 0; i < 40; i++) Step(motor, view, assault: true);
        Check(!motor.IsAssaultBoosting, "Cancelled Assault Boost requires releasing the button");
        Step(motor, view);
        energy.TickRecovery(10f, true);
        for (int i = 0; i < 310; i++) Step(motor, view, assault: true);
        Check(!motor.IsAssaultBoosting && energy.Current < 10f, "Energy exhaustion ends Assault Boost");
        energy.TickRecovery(10f, true);
        Step(motor, view, assault: true);
        Check(!motor.IsAssaultBoosting, "Recovered energy cannot restart an exhausted held boost automatically");
        Step(motor, view);
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = new Vector3(60, 10, -60);
        controller.enabled = true;
        Step(motor, view, assault: true);
        Check(!motor.IsAssaultBoosting, "Assault Boost cannot start while airborne");
        for (int i = 0; i < 100; i++) Step(motor, view);

        player.SetActive(false);
        var visual = new GameObject("VisualMech").transform;
        visual.SetParent(player.transform, false);
        var presentation = player.AddComponent<MechBoostPresentation>();
        Set(presentation, "visualRoot", visual);
        Set(presentation, "movement", motor);
        Set(presentation, "playAudio", false);
        Set(presentation, "boostMaterial", new Material(Shader.Find("Mech/BoostGlow")));
        player.SetActive(true);
        for (int i = 0; i < 30; i++) Step(motor, view);
        energy.TickRecovery(10f, true);
        Step(motor, view, Vector2.up, boost: true);
        Vector3 position = player.transform.position;
        Quaternion rotation = player.transform.rotation;
        Bounds bounds = controller.bounds;
        Call(presentation, "UpdatePresentation", 0.02f);
        Check(Quaternion.Angle(visual.localRotation, Quaternion.identity) > 0.1f,
            "Boost feedback leans the visual child");
        Check(player.transform.position == position && player.transform.rotation == rotation && controller.bounds == bounds,
            "Visual feedback does not move or tilt the root collider");
        Check(((ParticleSystem)Get(presentation, "thrusters")).emission.enabled,
            "Thruster emission starts during boost");
        presentation.enabled = false;
        Check(visual.localRotation == Quaternion.identity && !((TrailRenderer[])Get(presentation, "trails"))[0].emitting,
            "Disabling presentation resets lean and stops trails");

        var cameraRoot = new GameObject("Ground Camera");
        cameraRoot.SetActive(false);
        cameraRoot.AddComponent<Camera>();
        var cameraControl = cameraRoot.AddComponent<CameraControlThirdPerson>();
        Set(cameraControl, "target", player.transform);
        cameraRoot.SetActive(true);
        var rig = (CinemachineCamera)Get(cameraControl, "cinemachineCamera");
        var follow = rig.GetComponent<CinemachineThirdPersonFollow>();
        Check(follow.AvoidObstacles.Enabled && follow.AvoidObstacles.IgnoreTag == "Player",
            "Camera avoids obstacles and ignores the player");
        Set(motor, "quickBoostTimer", 0.24f);
        Call(cameraControl, "UpdateMovementFeedback", 0.1f);
        Call(cameraControl, "ApplySettings");
        Check(rig.Lens.FieldOfView > 60f && rig.Lens.FieldOfView <= 67f, "Quick Boost produces a bounded FOV pulse");
        Set(motor, "quickBoostTimer", 0f);
        Call(cameraControl, "UpdateMovementFeedback", 2f);
        Call(cameraControl, "ApplySettings");
        Check(Mathf.Abs(rig.Lens.FieldOfView - 60f) < 0.01f, "Camera FOV returns to normal after boost");
        Call(cameraControl, "UpdateCameraTargetRotation");
        rig.InternalUpdateCameraState(Vector3.up, -1f);
        Vector3 anchor = rig.Follow.position;
        Vector3 clearPosition = rig.State.GetFinalPosition();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = Vector3.Lerp(anchor, clearPosition, 0.55f);
        wall.transform.rotation = Quaternion.LookRotation(clearPosition - anchor);
        wall.transform.localScale = new Vector3(20f, 20f, 0.5f);
        Physics.SyncTransforms();
        rig.InternalUpdateCameraState(Vector3.up, -1f);
        Check(Vector3.Distance(rig.State.GetFinalPosition(), anchor) < Vector3.Distance(clearPosition, anchor) - 1f,
            "A wall physically pulls the camera in front of the obstruction");
        Object.DestroyImmediate(wall);
    }

    static void CheckHardLock(Camera view)
    {
        view.transform.SetPositionAndRotation(new Vector3(0, 1.1f, 130), Quaternion.identity);
        var player = new GameObject("Hard Lock Player");
        player.tag = "Player";
        player.transform.position = view.transform.position;
        var targeting = player.AddComponent<MechTargetLockController>();
        Set(targeting, "gameplayCamera", view);
        var motor = player.AddComponent<MechMovementController>();
        var a = Enemy(new Vector3(0, 1.1f, 150));
        var right = Enemy(new Vector3(4, 1.1f, 150));
        var fartherRight = Enemy(new Vector3(8, 1.1f, 150));
        var left = Enemy(new Vector3(-4, 1.1f, 150));
        var up = Enemy(new Vector3(0, 5.1f, 150));
        var aim = new GameObject("Aim point").transform;
        aim.SetParent(right.transform, false);
        aim.localPosition = Vector3.up * 0.25f;
        Set(right, "aimPoint", aim);
        Physics.SyncTransforms();
        targeting.ResumeTargeting();
        Call(targeting, "LateUpdate");
        Check(!targeting.IsHardLocked && targeting.CurrentTarget == a, "Explicit targeting starts in soft mode with normal cursor selection");
        int modeChanges = 0;
        targeting.HardLockChanged += active => modeChanges++;
        targeting.ToggleHardLock();
        Check(targeting.IsHardLocked && targeting.CurrentTarget == a && modeChanges == 1,
            "Toggle locks the selected enemy and reports one mode change");
        view.transform.rotation = Quaternion.LookRotation(right.AimPosition - view.transform.position);
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == a, "Center crossing cannot automatically replace a hard-locked target");
        view.transform.rotation = Quaternion.identity;
        for (int i = 0; i < 100; i++) targeting.ProcessSwitchInput(Vector2.right, 0.02f);
        Check(targeting.CurrentTarget == a, "Small mouse jitter cannot accumulate into a target switch");
        targeting.ProcessSwitchInput(Vector2.right * 45f, 0.02f);
        targeting.ProcessSwitchInput(Vector2.right * 45f, 0.02f);
        Check(targeting.CurrentTarget == right, "Deliberate right flick selects the nearest enemy in that screen direction");
        for (int i = 0; i < 30; i++) targeting.ProcessSwitchInput(Vector2.right * 100f, 0.02f);
        Check(targeting.CurrentTarget == right, "One sustained swipe cannot cycle through multiple enemies");
        targeting.ProcessSwitchInput(Vector2.zero, 0.3f);
        targeting.ProcessSwitchInput(Vector2.left * 90f, 0.02f);
        Check(targeting.CurrentTarget == a, "Settling the mouse rearms a flick back to the left");
        targeting.ProcessSwitchInput(Vector2.zero, 0.3f);
        targeting.ProcessSwitchInput(Vector2.up * 90f, 0.02f);
        Check(targeting.CurrentTarget == up, "Vertical flicks switch to enemies above the current target");
        targeting.ProcessSwitchInput(Vector2.zero, 0.3f);
        targeting.ProcessSwitchInput(Vector2.up * 90f, 0.02f);
        Check(targeting.CurrentTarget == up, "A flick toward empty space keeps the current enemy");

        Call(targeting, "SetTarget", right);
        for (int i = 0; i < 30; i++) Step(motor, view);
        Vector3 expectedFacing = Vector3.ProjectOnPlane(right.AimPosition - player.transform.position, Vector3.up).normalized;
        Check(Vector3.Angle(player.transform.forward, expectedFacing) < 0.1f,
            "The whole mech turns toward the hard-locked enemy even while idle");
        Check(Vector3.Dot(player.transform.up, Vector3.up) > 0.99999f,
            "Hard lock keeps the movement root upright");
        Step(motor, view, Vector2.left, boost: true);
        Vector3 dodge = (Vector3)Get(motor, "quickBoostDirection");
        Check(motor.IsQuickBoosting && Vector3.Distance(dodge, Vector3.left) < 0.001f,
            "Hard-locked dodge still launches from camera-relative movement input");
        Call(targeting, "SetTarget", left);
        Step(motor, view, Vector2.right);
        Check(Vector3.Distance((Vector3)Get(motor, "quickBoostDirection"), dodge) < 0.001f
            && Vector3.Dot(Vector3.ProjectOnPlane(motor.Velocity, Vector3.up).normalized, dodge) > 0.999f,
            "Switching hard-lock targets cannot redirect an active dodge");
        Check(Vector3.Distance(motor.MoveDirection, Vector3.right) < 0.001f,
            "Hard lock keeps WASD camera-relative");

        var cameraRoot = new GameObject("Hard Lock Camera");
        cameraRoot.SetActive(false);
        var actualView = cameraRoot.AddComponent<Camera>();
        var cameraControl = cameraRoot.AddComponent<CameraControlThirdPerson>();
        Set(cameraControl, "target", player.transform);
        cameraRoot.SetActive(true);
        var rig = (CinemachineCamera)Get(cameraControl, "cinemachineCamera");
        Call(targeting, "SetTarget", right);
        Call(cameraControl, "UpdateCameraTargetRotation");
        Call(cameraControl, "ApplySettings");
        rig.InternalUpdateCameraState(Vector3.up, -1f);
        Check(rig.LookAt == aim && rig.Follow.parent == player.transform,
            "Hard-lock camera follows the player while aiming at the enemy's custom aim point");
        actualView.transform.SetPositionAndRotation(rig.State.GetFinalPosition(), rig.State.GetFinalOrientation());
        Vector3 projected = actualView.WorldToViewportPoint(right.AimPosition);
        Check(projected.z > 0 && Vector2.Distance(projected, new Vector2(0.5f, 0.5f)) < 0.001f,
            "Actual hidden center aim points directly at the enemy");
        // Check visibility, not just reticle centering: a centered enemy can still
        // be hidden behind the player's body when the camera orbit is too low.
        var bodyBounds = new Bounds(player.transform.position, new Vector3(2, 3, 1));
        foreach (float enemyDistance in new[] { 20f, 4f, 2f })
        {
            right.transform.position = player.transform.position + new Vector3(0, -0.75f, enemyDistance);
            for (int i = 0; i < 60; i++) Call(cameraControl, "UpdateCameraOrbit", Vector2.zero, 0.02f);
            Call(cameraControl, "ApplySettings");
            rig.InternalUpdateCameraState(Vector3.up, -1f);
            Vector3 cameraPosition = rig.State.GetFinalPosition();
            var sightline = new Ray(cameraPosition, (right.AimPosition - cameraPosition).normalized);
            Check(cameraPosition.y > bodyBounds.max.y && !bodyBounds.IntersectRay(sightline),
                $"Elevated hard-lock sightline clears the mech body at enemy distance {enemyDistance} (camera {cameraPosition}, body {bodyBounds})");
            Check((rig.State.GetFinalOrientation() * Vector3.forward).y < 0f,
                "Hard-lock camera looks down at ground-level enemies");
        }
        right.transform.position = player.transform.position + new Vector3(10, 3, -15);
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Check(targeting.IsHardLocked && targeting.CurrentTarget == right,
            "Hard lock retains an enemy crossing behind the camera while it catches up");
        Call(cameraControl, "UpdateCameraTargetRotation");
        Call(cameraControl, "ApplySettings");
        rig.InternalUpdateCameraState(Vector3.up, -1f);
        actualView.transform.SetPositionAndRotation(rig.State.GetFinalPosition(), rig.State.GetFinalOrientation());
        projected = actualView.WorldToViewportPoint(right.AimPosition);
        Check(projected.z > 0 && Vector2.Distance(projected, new Vector2(0.5f, 0.5f)) < 0.001f,
            "Camera follows enemy motion and keeps center aim aligned");
        targeting.ToggleHardLock();
        Call(cameraControl, "UpdateCameraTargetRotation");
        Call(cameraControl, "ApplySettings");
        Check(!targeting.IsHardLocked && rig.LookAt == rig.Follow && modeChanges == 2,
            "Toggling off restores normal player framing and free-look mode");
        float freeYaw = (float)Get(cameraControl, "cameraYaw");
        right.transform.position += Vector3.right * 10;
        Call(cameraControl, "UpdateCameraTargetRotation");
        Check(Mathf.Abs(Mathf.DeltaAngle(freeYaw, (float)Get(cameraControl, "cameraYaw"))) < 0.001f,
            "Enemy motion no longer turns the camera after unlocking");

        targeting.ToggleHardLock();
        targeting.CurrentTarget.enabled = false;
        Call(targeting, "LateUpdate");
        Check(!targeting.IsHardLocked, "Disabling the enemy releases hard lock into free mode");
        targeting.ToggleHardLock();
        targeting.CurrentTarget.transform.position = player.transform.position + Vector3.forward * 100;
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        Check(!targeting.IsHardLocked, "Leaving lock range releases camera and mech facing");
        targeting.ToggleHardLock();
        Object.DestroyImmediate(targeting.CurrentTarget.gameObject);
        Call(targeting, "LateUpdate");
        Check(!targeting.IsHardLocked, "Destroying a locked enemy safely releases hard lock");
        foreach (var enemy in new[] { a, right, fartherRight, left, up })
            if (enemy != null) enemy.gameObject.SetActive(false);
        Physics.SyncTransforms();
        Call(targeting, "LateUpdate");
        targeting.ToggleHardLock();
        Check(!targeting.IsHardLocked && targeting.CurrentTarget == null, "Toggle with no available enemy stays in free mode");
        player.SetActive(false);
        cameraRoot.SetActive(false);
    }

    static void CheckMovement(Camera view)
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0, -0.5f, 0);
        ground.transform.localScale = new Vector3(500, 1, 500);
        var player = new GameObject("Movement Player");
        player.transform.position = new Vector3(0, 1.1f, 0);
        var motor = player.AddComponent<MechMovementController>();
        int started = 0, ended = 0, landed = 0, states = 0;
        float impact = 0;
        motor.QuickBoostStarted += direction => started++;
        motor.QuickBoostEnded += () => ended++;
        motor.Landed += speed => { landed++; impact = speed; };
        motor.StateChanged += (previous, next) => states++;
        Physics.SyncTransforms();
        for (int i = 0; i < 30; i++) Step(motor, view);
        Check(motor.IsGrounded && motor.State == MechMovementController.MovementState.Grounded,
            "Motor reports grounded after settling");
        int previousStates = states;
        for (int i = 0; i < 5; i++) Step(motor, view);
        Check(states == previousStates, "Unchanged movement state emits no extra events");
        Step(motor, view, Vector2.up);
        Check(Mathf.Abs(((Vector3)Get(motor, "planarVelocity")).z - 0.4f) < 0.001f,
            "Ground acceleration preserves original 20 units per second squared");
        for (int i = 0; i < 30; i++) Step(motor, view, Vector2.up);
        Check(Mathf.Abs(motor.Velocity.z - 6f) < 0.01f, "Normal ground speed remains 6");
        for (int i = 0; i < 30; i++) Step(motor, view, Vector2.up, sprint: true);
        Check(Mathf.Abs(motor.Velocity.z - 12f) < 0.01f
            && motor.State == MechMovementController.MovementState.GroundBoost, "Ground boost remains 12 and reports state");
        Step(motor, view);
        Check(Mathf.Abs(((Vector3)Get(motor, "planarVelocity")).z - 11.36f) < 0.01f,
            "Deceleration preserves original 32 units per second squared");
        Step(motor, view, Vector2.right, boost: true);
        Check(started == 1 && motor.IsQuickBoosting && motor.QuickBoostProgress == 0f,
            "Quick Boost starts once at progress zero");
        Check(Mathf.Abs(motor.Velocity.x - 27.3f) < 0.02f, "Quick Boost ignition preserves curve and speed multipliers");
        view.transform.rotation = Quaternion.Euler(0, 90, 0);
        Step(motor, view, Vector2.left);
        Check(((Vector3)Get(motor, "quickBoostDirection") - Vector3.right).sqrMagnitude < 0.001f
            && motor.QuickBoostProgress > 0, "Dodge direction stays fixed while camera and input change");
        for (int i = 0; i < 15; i++) Step(motor, view);
        Check(!motor.IsQuickBoosting && ended == 1, "Boost finishes once at original duration");
        Step(motor, view, boost: true);
        Check(started == 1 && !motor.CanQuickBoost, "Recovery blocks immediate repeat boost");
        for (int i = 0; i < 30; i++) Step(motor, view);
        Check(motor.CanQuickBoost, "Recovery makes boost available again");
        int priorLandings = landed;
        Step(motor, view, jump: true);
        Check(!motor.IsGrounded && motor.State == MechMovementController.MovementState.Airborne,
            "Jump transitions to airborne");
        Step(motor, view, boost: true);
        Check(started == 1, "Airborne boost remains unavailable in the current rules");
        for (int i = 0; i < 100; i++) Step(motor, view);
        Check(landed == priorLandings + 1 && impact > 0, "Landing emits one impact-speed event");
        Step(motor, view, Vector2.right, boost: true);
        motor.enabled = false;
        Check(ended == 2 && !motor.IsQuickBoosting && motor.Velocity == Vector3.zero,
            "Disabling cancels boost and resets motion");
        motor.enabled = true;
        Step(motor, view);
        Check(!motor.IsQuickBoosting && motor.QuickBoostCooldownRemaining == 0f, "Re-enable does not resume stale boost");

        var cameraRoot = new GameObject("Rig Test Camera");
        cameraRoot.SetActive(false);
        cameraRoot.AddComponent<Camera>();
        var cameraControl = cameraRoot.AddComponent<CameraControlThirdPerson>();
        Set(cameraControl, "target", player.transform);
        cameraRoot.SetActive(true);
        var rig = (CinemachineCamera)Get(cameraControl, "cinemachineCamera");
        var anchor = (Transform)Get(cameraControl, "cameraTarget");
        Check(rig.Follow == anchor && rig.LookAt == anchor && anchor.parent == player.transform,
            "Camera follows the independent player anchor");
        Check(Mathf.Abs(rig.GetComponent<CinemachineThirdPersonFollow>().CameraDistance - 13f) < 0.001f,
            "Camera retains active normal-profile distance");
        cameraControl.enabled = false;
        Check(!rig.gameObject.activeSelf, "Disabling camera disables its generated rig");
        cameraControl.enabled = true;
        Check(rig.gameObject.activeSelf, "Re-enabling camera restores its rig");
    }
}
