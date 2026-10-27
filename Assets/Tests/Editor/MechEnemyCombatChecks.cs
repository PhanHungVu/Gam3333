using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechEnemyCombatChecks
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static EnemyAttack Enemy(string family, GameObject player, float distance)
    {
        var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Small Enemies/Enemy_" + family + ".prefab"),
            player.transform.position - Vector3.forward * distance, Quaternion.identity);
        var agent = enemy.GetComponent<EnemyAgent>(); agent.enabled = false;
        // Random physical loot from an earlier death can obstruct a later attack scenario.
        var drops = enemy.GetComponent<EnemyPartDrop>();
        if (drops != null) drops.enabled = false;
        typeof(EnemyAgent).GetField("target", Private).SetValue(agent, player.transform);
        Physics.SyncTransforms();
        return enemy.GetComponent<EnemyAttack>();
    }
    static void Step(EnemyAttack enemy, float dt)
    {
        foreach (var weapon in enemy.GetComponentsInChildren<MechWeaponAnimator>()) weapon.Tick(dt);
        enemy.Tick(dt);
    }
    static bool FromEnemy(MechProjectile p) => p.Source != null && p.Source.GetComponentInParent<EnemyAttack>() != null;
    static MechProjectile Bolt() => Object.FindObjectsByType<MechProjectile>().Single(FromEnemy);
    static void Fly(MechProjectile bolt, float dt) => typeof(MechProjectile).GetMethod("Step", Private).Invoke(bolt, new object[] { dt });
    static GameObject Wall(GameObject player, float distance)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = player.transform.position - Vector3.forward * distance;
        wall.transform.localScale = new Vector3(6f, 6f, 0.3f); Physics.SyncTransforms(); return wall;
    }
    public static void Run(Action<bool, string> check)
    {
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),
            new Vector3(52000f, 2f, 0f), Quaternion.identity);
        EnemyAttack mace = null, crossbow = null, dual = null, far = null;
        GameObject wall = null;
        try
        {
            var health = player.GetComponent<CombatHealth>(); var stats = player.GetComponent<MechStats>();
            check(health != null && health.MaximumHealth == stats.MaxHealth && health.CurrentHealth == stats.MaxHealth,
                "Player starts with real current/max health from equipped stats");
            mace = Enemy("mace", player, 2f);
            var root = mace.transform.position;
            check(mace.GetComponent<EnemyAnimation>() != null && mace.GetComponentsInChildren<MechWeaponAnimator>()
                .All(w => w.CanAnimate), "Both permanently equipped enemy mace arms own working animation rigs");
            var first = mace.Weapon;
            float maceHit = mace.DamageOf(first);
            check(maceHit > 0f && maceHit == mace.DamageOf(mace.GetComponentsInChildren<MechWeaponAnimator>().Last()),
                "Enemy mace damage comes from its weapon part's data");
            float before = health.CurrentHealth;
            check(mace.TryAttack() && mace.Weapon.CurrentPose == MechWeaponAnimator.PoseState.Fire
                && health.CurrentHealth == before, "Mace starts its windup animation without instant damage");
            check(mace.Weapon.GetComponentInChildren<AudioSource>().clip == TestMech.Sound("Mace_attack"),
                "Enemy mace windup plays the authored mace attack sound");
            Step(mace, 0.2f);
            check(health.CurrentHealth == before && mace.Weapon.NormalizedTime > 0f,
                "Mace damage waits for its configured animation release frame");
            Step(mace, 0.25f);
            check(health.CurrentHealth == before - maceHit && mace.Releases == 1,
                "Mace release damages player exactly once");
            Step(mace, 0.3f);
            check(health.CurrentHealth == before - maceHit && !mace.TryAttack() && mace.transform.position == root,
                "Mace cooldown prevents repeat hits and root motion never moves the enemy");
            Step(mace, 2f);
            check(mace.TryAttack() && mace.Weapon != first, "Dual mace enemy alternates its equipped arms");
            player.transform.position += Vector3.right * 10f; Physics.SyncTransforms();
            Step(mace, 0.8f);
            check(health.CurrentHealth == before - maceHit, "Player can dodge a mace swing during windup");
            player.transform.position -= Vector3.right * 10f; Physics.SyncTransforms(); Step(mace, 2f);
            check(mace.TryAttack(), "Mace can start another admitted swing after cooldown");
            wall = Wall(player, 1f); Step(mace, 0.8f);
            check(health.CurrentHealth == before - maceHit, "Wall appearing during windup blocks melee damage");
            Step(mace, 2f);
            check(!mace.TryAttack(), "Mace does not begin attacks through walls");
            Object.DestroyImmediate(wall); wall = null; Physics.SyncTransforms();
            check(mace.TryAttack(), "Mace attacks after obstruction is removed");
            var ally = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ally.transform.position = player.transform.position - Vector3.forward * 1f;
            var allyHealth = ally.AddComponent<CombatHealth>(); allyHealth.Team = CombatTeam.Enemy; Physics.SyncTransforms();
            Step(mace, 0.8f);
            check(health.CurrentHealth == before - 2f * maceHit && allyHealth.CurrentHealth == allyHealth.MaximumHealth,
                "Another enemy in between neither blocks the mace swing nor takes damage");
            Object.DestroyImmediate(ally); Physics.SyncTransforms(); before -= maceHit;
            Step(mace, 2f); mace.TryAttack();
            mace.GetComponent<CombatHealth>().ApplyDamage(1000f); Step(mace, 1f);
            check(health.CurrentHealth == before - maceHit && !mace.gameObject.activeSelf,
                "Killing an enemy cancels its pending animation damage");
            Object.DestroyImmediate(mace.gameObject); mace = null;
            crossbow = Enemy("crossbow", player, 8f);
            before = health.CurrentHealth;
            check(crossbow.TryAttack() && crossbow.Weapon.CurrentPose == MechWeaponAnimator.PoseState.Fire,
                "Crossbow enemy starts the correct arm Fire animation");
            check(crossbow.Weapon.GetComponentInChildren<AudioSource>().clip == TestMech.Sound("CrossbowArm_attack"),
                "Enemy crossbow windup plays the authored arm crossbow sound");
            Step(crossbow, 0.2f);
            check(!Object.FindObjectsByType<MechProjectile>().Any(FromEnemy),
                "Crossbow projectile waits for its release frame");
            Step(crossbow, 0.25f); var bolt = Bolt();
            check(bolt.IsLive && health.CurrentHealth == before, "Crossbow releases a travelling projectile rather than instant ranged damage");
            check(bolt.GetComponentInChildren<MeshFilter>().sharedMesh.name == "CrossbowBoltMesh",
                "Enemy crossbow launches the same authored bolt model as the player");
            Fly(bolt, 1f);
            check(health.CurrentHealth == before - crossbow.DamageOf(crossbow.Weapon) && !bolt.IsLive,
                "Enemy bolt uses swept collision to damage player once");
            Object.DestroyImmediate(bolt.gameObject);
            Step(crossbow, 2f); check(crossbow.TryAttack(), "Crossbow can fire again after reload cooldown");
            Step(crossbow, 0.45f); bolt = Bolt(); before = health.CurrentHealth;
            var friendly = GameObject.CreatePrimitive(PrimitiveType.Cube);
            friendly.transform.position = player.transform.position - Vector3.forward * 4f;
            friendly.transform.localScale = new Vector3(4f, 4f, 1f);
            var friendlyHealth = friendly.AddComponent<CombatHealth>(); friendlyHealth.Team = CombatTeam.Enemy; Physics.SyncTransforms();
            Fly(bolt, 1f);
            check(health.CurrentHealth == before && friendlyHealth.CurrentHealth == 100f && !bolt.IsLive,
                "Other enemies block bolts without receiving friendly damage");
            Object.DestroyImmediate(bolt.gameObject); Object.DestroyImmediate(friendly);
            Step(crossbow, 2f); crossbow.TryAttack(); Step(crossbow, 0.45f); bolt = Bolt();
            player.transform.position += Vector3.right * 5f; Physics.SyncTransforms(); Fly(bolt, 1f);
            check(health.CurrentHealth == before && bolt.IsLive, "Player can dodge a non-homing enemy crossbow bolt");
            Object.DestroyImmediate(bolt.gameObject); player.transform.position -= Vector3.right * 5f; Physics.SyncTransforms();
            Step(crossbow, 2f); crossbow.TryAttack(); Step(crossbow, 0.45f); bolt = Bolt(); wall = Wall(player, 4f); Fly(bolt, 1f);
            check(health.CurrentHealth == before && !bolt.IsLive, "World geometry stops enemy bolts before player damage");
            Object.DestroyImmediate(bolt.gameObject); Object.DestroyImmediate(wall); wall = null;
            float injured = health.CurrentHealth;
            check(player.GetComponent<MechLocomotionDebug>().TrySwap(1) && health.MaximumHealth == stats.MaxHealth
                && health.CurrentHealth == injured, "Health capacity follows Heavy legs without healing damage on swaps");
            crossbow.enabled = false;
            check(crossbow.GetComponentsInChildren<MechWeaponAnimator>().All(w => !w.Animator.enabled),
                "Disabling enemy attack stops its weapon playback");
            dual = Enemy("mace", player, 2f);
            var tuning = new SerializedObject(dual);
            tuning.FindProperty("independentWeapons").boolValue = true;
            var second = tuning.FindProperty("weapons").GetArrayElementAtIndex(1).FindPropertyRelative("damageMultiplier");
            second.floatValue *= 2f;
            tuning.ApplyModifiedPropertiesWithoutUndo();
            var arms = dual.Weapons;
            float firstHit = dual.DamageOf(arms[0]), secondHit = dual.DamageOf(arms[1]);
            check(firstHit > 0f && secondHit == 2f * firstHit, "Each weapon entry has its own damage multiplier");
            before = health.CurrentHealth;
            check(dual.TryAttack() && arms.All(w => w.CurrentPose == MechWeaponAnimator.PoseState.Fire),
                "Independent weapons start every ready weapon at once");
            Step(dual, 0.45f);
            check(health.CurrentHealth == before - firstHit - secondHit && dual.Releases == 2,
                "Each independent weapon lands its own hit with its own damage");
            check(!dual.TryAttack(), "Independent weapons each wait out their own cooldown");
            Object.DestroyImmediate(dual.gameObject); dual = null;
            far = Enemy("crossbow", player, 200f);
            var farTuning = new SerializedObject(far);
            farTuning.FindProperty("independentWeapons").boolValue = true;
            farTuning.ApplyModifiedPropertiesWithoutUndo();
            check(!far.TryAttack(), "Weapons never attack beyond their own reach");
            Object.DestroyImmediate(far.gameObject); far = null;
            health.ApplyDamage(10000f);
            check(health.IsDead && !player.activeSelf && !player.GetComponent<MechMovementController>().isActiveAndEnabled,
                "Zero player health disables movement/combat through the existing death lifecycle");
        }
        finally
        {
            foreach (var bolt in Object.FindObjectsByType<MechProjectile>().Where(FromEnemy)) Object.DestroyImmediate(bolt.gameObject);
            if (dual != null) Object.DestroyImmediate(dual.gameObject);
            if (far != null) Object.DestroyImmediate(far.gameObject);
            if (wall != null) Object.DestroyImmediate(wall);
            if (mace != null) Object.DestroyImmediate(mace.gameObject);
            if (crossbow != null) Object.DestroyImmediate(crossbow.gameObject);
            Object.DestroyImmediate(player);
        }
    }
}
