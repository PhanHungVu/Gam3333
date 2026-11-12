using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

// Real enemy/player prefabs and audio voices; no timing changes to attack gameplay.
public static class EnemyAttackAudioChecks
{
    const string Pending = "EnemyAttackAudioChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static GameObject player;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("ENEMY AUDIO PASS: " + message); }
    static AudioSource Voice(MechWeaponAnimator weapon) => weapon.transform.Find("Weapon Attack Audio")?.GetComponent<AudioSource>();
    static bool Quiet(MechWeaponAnimator weapon) => Voice(weapon) == null || !Voice(weapon).isPlaying;
    static float ReleaseAt(EnemyAttack attack) => attack.AttackDuration * new SerializedObject(attack).FindProperty("releaseFrame").floatValue;
    static void Step(EnemyAttack attack, float dt)
    {
        foreach (var weapon in attack.Weapons) if (weapon != null) weapon.Tick(dt);
        attack.Tick(dt);
    }
    [InitializeOnLoadMethod] static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run(); };
    }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static GameObject Enemy(string path, float distance)
    {
        var gate = new GameObject("Controlled enemy audio"); gate.SetActive(false);
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), gate.transform);
        root.transform.SetPositionAndRotation(new Vector3(0, 1, distance), Quaternion.LookRotation(Vector3.back));
        if (root.TryGetComponent<EnemyGraphController>(out var graph)) graph.enabled = false;
        if (root.TryGetComponent<Unity.Behavior.BehaviorGraphAgent>(out var graphAgent)) graphAgent.enabled = false;
        root.GetComponent<EnemyAgent>().enabled = false;
        root.GetComponent<EnemyPartDrop>().enabled = false;
        gate.SetActive(true);
        var agent = root.GetComponent<EnemyAgent>(); agent.PreferredTarget = player.transform; agent.RefreshTarget();
        Physics.SyncTransforms(); return root;
    }
    static void Cleanup(GameObject root)
    {
        if (root != null) Object.DestroyImmediate(root.transform.parent.gameObject);
        foreach (var shot in Object.FindObjectsByType<MechProjectile>()) Object.DestroyImmediate(shot.gameObject);
        foreach (var corpse in Object.FindObjectsByType<CombatRagdoll>()) Object.DestroyImmediate(corpse.gameObject);
    }
    static void Run()
    {
        SessionState.SetBool(Pending, false); AudioListener.pause = false; AudioListener.volume = 0;
        try
        {
            PreMissionReliabilityChecks.BeginLogAudit();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(150, .5f, 150);
            player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath), new Vector3(0, 1.6f, 0), Quaternion.identity);
            player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
            player.GetComponent<MechTargetLockController>().EnterFreeAim(); player.GetComponent<CombatFeedback>().enabled = false;
            player.GetComponent<CombatHealth>().SetMaximum(100000, true);
            foreach (int role in new[] { 0, 1, 2, 3, 5, 6 }) Basic(EnemyRosterSetup.Roster[role], role < 4 ? 2 : 8);
            Basic(FodderModelSetup.FodderPath, 8);
            Basic("Assets/Prefabs/Enemies/Elite Enemies/Enemy_Elite_Crossbow.prefab", 8);
            CancellationAndBlocking(); IndependentWeapons(); SpecialAttacks();
            var combat = player.GetComponent<MechCombat>(); var weapon = combat.GetPart(PartCategory.ArmL).GetComponent<MechWeaponAnimator>();
            int sounds = weapon.AttackSoundsPlayed;
            Check(combat.TryAttack(PartCategory.ArmL).Started && weapon.AttackSoundsPlayed == sounds + 1 && Voice(weapon).isPlaying,
                "Player melee retains its existing immediate attack sound");
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); PreMissionReliabilityChecks.EndLogAudit();
            Debug.Log("ENEMY ATTACK AUDIO CHECKS COMPLETE: " + checks); EditorApplication.Exit(0);
        }
        catch (Exception error) { PreMissionReliabilityChecks.EndLogAudit(); Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void Basic(string path, float distance)
    {
        var root = Enemy(path, distance); var attack = root.GetComponent<EnemyAttack>();
        int shots = 0;
        Action<MechProjectile, Vector3, Transform> observe = (shot, origin, owner) => { if (owner == root.transform) shots++; };
        CombatVisualEvents.ShotReleased += observe;
        try
        {
            Check(attack.TryAttack(), root.name + " starts its normal attack");
            var weapon = attack.Weapon; float release = ReleaseAt(attack); float health = player.GetComponent<CombatHealth>().CurrentHealth;
            Check(weapon.AttackSoundsPlayed == 0 && Quiet(weapon) && attack.IsTelegraphing && shots == 0,
                root.name + " starts the warning without attack audio or projectiles");
            Step(attack, release * .5f);
            Check(weapon.AttackSoundsPlayed == 0 && Quiet(weapon) && player.GetComponent<CombatHealth>().CurrentHealth == health,
                root.name + " remains silent before the release frame");
            Step(attack, release * .5f + .001f);
            Check(weapon.AttackSoundsPlayed == 1 && Voice(weapon) != null && Voice(weapon).isPlaying
                && Voice(weapon).clip == weapon.Animations.attackSound && Voice(weapon).spatialBlend == 1,
                root.name + " starts exactly its own spatial attack sound at release");
            Check(attack.CurrentAttackIsMelee ? player.GetComponent<CombatHealth>().CurrentHealth < health : shots == 1,
                root.name + " sound accompanies the actual melee hit or projectile launch");
            Step(attack, .02f);
            Check(weapon.AttackSoundsPlayed == 1, root.name + " recovery does not replay the sound");
        }
        finally { CombatVisualEvents.ShotReleased -= observe; Cleanup(root); }
    }
    static void CancellationAndBlocking()
    {
        foreach (bool ranged in new[] { false, true })
        foreach (string interruption in new[] { "cancel", "target lost", "death" })
        {
            var root = Enemy(EnemyRosterSetup.Roster[ranged ? 5 : 0], ranged ? 8 : 2); var attack = root.GetComponent<EnemyAttack>();
            Check(attack.TryAttack(), interruption + " fixture begins an attack"); var weapon = attack.Weapon; float release = ReleaseAt(attack);
            Step(attack, release * .3f);
            if (interruption == "cancel") attack.CancelPendingAttack();
            else if (interruption == "target lost") player.SetActive(false);
            else root.GetComponent<CombatHealth>().ApplyDamage(100000);
            Step(attack, attack.AttackDuration + 1);
            Check(weapon.AttackSoundsPlayed == 0 && Quiet(weapon), (ranged ? "Ranged " : "Melee ") + interruption + " before release never plays an attack sound");
            player.SetActive(true); Cleanup(root);
        }
        foreach (bool wallBlocked in new[] { false, true })
        {
            var root = Enemy(EnemyRosterSetup.Roster[1], 2); var attack = root.GetComponent<EnemyAttack>();
            Check(attack.TryAttack(), "Release-time obstruction fixture starts a melee windup"); var weapon = attack.Weapon;
            GameObject wall = null;
            if (wallBlocked)
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 2, 1); wall.transform.localScale = new Vector3(6, 4, .2f);
            }
            else player.transform.position += Vector3.right * 15;
            Physics.SyncTransforms(); Step(attack, attack.AttackDuration + .01f);
            Check(weapon.AttackSoundsPlayed == 0 && Quiet(weapon), wallBlocked ? "A wall preventing the melee strike also prevents its sound" : "A target out of melee range does not cause a phantom strike sound");
            if (wall != null) Object.DestroyImmediate(wall); else player.transform.position -= Vector3.right * 15;
            Cleanup(root); Physics.SyncTransforms();
        }
        var reused = Enemy(EnemyRosterSetup.Roster[1], 2); var repeated = reused.GetComponent<EnemyAttack>();
        Check(repeated.TryAttack(), "Reusable voice fixture starts"); var current = repeated.Weapon;
        Step(repeated, ReleaseAt(repeated) + .01f);
        Check(current.AttackSoundsPlayed == 1 && Voice(current).isPlaying, "First valid release starts the reusable voice");
        var settings = new SerializedObject(repeated); settings.FindProperty("weapons").arraySize = 1; settings.ApplyModifiedPropertiesWithoutUndo();
        repeated.CancelPendingAttack(); Step(repeated, repeated.CooldownRemaining + .1f);
        Check(repeated.TryAttack() && repeated.Weapon == current && Quiet(current), "A new windup stops the preceding voice instead of replaying it early");
        repeated.CancelPendingAttack(); Step(repeated, repeated.AttackDuration + .1f);
        Check(current.AttackSoundsPlayed == 1 && Quiet(current), "Cancelling the next windup cannot restart the previous sound");
        Cleanup(reused);
    }
    static void IndependentWeapons()
    {
        var root = Enemy(EnemyRosterSetup.Roster[1], 2); var attack = root.GetComponent<EnemyAttack>();
        var fields = new SerializedObject(attack); fields.FindProperty("independentWeapons").boolValue = true;
        fields.FindProperty("weapons").GetArrayElementAtIndex(1).FindPropertyRelative("cooldownMultiplier").floatValue *= 2;
        fields.ApplyModifiedPropertiesWithoutUndo();
        Check(attack.TryAttack() && attack.Weapons.All(w => w.AttackSoundsPlayed == 0), "Independent weapons both begin silently");
        var first = attack.Weapons[0]; var second = attack.Weapons[1];
        float release = attack.CooldownOf(first) * fields.FindProperty("releaseFrame").floatValue;
        Step(attack, release + .001f);
        Check(first.AttackSoundsPlayed == 1 && second.AttackSoundsPlayed == 0, "Each weapon waits for its own damage-release clock");
        Step(attack, release + .001f);
        Check(first.AttackSoundsPlayed == 1 && second.AttackSoundsPlayed == 1, "The slower weapon releases one independent voice later");
        Cleanup(root);
    }
    static void SpecialAttacks()
    {
        var field = typeof(EnemyGraphAttack).GetField("attackSound", Private);
        Check(field.GetCustomAttributes<FormerlySerializedAsAttribute>().Any(a => a.oldName == "windupSound"), "Existing authored special clips retain their serialized reference during migration");
        var clip = AudioClip.Create("Silent audio timing fixture", 48000, 1, 48000, false);
        foreach (int role in new[] { 4, 7, 8 })
        {
            var root = Enemy(EnemyRosterSetup.Roster[role], 4); var special = root.GetComponent<EnemyGraphAttack>();
            var fields = new SerializedObject(special); fields.FindProperty("attackSound").objectReferenceValue = clip; fields.ApplyModifiedPropertiesWithoutUndo();
            Check(special.BeginWindup(), root.name + " begins its existing special warning");
            special.TickWindup(.2f);
            var source = (AudioSource)typeof(EnemyGraphAttack).GetField("audioSource", Private).GetValue(special);
            Check(source == null || !source.isPlaying, root.name + " optional authored attack audio stays silent during windup");
            special.BeginAttack(); source = (AudioSource)typeof(EnemyGraphAttack).GetField("audioSource", Private).GetValue(special);
            Check(source != null && source.isPlaying && special.CurrentStage == EnemyGraphAttack.Stage.Attack,
                root.name + " plays its optional sound when the charge or throw becomes active");
            special.Cancel(); Check(!source.isPlaying, root.name + " cancellation stops the special voice");
            Cleanup(root);
        }
        Object.DestroyImmediate(clip);
    }
}
