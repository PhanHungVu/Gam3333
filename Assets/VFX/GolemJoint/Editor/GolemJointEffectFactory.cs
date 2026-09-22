using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Authors ordinary Particle Systems; the resulting prefab needs no runtime scripts.</summary>
public static class GolemJointEffectFactory
{
    const string Folder = "Assets/VFX/GolemJoint";
    const string PrefabPath = Folder + "/Blue Golem Joint.prefab";

    [MenuItem("Tools/VFX/Create Blue Golem Joint")]
    public static void Create()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            return;
        }
        Build();
    }

    public static void Build()
    {
        var shader = Shader.Find("Golem/Joint Energy");
        if (shader == null) throw new System.InvalidOperationException("Joint Energy shader is missing.");
        var glow = Material("Blue Energy Glow", shader, false);
        var ring = Material("Blue Energy Ring", shader, true);
        var root = new GameObject("Blue Golem Joint");
        try
        {
            var core = Layer(root, "01 - Cyan Core", glow, 4f, 1f, 0.29f, 8);
            Fade(core, 0.65f);
            var size = core.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0, 0.7f), new Keyframe(0.5f, 1f), new Keyframe(1, 0.7f)));

            Ring(root, ring, "02 - Outer Segmented Ring", 0.48f, 1.15f, new Vector3(0, 0, 0));
            Ring(root, ring, "03 - Tilted Inner Ring", 0.36f, -1.6f, new Vector3(65, 25, 0));

            var motes = Layer(root, "04 - Orbiting Ions", glow, 24f, 0.85f, 0.055f, 32);
            var main = motes.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.14f;
            shape.radiusThickness = 0.3f;
            var velocity = motes.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ = 2.8f;
            velocity.radial = 0.015f;
            Fade(motes, 1f);

            var sparks = Layer(root, "05 - Connection Sparks", glow, 14f, 0.3f, 0.027f, 16);
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.65f);
            shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.09f;
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 2.4f;
            renderer.velocityScale = 0.08f;
            Fade(sparks, 0.8f);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Validate(prefab);
            Selection.activeObject = prefab;
            Debug.Log("Blue Golem Joint created and validated: " + PrefabPath);
            if (Application.isBatchMode) RenderPreview(root);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static Material Material(string name, Shader shader, bool ring)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader) { name = name };
        material.SetColor("_Tint", new Color(0.12f, 1.25f, 3.2f, 1f));
        material.SetFloat("_Ring", ring ? 1f : 0f);
        material.SetFloat("_Segments", 6f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static ParticleSystem Layer(GameObject root, string name, Material material,
        float rate, float lifetime, float size, int maximum)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        var ps = child.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = (uint)(root.transform.childCount * 173);
        var main = ps.main;
        main.duration = 2f;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.startSize = size;
        main.startColor = Color.white;
        main.maxParticles = maximum;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.enabled = false;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.maxParticleSize = 2f;
        return ps;
    }

    static void Ring(GameObject root, Material material, string name, float diameter, float speed, Vector3 angles)
    {
        var ps = Layer(root, name, material, 1f, 1.05f, diameter, 2);
        ps.transform.localRotation = Quaternion.Euler(angles);
        ps.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = speed;
        Fade(ps, 0.8f);
    }

    static void Fade(ParticleSystem ps, float opacity)
    {
        var gradient = new Gradient();
        gradient.SetKeys(new[] {
            new GradientColorKey(new Color(0.55f, 0.95f, 1f), 0f),
            new GradientColorKey(new Color(0.1f, 0.45f, 1f), 1f)
        }, new[] {
            new GradientAlphaKey(0f, 0f), new GradientAlphaKey(opacity, 0.16f),
            new GradientAlphaKey(opacity * 0.7f, 0.65f), new GradientAlphaKey(0f, 1f)
        });
        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = gradient;
    }

    static void Validate(GameObject prefab)
    {
        if (prefab == null) throw new System.InvalidOperationException("Prefab was not saved.");
        var systems = prefab.GetComponentsInChildren<ParticleSystem>();
        if (systems.Length != 5) throw new System.InvalidOperationException("Expected five particle layers.");
        foreach (var ps in systems)
        {
            var main = ps.main;
            if (!main.loop || !main.prewarm || main.simulationSpace != ParticleSystemSimulationSpace.Local)
                throw new System.InvalidOperationException("Invalid joint simulation: " + ps.name);
            var material = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            if (material == null || material.shader == null || ShaderUtil.ShaderHasError(material.shader))
                throw new System.InvalidOperationException("Invalid particle material: " + ps.name);
        }
    }

    static void RenderPreview(GameObject root)
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(1.35f, false, true);
        var cameraObject = new GameObject("Preview Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0.15f, 0.08f, -1.4f);
        camera.transform.LookAt(Vector3.zero);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.009f, 0.017f, 0.035f);
        camera.orthographic = true;
        camera.orthographicSize = 0.32f;
        camera.nearClipPlane = 0.01f;
        var target = new RenderTexture(768, 768, 24);
        var pixels = new Texture2D(768, 768, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../JointPreview.png"), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
        }
    }
}
