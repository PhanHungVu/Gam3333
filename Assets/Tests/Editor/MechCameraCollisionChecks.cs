using System;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechCameraCollisionChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static void Apply(CameraControlThirdPerson control) => typeof(CameraControlThirdPerson).GetMethod("ApplySettings", Private).Invoke(control, null);

    public static void Run(Action<bool, string> check)
    {
        foreach (string name in MechCameraCollisionSetup.StructureNames)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map Elements/" + name + ".prefab");
            bool covered = prefab != null;
            int meshes = 0;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) continue;
                meshes++;
                var collider = filter.GetComponent<MeshCollider>();
                covered &= collider != null && collider.enabled && !collider.isTrigger
                    && !collider.convex && collider.sharedMesh == filter.sharedMesh;
            }
            check(covered && meshes > 0, name + " prefab has solid collision matching every visible mesh");
        }

        var player = new GameObject("Camera collision player");
        player.tag = "Player";
        player.transform.position = new Vector3(80000f, 100f, 0f);
        var cameraObject = new GameObject("Camera collision view");
        cameraObject.SetActive(false);
        var view = cameraObject.AddComponent<Camera>();
        view.aspect = 2.4f;
        var control = cameraObject.AddComponent<CameraControlThirdPerson>();
        control.BindTarget(player.transform);
        Set(control, "lockCursorOnPlay", false);
        cameraObject.SetActive(true);
        var rig = Get<CinemachineCamera>(control, "cinemachineCamera");
        var follow = rig.GetComponent<CinemachineThirdPersonFollow>();
        var anchor = Get<Transform>(control, "cameraTarget");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var box = wall.GetComponent<BoxCollider>();
        var platform = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map Elements/platform.prefab"));
        try
        {
            void Configure()
            {
                Apply(control);
                follow.ShoulderOffset = Vector3.zero;
                follow.VerticalArmLength = 0f;
                follow.Damping = Vector3.zero;
                follow.CameraDistance = 10f;
            }
            Vector3 Evaluate(float dt = -1f)
            {
                Physics.SyncTransforms();
                if (dt < 0) rig.PreviousStateIsValid = false;
                rig.InternalUpdateCameraState(Vector3.up, dt);
                return rig.State.GetFinalPosition();
            }
            Configure();
            anchor.position = player.transform.position;
            anchor.rotation = Quaternion.identity;
            wall.transform.position = anchor.position + Vector3.back * 5f;
            wall.transform.localScale = new Vector3(20f, 20f, 0.15f);
            Physics.SyncTransforms();
            float safeZ = box.bounds.max.z + follow.AvoidObstacles.CameraRadius;
            Vector3 blocked = Evaluate();
            check(blocked.z >= safeZ - 0.03f && follow.CurrentObstacle == box,
                "Camera pulls in before a thin wall instead of passing through it (position " + blocked
                    + ", safe Z " + safeZ + ", obstacle " + follow.CurrentObstacle + ")");

            box.enabled = false;
            Vector3 recovering = Evaluate(1f / 60f);
            check(recovering.z < blocked.z && recovering.z > anchor.position.z - 9.9f,
                "Camera returns smoothly after an obstruction clears");
            for (int i = 0; i < 120; i++) recovering = Evaluate(1f / 60f);
            check(Vector3.Distance(recovering, anchor.position + Vector3.back * 10f) < 0.05f,
                "Clear camera regains its full follow distance");
            box.enabled = true;
            blocked = Evaluate(1f / 60f);
            check(blocked.z >= safeZ - 0.03f, "Sudden obstructions are corrected immediately, without collision damping");

            box.isTrigger = true;
            check(Vector3.Distance(Evaluate(), anchor.position + Vector3.back * 10f) < 0.05f,
                "Pickup/interaction triggers do not obstruct the camera");
            box.isTrigger = false;
            wall.tag = "Player";
            check(Vector3.Distance(Evaluate(), anchor.position + Vector3.back * 10f) < 0.05f,
                "Player-tagged colliders are excluded from camera obstruction");
            wall.tag = "Untagged";
            wall.layer = 8;
            Set(control, "obstacleLayers", (LayerMask)(1 << 8));
            Configure();
            check(Evaluate().z >= safeZ - 0.03f, "Camera respects the configured structure collision layers");
            wall.layer = 0;
            Set(control, "obstacleLayers", (LayerMask)1);

            var lens = rig.Lens;
            lens.NearClipPlane = 1f;
            rig.Lens = lens;
            Set(control, "baseFieldOfView", 90f);
            Configure();
            check(follow.AvoidObstacles.CameraRadius >= Mathf.Sqrt(1f + 1f + 2.4f * 2.4f) + 0.019f,
                "Wide-screen near-plane corners enlarge the collision radius");
            check(Evaluate().z >= box.bounds.max.z + follow.AvoidObstacles.CameraRadius - 0.03f,
                "The near-plane clearance is applied by the actual Cinemachine solver");
            box.enabled = false;
            lens = rig.Lens; lens.NearClipPlane = 0.1f; rig.Lens = lens;
            Set(control, "baseFieldOfView", 60f);
            Configure();

            platform.transform.position = player.transform.position + new Vector3(100f, 0f, 0f);
            var bounds = platform.GetComponentInChildren<Renderer>().bounds;
            foreach (var outward in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up, Vector3.down })
            {
                // Aim through the real imported platform from every side, including underneath.
                float extent = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z)));
                anchor.position = bounds.center + outward * (extent + 2f);
                anchor.rotation = Quaternion.LookRotation(outward,
                    Mathf.Abs(outward.y) > 0.9f ? Vector3.forward : Vector3.up);
                Physics.SyncTransforms();
                bool hit = Physics.Raycast(anchor.position, -outward, out var surface, 10f, 1, QueryTriggerInteraction.Ignore);
                Vector3 actual = Evaluate();
                check(hit && follow.CurrentObstacle != null && follow.CurrentObstacle.transform.IsChildOf(platform.transform)
                    && Vector3.Dot(actual - surface.point, outward) >= 0f
                    && !Physics.CheckSphere(actual, follow.AvoidObstacles.CameraRadius - 0.02f, 1, QueryTriggerInteraction.Ignore),
                    "Real platform blocks camera from direction " + outward);
            }
        }
        finally
        {
            Object.DestroyImmediate(platform);
            Object.DestroyImmediate(wall);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(player);
        }
    }
}
