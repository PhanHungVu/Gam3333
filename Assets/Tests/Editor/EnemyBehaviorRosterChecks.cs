using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EnemyBehaviorChecks
{
    static EnemyGraphController SpawnRole(int role, float distance)
    {
        var position = player.transform.position + Vector3.forward * distance; position.y = 1;
        var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[role]), position, Quaternion.LookRotation(Vector3.back));
        var drop = actor.GetComponent<EnemyPartDrop>(); if (drop != null) drop.enabled = false;
        subject = actor.GetComponent<EnemyGraphController>(); return subject;
    }
    static void ResetPlayer()
    { player.transform.position = new Vector3(0, 1.6f, 0); health.SetMaximum(10000, true); Physics.SyncTransforms(); field.Rebuild(); }
    static void ClearShots() { foreach (var shot in Object.FindObjectsByType<MechProjectile>()) Object.Destroy(shot.gameObject); }
    static IEnumerator RosterCases()
    {
        for (int role = 0; role < EnemyRosterSetup.Roster.Length; role++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[role]);
            Check(prefab.GetComponent<EnemyGraphController>().Role == (EnemyBehaviorRole)role
                && EnemySpawnCost.Of(prefab) == (role < 4 ? 1 : role < 6 ? 2 : 10), "Roster identity and spawn cost: " + (EnemyBehaviorRole)role);
        }
        for (int role = 1; role < 4; role++)
        {
            ResetPlayer(); var actor = SpawnRole(role, 2.2f); yield return Until(() => actor.Attack.IsAttacking, 4, "Variant did not wind up");
            float before = health.CurrentHealth; var weapon = actor.Attack.Weapon;
            yield return Frames(10); Check(health.CurrentHealth == before, actor.Role + " visibly winds up before applying damage");
            yield return Until(() => actor.Attack.Releases == 1, 3, "Variant never released");
            Check(Mathf.Abs(health.CurrentHealth - (before - actor.Attack.DamageOf(weapon))) < .002f, actor.Role + " releases its weapon-based contact damage once");
            if (role == 2) Check(actor.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Hammer head proxy") == 2, "Hammer proxy is present once per weapon after repeated setup");
            if (role == 3) Check(Object.FindObjectsByType<MechProjectile>().Length == 0, "Type 1 throwing axe shares the overhead contact behavior without firing a projectile");
            Capture("variant-" + actor.Role, actor); yield return Remove(actor);
        }
        ResetPlayer(); var ranged = SpawnRole(5, 8);
        yield return Until(() => ranged.Attack.IsAttacking, 4, "Crossbow did not aim"); float hp = health.CurrentHealth;
        Check(new SerializedObject(ranged.Attack).FindProperty("aimSpreadDegrees").floatValue >= 10, "Crossbow intentionally uses a broad aim cone");
        yield return Frames(12); Check(hp == health.CurrentHealth && ranged.Attack.IsTelegraphing, "Crossbow aims visibly before releasing a fast shot");
        Capture("crossbow-aim", ranged);
        yield return Until(() => ranged.Attack.Releases == 1, 2, "Crossbow did not fire");
        Check(Object.FindObjectsByType<MechProjectile>().Any(p => p.IsLive), "Crossbow uses the existing travelling projectile executor");
        player.transform.position = ranged.transform.position + ranged.transform.forward * 2 + Vector3.up * .6f; Physics.SyncTransforms();
        yield return Until(() => ranged.CurrentAction == "Reposition", 5, "Ranged enemy did not back away"); float close = ranged.Agent.DistanceToTarget;
        yield return Frames(60); Check(ranged.Agent.DistanceToTarget > close + 1, "Ranged enemy creates firing distance when the player closes in");
        yield return Remove(ranged); ClearShots(); yield return Frames(2);

        ResetPlayer(); var heavy = SpawnRole(6, 18);
        Check(heavy.Attack.Weapons.All(w => w.GetComponent<Part>().partType == PartType.Shoulder), "Heavy elite reuses its existing shoulder weapon PartData");
        yield return Until(() => heavy.Attack.IsAttacking, 4, "Heavy enemy did not aim"); hp = health.CurrentHealth;
        yield return Frames(30); Check(health.CurrentHealth == hp && heavy.Attack.IsTelegraphing && heavy.Attack.DamageOf(heavy.Attack.Weapon) >= 30,
            "Heavy ranged attack has a long readable windup and high damage"); Capture("heavy-windup", heavy);
        yield return Until(() => heavy.Attack.Releases == 1, 3, "Heavy attack did not fire");
        yield return Frames(60); Check(heavy.Attack.Releases == 1 && heavy.Attack.CooldownRemaining > 0, "Heavy elite observes its recovery instead of firing continuously");
        yield return Remove(heavy); ClearShots(); yield return Frames(2);

        ResetPlayer(); var spinner = SpawnRole(4, 8); var charge = spinner.GetComponent<EnemySpinnerAttack>();
        yield return Until(() => charge.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Spinner did not wind up"); hp = health.CurrentHealth;
        yield return Frames(25); Check(health.CurrentHealth == hp && charge.CurrentStage == EnemyGraphAttack.Stage.Windup, "Spinner commits a visible spinning windup without early damage");
        Capture("spinner-windup", spinner);
        yield return Until(() => charge.Contacts > 0, 4, "Spinner charge did not contact player");
        Check(health.CurrentHealth == hp - charge.Damage && charge.Contacts == 1, "Spinner charge damages the player once through shared damage rules");
        yield return Frames(45); Check(charge.Contacts == 1 && charge.CooldownRemaining > 0, "Spinner cannot repeatedly damage an overlapping player during recovery");
        yield return Remove(spinner);
        ResetPlayer(); spinner = SpawnRole(4, 8); charge = spinner.GetComponent<EnemySpinnerAttack>();
        yield return Until(() => charge.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Spinner dodge windup missing"); hp = health.CurrentHealth;
        player.transform.position += Vector3.right * 7; Physics.SyncTransforms();
        yield return Until(() => charge.CurrentStage == EnemyGraphAttack.Stage.Attack, 3, "Spinner charge did not start"); Capture("spinner-charge", spinner);
        yield return Until(() => charge.CurrentStage == EnemyGraphAttack.Stage.Recovery, 3, "Spinner charge did not finish");
        Check(health.CurrentHealth == hp && charge.Contacts == 0, "Player can dodge the committed spinner direction"); yield return Remove(spinner);
        ResetPlayer(); spinner = SpawnRole(4, 8); charge = spinner.GetComponent<EnemySpinnerAttack>();
        yield return Until(() => charge.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Spinner wall windup missing");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 3, 4); wall.transform.localScale = new Vector3(8, 6, 1); Physics.SyncTransforms();
        yield return Until(() => charge.CurrentStage == EnemyGraphAttack.Stage.Recovery, 4, "Wall charge never recovered");
        Check(spinner.transform.position.z > 4.7f && charge.Contacts == 0, "Existing collision motor stops the charge at a wall");
        yield return Remove(spinner); Object.Destroy(wall); yield return Frames(2);

        ResetPlayer(); var rope = SpawnRole(7, 18); var thrower = rope.GetComponent<EnemyRopeAttack>();
        yield return Until(() => thrower.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Rock throw did not wind up"); hp = health.CurrentHealth;
        yield return Frames(30); Check(thrower.Impacts == 0 && health.CurrentHealth == hp, "Rock elite telegraphs the landing area before damage"); Capture("rope-windup", rope);
        yield return Until(() => thrower.CurrentStage == EnemyGraphAttack.Stage.Attack, 3, "Rock was not thrown");
        yield return Frames(18); Capture("rope-flight", rope);
        yield return Until(() => thrower.Impacts == 1, 3, "Rock did not land");
        Check(health.CurrentHealth == hp - thrower.Damage, "Rock landing applies one existing-rule area hit");
        var landed = thrower.LandedPosition; float startDistance = Steering.Flat(rope.transform.position - landed).magnitude;
        player.transform.position += Vector3.right * 8; Physics.SyncTransforms();
        yield return Until(() => thrower.CurrentStage == EnemyGraphAttack.Stage.Pull, 2, "Rope did not pull after landing");
        yield return Frames(45); Check(Steering.Flat(rope.transform.position - landed).magnitude < startDistance - 4
            && Vector3.Distance(thrower.LandedPosition, landed) < .01f, "Elite pulls toward the landed rock instead of following the relocated player");
        Capture("rope-pull", rope);
        yield return Until(() => thrower.CurrentStage == EnemyGraphAttack.Stage.Recovery, 4, "Rope did not recover");
        Check(Steering.Flat(rope.transform.position-landed).magnitude < 3 && thrower.Impacts == 1, "Pull ends near the rock without another landing hit");
        yield return Remove(rope);
        ResetPlayer(); rope = SpawnRole(7, 18); thrower = rope.GetComponent<EnemyRopeAttack>();
        yield return Until(() => thrower.CurrentStage == EnemyGraphAttack.Stage.Attack, 4, "Rope cancellation throw missing"); hp = health.CurrentHealth;
        rope.Graph.enabled = false; yield return Frames(120);
        Check(thrower.Impacts == 0 && health.CurrentHealth == hp && !thrower.IsExecuting, "Stopping the rope graph cancels airborne damage and cleans up its presentation");
        yield return Remove(rope);
        yield return WaveCases();
        yield return MixedCases();
    }
    static IEnumerator MixedCases()
    {
        ResetPlayer(); var actors = new EnemyGraphController[8];
        for (int i = 0; i < actors.Length; i++)
        {
            actors[i] = SpawnRole(i, 12);
            float angle = i * Mathf.PI * 2 / actors.Length;
            actors[i].transform.position = player.transform.position + new Vector3(Mathf.Cos(angle)*14, -.6f, Mathf.Sin(angle)*14);
            actors[i].transform.rotation = Quaternion.LookRotation(Steering.Flat(player.transform.position-actors[i].transform.position));
        }
        Physics.SyncTransforms(); int frames = Time.frameCount; double began = EditorApplication.timeSinceStartup;
        yield return Frames(1200);
        Check(actors.All(a => a != null && a.IsReady && a.Graph.Graph.IsRunning), "Mixed roster keeps all eight graph instances running for twenty combat seconds");
        Check(actors.Where(a => a.SpecialAttack == null).Sum(a => a.Attack.Releases) >= 10,
            "Melee and ranged actors continue releasing attacks in a mixed encounter");
        Check(actors[7].GetComponent<EnemyRopeAttack>().Impacts > 0 && health.CurrentHealth < health.MaximumHealth,
            "Elite throw/pull and existing damage remain functional among the small enemies");
        Check(actors.All(a => float.IsFinite(a.transform.position.x) && float.IsFinite(a.transform.position.y) && float.IsFinite(a.transform.position.z)),
            "Mixed attack movement produces finite grounded actor positions");
        Debug.Log("ENEMY MIXED SAMPLE: " + (Time.frameCount-frames) + " frames in " + (EditorApplication.timeSinceStartup-began).ToString("0.00") + " editor seconds (not a standalone performance benchmark)");
        Capture("mixed-roster", actors[7]);
        foreach (var actor in actors) Object.Destroy(actor.gameObject);
        ClearShots(); yield return Frames(2);
    }
    static IEnumerator WaveCases()
    {
        ResetPlayer();
        foreach (bool survival in new[] { false, true })
        {
            var data = AssetDatabase.LoadAssetAtPath<WaveData>(survival ? EnemyRosterSetup.Survival : EnemyRosterSetup.Roaming);
            Check(data.spawnInterval == (survival ? .2f : .5f) && data.timerReset == (survival ? 30 : 120)
                && data.resourceTotal == (survival ? 100 : 50), "Requested " + (survival ? "survival" : "roaming") + " clock/resource settings are saved");
            float small = data.enemies.Where(e => (int)e.prefab.GetComponent<EnemyGraphController>().Role < 6).Sum(e => e.weight);
            float elite = data.enemies.Sum(e => e.weight) - small;
            Check(Mathf.Abs(small - (survival ? 70 : 90)) < .01f && Mathf.Abs(elite - (survival ? 30 : 10)) < .01f, "Small/elite aggregate probabilities match the requested preset");
            var directorObject = new GameObject("Wave integration fixture"); var director = directorObject.AddComponent<SpawnDirector>(); director.enabled = false;
            var ring = new GameObject("Existing spawn ring").AddComponent<RoamingSpawner>();
            Check(director.RunWave(data, new SpawnPoint[] { ring }, director), "Existing SpawnDirector accepts the completed roster preset");
            int spent = 0, attempts = 0;
            while (director.ResourceLeft > 0 && attempts++ < 150)
            {
                var before = EnemyAgent.All.ToArray(); director.Tick(data.spawnInterval);
                var spawned = EnemyAgent.All.Where(e => !before.Contains(e)).ToArray();
                foreach (var enemy in spawned)
                {
                    spent += enemy.GetComponent<EnemySpawnCost>().Cost;
                    var drops = enemy.GetComponent<EnemyPartDrop>(); if (drops != null) drops.enabled = false;
                    Object.DestroyImmediate(enemy.gameObject);
                }
            }
            Check(director.ResourceLeft == 0 && spent == data.resourceTotal, "Real spawns consume exactly the wave budget, including affordable end-of-wave choices");
            director.Tick(data.spawnInterval); director.Tick(data.timerReset - .01f);
            Check(director.ResourceLeft == 0, "Spent wave waits until its reset timer completes");
            director.Tick(.02f); Check(director.ResourceLeft > 0, "Wave refills and resumes using its original cycle machinery");
            foreach (var enemy in EnemyAgent.All.ToArray()) { var drops = enemy.GetComponent<EnemyPartDrop>(); if (drops != null) drops.enabled = false; Object.DestroyImmediate(enemy.gameObject); }
            Object.Destroy(directorObject); Object.Destroy(ring.gameObject); yield return Frames(2);
        }
        var capped = Object.Instantiate(AssetDatabase.LoadAssetAtPath<WaveData>(EnemyRosterSetup.Survival)); capped.maxConcurrentEnemies = 3;
        var limitedObject = new GameObject("Limit fixture"); var limited = limitedObject.AddComponent<SpawnDirector>(); limited.enabled = false;
        var point = new GameObject("Limit ring").AddComponent<RoamingSpawner>(); limited.RunWave(capped, new SpawnPoint[] { point }, limited);
        for (int i = 0; i < 6; i++) limited.Tick(1);
        int remaining = limited.ResourceLeft; Check(EnemyAgent.All.Count == 3, "Optional alive budget bounds the active wave population");
        limited.Tick(10); Check(limited.ResourceLeft == remaining, "Population limit waits without spending resource");
        var first = EnemyAgent.All[0]; var dropper = first.GetComponent<EnemyPartDrop>(); if (dropper != null) dropper.enabled = false; Object.DestroyImmediate(first.gameObject);
        limited.Tick(1); Check(EnemyAgent.All.Count == 3 && limited.ResourceLeft < remaining, "A freed slot admits the next enemy without resetting the wave");
        foreach (var enemy in EnemyAgent.All.ToArray()) { var drops = enemy.GetComponent<EnemyPartDrop>(); if (drops != null) drops.enabled = false; Object.DestroyImmediate(enemy.gameObject); }
        Object.Destroy(limitedObject); Object.Destroy(point.gameObject); Object.Destroy(capped); yield return Frames(2);
    }
}
