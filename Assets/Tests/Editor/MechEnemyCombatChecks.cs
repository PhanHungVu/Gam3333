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
        // Isolate hit/occlusion rules from the ranged AI's intentional aim spread.
        var attackSettings = new SerializedObject(enemy.GetComponent<EnemyAttack>());
        attackSettings.FindProperty("aimSpreadDegrees").floatValue = 0; attackSettings.ApplyModifiedPropertiesWithoutUndo();
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
        EnemyAttack mace = null, crossbow = null, dual = null, far = null, uphill = null, below = null;
        GameObject wall = null;
        try
        {
            // These checks advance attack time synchronously without advancing the real clock.
            // Actor hitstop has separate real-frame coverage in CombatHitFeelChecks.
            var presentation = new SerializedObject(player.GetComponent<CombatFeedback>());
            presentation.FindProperty("hitStop").boolValue = false; presentation.ApplyModifiedPropertiesWithoutUndo();
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
            check(mace.Weapon.AttackSoundsPlayed == 0,
                "Enemy mace windup does not play its attack sound");
            float maceRelease = mace.AttackDuration * new SerializedObject(mace).FindProperty("releaseFrame").floatValue;
            Step(mace, maceRelease * .5f);
            check(health.CurrentHealth == before && mace.Weapon.NormalizedTime > 0f,
                "Mace damage waits for its configured animation release frame");
            Step(mace, maceRelease * .5f + .01f);
            check(mace.Weapon.AttackSoundsPlayed == 1 && mace.Weapon.GetComponentInChildren<AudioSource>().clip == TestMech.Sound("Mace_attack"),
                "Enemy mace release plays the authored attack sound with its active hit");
            check(Mathf.Abs(health.CurrentHealth - (before - maceHit)) < 0.002f && mace.Releases == 1,
                "Mace release damages player exactly once; before=" + before + "; after=" + health.CurrentHealth
                + "; hit=" + maceHit + "; releases=" + mace.Releases + "; distance=" + Vector3.Distance(mace.transform.position, player.transform.position));
            Step(mace, 0.3f);
            check(Mathf.Abs(health.CurrentHealth - (before - maceHit)) < 0.002f && !mace.TryAttack() && mace.transform.position == root,
                "Mace cooldown prevents repeat hits and root motion never moves the enemy");
            Step(mace, 2f);
            check(mace.TryAttack() && mace.Weapon != first, "Dual mace enemy alternates its equipped arms");
            player.transform.position += Vector3.right * 10f; Physics.SyncTransforms();
            Step(mace, mace.AttackDuration + .01f);
            check(Mathf.Abs(health.CurrentHealth - (before - maceHit)) < 0.002f, "Player can dodge a mace swing during windup");
            player.transform.position -= Vector3.right * 10f; Physics.SyncTransforms(); Step(mace, 2f);
            check(mace.TryAttack(), "Mace can start another admitted swing after cooldown");
            wall = Wall(player, 1f); Step(mace, mace.AttackDuration + .01f);
            check(Mathf.Abs(health.CurrentHealth - (before - maceHit)) < 0.002f, "Wall appearing during windup blocks melee damage");
            Step(mace, 2f);
            check(!mace.TryAttack(), "Mace does not begin attacks through walls");
            Object.DestroyImmediate(wall); wall = null; Physics.SyncTransforms();
            check(mace.TryAttack(), "Mace attacks after obstruction is removed");
            var ally = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ally.transform.position = player.transform.position - Vector3.forward * 1f;
            var allyHealth = ally.AddComponent<CombatHealth>(); allyHealth.Team = CombatTeam.Enemy; Physics.SyncTransforms();
            Step(mace, mace.AttackDuration + .01f);
            check(Mathf.Abs(health.CurrentHealth - (before - 2f * maceHit)) < 0.002f && allyHealth.CurrentHealth == allyHealth.MaximumHealth,
                "Another enemy in between neither blocks the mace swing nor takes damage");
            Object.DestroyImmediate(ally); Physics.SyncTransforms(); before -= maceHit;
            Step(mace, 2f); mace.TryAttack();
            mace.GetComponent<CombatHealth>().ApplyDamage(1000f); Step(mace, 1f);
            check(Mathf.Abs(health.CurrentHealth - (before - maceHit)) < 0.002f && !mace.gameObject.activeSelf,
                "Killing an enemy cancels its pending animation damage");
            Object.DestroyImmediate(mace.gameObject); mace = null;
            crossbow = Enemy("crossbow", player, 8f);
            before = health.CurrentHealth;
            check(crossbow.TryAttack() && crossbow.Weapon.CurrentPose == MechWeaponAnimator.PoseState.Fire,
                "Fodder replacement starts its equipped cannon Fire animation");
            check(crossbow.GetComponent<EnemyFodderAnimation>() != null && crossbow.Weapon.AttackSoundsPlayed == 0,
                "Fodder cannon windup stays silent");
            float crossbowRelease = crossbow.AttackDuration * new SerializedObject(crossbow).FindProperty("releaseFrame").floatValue;
            Step(crossbow, crossbowRelease * .5f);
            check(!Object.FindObjectsByType<MechProjectile>().Any(FromEnemy),
                "Crossbow projectile waits for its release frame");
            Step(crossbow, crossbowRelease * .5f + .01f); var bolt = Bolt();
            check(crossbow.Weapon.AttackSoundsPlayed == 1 && crossbow.Weapon.GetComponentInChildren<AudioSource>().clip == crossbow.Weapon.Animations.attackSound,
                "Fodder cannon plays its authored sound when the projectile launches");
            check(bolt.IsLive && health.CurrentHealth == before, "Crossbow releases a travelling projectile rather than instant ranged damage");
            var cannon = crossbow.Weapon.GetComponent<Part>().Stats;
            check(cannon.partId == "backweaponA3" && bolt.Subtype == PartSubtype.Splash
                && bolt.GetComponentInChildren<MeshFilter>().sharedMesh.name == "Sphere",
                "Fodder fires the Zoo cannon's existing splash projectile instead of the retired enemy crossbow bolt");
            float cannonDirectHit = crossbow.DamageOf(crossbow.Weapon)
                * (1 + cannon.GetStat(StatType.SplashDamage) / cannon.GetStat(StatType.Damage));
            Fly(bolt, 1f);
            check(Mathf.Abs(health.CurrentHealth - (before - cannonDirectHit)) < 0.002f && !bolt.IsLive,
                "Fodder cannon swept impact applies its direct plus splash damage once");
            Object.DestroyImmediate(bolt.gameObject);
            check(!crossbow.TryAttack(), "Crossbow cannot restart before its weapon cooldown expires");
            Step(crossbow, crossbow.CooldownRemaining + .01f); check(crossbow.TryAttack(), "Crossbow can fire again after reload cooldown");
            Step(crossbow, crossbowRelease + .01f); bolt = Bolt(); before = health.CurrentHealth;
            var friendly = GameObject.CreatePrimitive(PrimitiveType.Cube);
            friendly.transform.position = player.transform.position - Vector3.forward * 4f;
            friendly.transform.localScale = new Vector3(4f, 4f, 1f);
            var friendlyHealth = friendly.AddComponent<CombatHealth>(); friendlyHealth.Team = CombatTeam.Enemy; Physics.SyncTransforms();
            Fly(bolt, 1f);
            check(health.CurrentHealth == before && friendlyHealth.CurrentHealth == 100f && !bolt.IsLive,
                "Other enemies block bolts without receiving friendly damage");
            Object.DestroyImmediate(bolt.gameObject); Object.DestroyImmediate(friendly);
            Step(crossbow, crossbow.CooldownRemaining + .01f); crossbow.TryAttack(); Step(crossbow, crossbowRelease + .01f); bolt = Bolt();
            var committedDirection = bolt.Direction;
            player.transform.position += Vector3.right * 5f; Physics.SyncTransforms(); Fly(bolt, 1f);
            // Faster prototype shots may exhaust their range during this full second.
            check(health.CurrentHealth == before && (bolt.Direction - committedDirection).sqrMagnitude < .000001f,
                "Player can dodge a non-homing enemy crossbow bolt");
            Object.DestroyImmediate(bolt.gameObject); player.transform.position -= Vector3.right * 5f; Physics.SyncTransforms();
            Step(crossbow, crossbow.CooldownRemaining + .01f); crossbow.TryAttack(); Step(crossbow, crossbowRelease + .01f); bolt = Bolt(); wall = Wall(player, 4f); Fly(bolt, 1f);
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
            Step(dual, dual.AttackDuration * new SerializedObject(dual).FindProperty("releaseFrame").floatValue + .01f);
            check(Mathf.Abs(health.CurrentHealth - (before - firstHit - secondHit)) < 0.002f && dual.Releases == 2,
                "Each independent weapon lands its own hit with its own damage");
            check(!dual.TryAttack(), "Independent weapons each wait out their own cooldown");
            Object.DestroyImmediate(dual.gameObject); dual = null;
            far = Enemy("crossbow", player, 200f);
            var farTuning = new SerializedObject(far);
            farTuning.FindProperty("independentWeapons").boolValue = true;
            farTuning.ApplyModifiedPropertiesWithoutUndo();
            check(!far.TryAttack(), "Weapons never attack beyond their own reach");
            Object.DestroyImmediate(far.gameObject); far = null;

            // Height: ranged enemies shoot up a slope; melee still can't reach a target well above it.
            var level = player.transform.position;
            uphill = Enemy("crossbow", player, 8f);
            below = Enemy("mace", player, 2f);
            player.transform.position = level + Vector3.up * 6f; Physics.SyncTransforms();
            check(uphill.TryAttack(), "Ranged enemies fire at a player standing well above them");
            check(!below.TryAttack(), "Melee enemies can't swing at a player well above them");
            player.transform.position = level; Physics.SyncTransforms();
            Object.DestroyImmediate(uphill.gameObject); uphill = null;
            Object.DestroyImmediate(below.gameObject); below = null;
            health.ApplyDamage(10000f);
            check(health.IsDead && !player.activeSelf && !player.GetComponent<MechMovementController>().isActiveAndEnabled,
                "Zero player health disables movement/combat through the existing death lifecycle");
        }
        finally
        {
            foreach (var bolt in Object.FindObjectsByType<MechProjectile>().Where(FromEnemy)) Object.DestroyImmediate(bolt.gameObject);
            if (dual != null) Object.DestroyImmediate(dual.gameObject);
            if (far != null) Object.DestroyImmediate(far.gameObject);
            if (uphill != null) Object.DestroyImmediate(uphill.gameObject);
            if (below != null) Object.DestroyImmediate(below.gameObject);
            if (wall != null) Object.DestroyImmediate(wall);
            if (mace != null) Object.DestroyImmediate(mace.gameObject);
            if (crossbow != null) Object.DestroyImmediate(crossbow.gameObject);
            Object.DestroyImmediate(player);
        }
    }
}
