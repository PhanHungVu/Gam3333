using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

// Shared damage & health rules (Scripts/Damage), checked on plain test objects with no weapons or parts.
public static class DamageChecks
{
    public static void Run(Action<bool, string> check)
    {
        var origin = new Vector3(80000f, 0f, 0f);
        var created = new List<GameObject>();
        CombatHealth Target(Vector3 position, float hp)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Damage Target";
            go.transform.position = position;
            created.Add(go);
            var health = go.AddComponent<CombatHealth>();
            health.SetMaximum(hp, true);
            return health;
        }
        void Wall(Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Damage Wall";
            go.transform.position = position;
            go.transform.localScale = scale;
            created.Add(go);
        }
        try
        {
            // Health
            var a = Target(origin + Vector3.forward * 20f, 100f);
            check(!a.ApplyDamage(-5f) && !a.ApplyDamage(0f) && !a.ApplyDamage(float.NaN) && a.CurrentHealth == 100f,
                "Invalid damage never heals or changes health");
            int deaths = 0;
            a.Died += () => deaths++;
            a.ApplyDamage(60f); a.ApplyDamage(60f); a.ApplyDamage(60f);
            check(deaths == 1 && a.IsDead && !a.gameObject.activeSelf, "Lethal damage kills once and turns the target off");
            var b = Target(origin + Vector3.forward * 30f, 100f);
            b.ApplyDamage(40f);
            b.SetMaximum(200f, false);
            check(b.CurrentHealth == 60f && b.MaximumHealth == 200f, "Raising maximum health does not heal");
            b.SetMaximum(50f, false);
            check(b.CurrentHealth == 50f, "Lowering maximum health cuts current health to fit");

            // Area damage
            var blast = new CombatBlast();
            var owner = Target(origin + Vector3.left * 1.5f, 100f);
            var compound = Target(origin + Vector3.right * 2f, 100f);
            var extra = new GameObject("Extra Collider");
            extra.transform.SetParent(compound.transform, false);
            extra.transform.localPosition = Vector3.up * 0.6f;
            extra.AddComponent<BoxCollider>();
            var plain = Target(origin + Vector3.back * 2f, 100f);
            var hidden = Target(origin + Vector3.forward * 3.5f, 100f);
            Wall(origin + Vector3.forward * 2f, new Vector3(3f, 3f, 0.2f));
            Physics.SyncTransforms();
            var area = blast.Resolve(origin, 4f, 30f, 30f, owner.transform, ~0);
            check(area.Hits == 2 && compound.CurrentHealth == 70f && plain.CurrentHealth == 70f,
                "Area damage hits each target once, even with several colliders");
            check(owner.CurrentHealth == 100f, "Area damage never hurts its owner");
            check(hidden.CurrentHealth == 100f, "Walls block area damage");

            // Melee box
            var boxOrigin = origin + Vector3.right * 40f;
            var attacker = new GameObject("Melee Owner");
            attacker.transform.position = boxOrigin;
            created.Add(attacker);
            var inside = Target(boxOrigin + Vector3.forward * 2f, 100f);
            var outside = Target(boxOrigin + Vector3.forward * 8f, 100f);
            var behind = Target(boxOrigin + new Vector3(2.2f, 0f, 2f), 100f);
            Wall(boxOrigin + new Vector3(1.2f, 0f, 2f), new Vector3(0.2f, 3f, 3f));
            Physics.SyncTransforms();
            var melee = blast.ResolveBox(boxOrigin + Vector3.forward * 2.5f, new Vector3(2.5f, 1.5f, 2.5f),
                Quaternion.identity, 25f, attacker.transform, ~0, boxOrigin);
            check(melee.Hits == 1 && inside.CurrentHealth == 75f && outside.CurrentHealth == 100f,
                "The melee box damages each target inside it once");
            check(behind.CurrentHealth == 100f, "Walls block melee damage");
            var history = new HashSet<CombatHealth>();
            var first = blast.ResolveBox(boxOrigin + Vector3.forward * 2.5f, new Vector3(2.5f, 1.5f, 2.5f),
                Quaternion.identity, 10f, attacker.transform, ~0, boxOrigin, history);
            var repeated = blast.ResolveBox(boxOrigin + Vector3.forward * 2.5f, new Vector3(2.5f, 1.5f, 2.5f),
                Quaternion.identity, 10f, attacker.transform, ~0, boxOrigin, history);
            check(first.Hits == 1 && repeated.Hits == 0 && inside.CurrentHealth == 65 && history.SetEquals(new[] { inside }),
                "A shared damage history prevents repeat sweep hits without recording targets blocked by walls");
            history.Clear();
            check(blast.ResolveBox(boxOrigin + Vector3.forward * 2.5f, new Vector3(2.5f, 1.5f, 2.5f),
                Quaternion.identity, 10f, attacker.transform, ~0, boxOrigin, history).Hits == 1 && inside.CurrentHealth == 55,
                "A fresh attack's damage history allows the same target to be hit again");
        }
        finally
        {
            foreach (var go in created) if (go != null) Object.DestroyImmediate(go);
        }
    }
}
