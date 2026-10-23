using System;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CombatFeedbackChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Flags).SetValue(obj, value);
    static object Get(object obj, string field) => obj.GetType().GetField(field, Flags).GetValue(obj);
    static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);

    static CombatHealth Enemy(Vector3 position, Action died)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.transform.position = position;
        obj.AddComponent<MechTargetable>();
        var health = obj.AddComponent<CombatHealth>();
        health.Died += died;
        obj.AddComponent<EnemyCombatFeedback>();
        return health;
    }

    public static void Run(Action<bool, string> check)
    {
        Vector3 origin = new Vector3(8000, 2, 0);
        var player = new GameObject("Feedback Test Player");
        player.SetActive(false);
        player.transform.position = origin;
        var visual = new GameObject("VisualMech").transform;
        visual.SetParent(player.transform, false);
        visual.localRotation = Quaternion.Euler(4, 0, 3);
        var body = new GameObject("Body").transform;
        body.SetParent(visual, false);
        var combat = player.AddComponent<MechCombat>();
        var cannon = TestMech.Equip(player, (PartCategory.ShoulderL, TestMech.Cannon()))[0];
        cannon.recoilVisual = body; // recoil plays on the part that fired
        var targeting = player.AddComponent<MechTargetLockController>();
        var feedback = player.AddComponent<CombatFeedback>();
        Set(feedback, "effectMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/Combat/Prototype Combat.mat"));
        Set(feedback, "prewarmEffects", 4);
        Set(feedback, "maximumEffects", 8);
        var cameraRoot = new GameObject("Feedback Test Camera");
        cameraRoot.SetActive(false);
        var view = cameraRoot.AddComponent<Camera>();
        view.transform.SetPositionAndRotation(origin, Quaternion.identity);
        var control = cameraRoot.AddComponent<CameraControlThirdPerson>();
        Set(control, "target", player.transform);
        Set(combat, "gameplayCamera", view);
        Set(targeting, "gameplayCamera", view);
        Set(feedback, "cameraControl", control);
        cameraRoot.SetActive(true);
        player.SetActive(true);
        Call(feedback, "Start");
        var rig = (CinemachineCamera)Get(control, "cinemachineCamera");
        var follow = rig.Follow;
        var lookAt = rig.LookAt;
        float lens = rig.Lens.FieldOfView;
        int deaths = 0, impacts = 0;
        MechProjectileImpact result = default;
        combat.ProjectileImpact += value => { impacts++; result = value; };
        var a = Enemy(origin + new Vector3(0, 0, 5), () => deaths++);
        var b = Enemy(origin + new Vector3(1.5f, 0, 7), () => deaths++);
        var c = Enemy(origin + new Vector3(-1.5f, 0, 7), () => deaths++);
        var extra = new GameObject("Duplicate hurt collider").AddComponent<BoxCollider>();
        extra.transform.SetParent(b.transform, false);
        Physics.SyncTransforms();
        targeting.ResumeTargeting();
        var shot = combat.TryAttack(PartCategory.ShoulderL).Projectile;
        check(shot != null && feedback.ActiveEffects == 1 && body.localPosition != Vector3.zero,
            "Cannon fire produces a pooled muzzle flash and immediate visual recoil");
        check(player.transform.position == origin && player.transform.rotation == Quaternion.identity
            && visual.localRotation == Quaternion.Euler(4, 0, 3),
            "Recoil changes neither gameplay root nor boost lean transform");
        Call(shot, "Step", 0.2f);
        check(deaths == 3 && impacts == 1 && result.Hits == 3 && result.Kills == 3,
            "One cannon blast applies three distinct deaths and emits one aggregate result despite compound colliders");
        check(feedback.KillConfirmationCount == 1 && feedback.LastConfirmedKills == 3,
            "Three cannon kills produce exactly one stronger multi-kill confirmation");
        check(a.IsDead && b.IsDead && c.IsDead && !a.gameObject.activeInHierarchy
            && !b.gameObject.activeInHierarchy && !c.gameObject.activeInHierarchy,
            "Presentation never delays gameplay death or leaves enemy colliders active");
        Call(targeting, "LateUpdate");
        check(targeting.CurrentTarget == null && !a.ApplyDamage(10), "Dead targets cannot be selected or damaged again");
        check(feedback.ActiveEffects == 5, "Three death effects survive independently alongside muzzle and explosion visuals");
        var pool = (GameObject)Get(feedback, "poolRoot");
        check(pool.transform.parent == null && pool.GetComponentsInChildren<Collider>(true).Length == 0,
            "Pooled presentation is detached from enemies and has no interaction colliders");
        var listener = control.GetCombatImpulseListener();
        check(listener.ApplyAfter == CinemachineCore.Stage.Noise && listener.ChannelMask == 64
            && rig.Follow == follow && rig.LookAt == lookAt && rig.Lens.FieldOfView == lens,
            "Impact uses Cinemachine's additive impulse stage without changing tracking targets or FOV");
        float previousTimeOverride = CinemachineCore.CurrentTimeOverride;
        try
        {
            CinemachineCore.CurrentTimeOverride = Time.time + 0.035f;
            bool signal = CinemachineImpulseManager.Instance.GetImpulseAt(view.transform.position, false, 64,
                out Vector3 impulsePosition, out Quaternion impulseRotation);
            check(signal && impulsePosition.sqrMagnitude > 0 && impulsePosition.magnitude <= 0.2f
                && Quaternion.Angle(impulseRotation, Quaternion.identity) < 0.001f,
                "Major impact emits a bounded translational impulse with no rotational signal");
        }
        finally { CinemachineCore.CurrentTimeOverride = previousTimeOverride; }
        Call(feedback, "Tick", 0.2f);
        check(feedback.ActiveEffects == 4, "Death and explosion effects remain visible after muzzle flash expires");
        Call(feedback, "Tick", 1f);
        check(feedback.ActiveEffects == 0 && body.localPosition == Vector3.zero && body.localRotation == Quaternion.identity,
            "Effects return to the pool and recoil returns exactly to rest");

        var hitEnemy = Enemy(origin + Vector3.forward * 5, () => deaths++);
        var renderer = hitEnemy.GetComponent<Renderer>();
        var tint = new MaterialPropertyBlock();
        tint.SetColor("_BaseColor", Color.cyan);
        renderer.SetPropertyBlock(tint);
        hitEnemy.ApplyDamage(1);
        renderer.GetPropertyBlock(tint);
        check(tint.GetColor("_BaseColor").r > 1, "Nonlethal damage immediately flashes the enemy mesh without cloning materials");
        hitEnemy.GetComponent<EnemyCombatFeedback>().enabled = false;
        renderer.GetPropertyBlock(tint);
        check(tint.GetColor("_BaseColor") == Color.cyan, "Disabling enemy presentation restores its original property block");
        hitEnemy.gameObject.SetActive(false);

        Set(feedback, "presentationEnabled", false);
        Call(feedback, "Tick", 0f);
        var offA = Enemy(origin + new Vector3(0, 0, 5), () => deaths++);
        var offB = Enemy(origin + new Vector3(1.5f, 0, 7), () => deaths++);
        var offC = Enemy(origin + new Vector3(-1.5f, 0, 7), () => deaths++);
        Physics.SyncTransforms();
        TestMech.Advance(player, 3f);
        shot = combat.TryAttack(PartCategory.ShoulderL).Projectile;
        Call(shot, "Step", 0.2f);
        check(deaths == 6 && result.Kills == 3 && feedback.ActiveEffects == 0 && feedback.KillConfirmationCount == 1
            && listener.Gain == 0 && body.localPosition == Vector3.zero,
            "Master presentation toggle preserves identical triple-kill gameplay with all new feedback muted");
        check(combat.AmmoRemaining(PartCategory.ShoulderL) == 18 && TestMech.Near(combat.ReloadRemaining(PartCategory.ShoulderL), 3f),
            "Feedback does not alter ammunition or reload timing");
        Set(feedback, "presentationEnabled", true);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = origin + Vector3.forward * 10;
        wall.transform.localScale = new Vector3(6, 4, 0.2f);
        Physics.SyncTransforms();
        for (int i = 0; i < 17; i++)
        {
            TestMech.Advance(player, 3f);
            shot = combat.TryAttack(PartCategory.ShoulderL).Projectile;
            Call(shot, "Step", 0.2f);
            Call(feedback, "Tick", 1f);
        }
        check(feedback.PoolSize <= 8 && feedback.ActiveEffects == 0 && feedback.KillConfirmationCount == 1,
            "Repeated cannon impacts reuse a bounded pool without per-hit notifications or object accumulation");
        wall.transform.position = origin + new Vector3(0.325f, 0.5f, 0.325f);
        wall.transform.localScale = new Vector3(0.05f, 2, 2);
        Physics.SyncTransforms();
        TestMech.Advance(player, 3f);
        shot = combat.TryAttack(PartCategory.ShoulderL).Projectile;
        check(!shot.IsLive && impacts == 20, "Muzzle-obstructed cannon still publishes its immediate impact exactly once");
        for (int i = 0; i < 100; i++) feedback.PlayDeath(origin, 1);
        check(feedback.PoolSize == 8 && feedback.ActiveEffects == 8, "An effect burst cannot grow beyond the configured pool cap");
        feedback.enabled = false;
        check(feedback.ActiveEffects == 0 && listener.Gain == 0, "Disabling the feedback component immediately clears visuals and impulse gain");
        feedback.enabled = true;
        Set(feedback, "effectMaterial", null);
        feedback.PlayDeath(origin, 1);
        check(feedback.ActiveEffects == 0, "Missing optional effect material skips visuals safely");
        var voices = (AudioSource[])Get(feedback, "voices");
        bool silent = true;
        foreach (var voice in voices) silent &= voice == null;
        check(silent, "Empty optional audio slots remain silent without creating audio objects");
        Object.DestroyImmediate(pool);
        feedback.enabled = false;
        check(feedback.ActiveEffects == 0, "Scene unload is safe when the detached pool is destroyed before its owner");
        player.SetActive(false);
        cameraRoot.SetActive(false);
        wall.SetActive(false);
        Object.DestroyImmediate(player);
        Object.DestroyImmediate(cameraRoot);
    }
}
