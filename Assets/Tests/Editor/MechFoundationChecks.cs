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
        bool sprint = false, bool jump = false, bool boost = false, float dt = 0.02f)
    {
        Type inputType = typeof(MechMovementController).GetNestedType("MovementInput", BindingFlags.NonPublic);
        object input = Activator.CreateInstance(inputType);
        inputType.GetField("move").SetValue(input, move);
        inputType.GetField("sprint").SetValue(input, sprint);
        inputType.GetField("jump").SetValue(input, jump);
        inputType.GetField("quickBoost").SetValue(input, boost);
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
            CheckMovement(view);
            CheckEnemyIndependentMovement(view);
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
        Check(targeting.CurrentTarget == a && changes == 1, "Acquire nearest center target and publish one change");
        Call(targeting, "LateUpdate");
        Check(changes == 1, "Keeping a target does not repeat change events");
        view.transform.rotation = Quaternion.LookRotation(new Vector3(4, 0, 20));
        Call(targeting, "LateUpdate");
        Check(targeting.CurrentTarget == a, "Free look preserves sticky target until center crosses an enemy");
        Call(tracker, "LateUpdate");
        Check(Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, cursor.position),
            view.WorldToScreenPoint(a.AimPosition)) < 0.1f, "Cursor overlays target after camera rotation");
        view.transform.rotation = Quaternion.LookRotation(b.AimPosition);
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
