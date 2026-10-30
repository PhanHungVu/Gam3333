using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;

// Version-pinned editor adapter for Unity Behavior 1.0.16's internal authoring API.
public sealed class EnemyGraphAuthoring
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    readonly ScriptableObject asset;
    public object Start { get; }
    public static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object Get(object owner, string name) => owner.GetType().GetProperty(name, Flags)?.GetValue(owner) ?? owner.GetType().GetField(name, Flags)?.GetValue(owner);
    static void Set(object owner, string name, object value)
    { var p = owner.GetType().GetProperty(name, Flags); if (p != null) p.SetValue(owner, value); else owner.GetType().GetField(name, Flags).SetValue(owner, value); }
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethods(Flags)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length && !m.ContainsGenericParameters).Invoke(owner, args);
    public static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); Folder(path.Substring(0, split)); AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
    EnemyGraphAuthoring(string path, bool repeat)
    {
        Folder(path.Substring(0, path.LastIndexOf('/')));
        asset = ScriptableObject.CreateInstance(Find("Unity.Behavior.BehaviorAuthoringGraph"));
        asset.name = System.IO.Path.GetFileNameWithoutExtension(path); AssetDatabase.CreateAsset(asset, path);
        Call(asset, "EnsureAssetHasBlackboard");
        Start = ((IList)Get(asset, "Nodes")).Cast<object>().FirstOrDefault(n => n.GetType().Name == "StartNodeModel") ?? Builtin("Start", 0, 0);
        Set(Start, "Repeat", repeat);
    }
    public object Node(Type runtime, float x, float y)
    {
        var registry = Find("Unity.Behavior.NodeRegistry");
        var info = registry.GetMethod("GetInfo", Flags, null, new[] { typeof(Type) }, null).Invoke(null, new object[] { runtime });
        if (info == null) throw new InvalidOperationException("Missing node registration: " + runtime.FullName);
        var modelType = (Type)Get(Get(info, "ModelType"), "Type");
        return Call(asset, "CreateNode", modelType, new Vector2(x, y), null, new[] { info });
    }
    public object Builtin(string name, float x, float y) => Node(Find("Unity.Behavior." + name), x, y);
    public object Subgraph(BehaviorGraph graph, float x, float y)
    {
        var node = Builtin("RunSubgraph", x, y);
        var inline = Activator.CreateInstance(Find("Unity.Behavior.GraphFramework.TypedVariableModel`1").MakeGenericType(typeof(BehaviorGraph)));
        Set(inline, "ObjectValue", graph);
        Call(node, "SetField", "Subgraph", inline, typeof(BehaviorGraph));
        return node;
    }
    public void Link(object from, object to)
    {
        object Port(object node, string property) => ((IEnumerable)Get(node, property)).Cast<object>().First();
        Call(asset, "ConnectEdge", Port(from, "OutputPortModels"), Port(to, "InputPortModels"));
    }
    public static BehaviorGraph Ensure(string path, bool repeat, Action<EnemyGraphAuthoring> build)
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BehaviorGraphAgent).Assembly);
        if (package == null || package.version != "1.0.16")
            throw new InvalidOperationException("Enemy graph authoring is verified with com.unity.behavior 1.0.16. Review its editor API before regenerating graphs with another version.");
        var existing = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
        if (existing == null)
        {
            var authoring = new EnemyGraphAuthoring(path, repeat); build(authoring); existing = authoring.asset;
            Call(existing, "SetAssetDirty", true); Call(existing, "ValidateAsset");
        }
        var runtime = (BehaviorGraph)Call(existing, "BuildRuntimeGraph", true);
        if (runtime == null) throw new InvalidOperationException("Graph build failed: " + path);
        EditorUtility.SetDirty(existing); AssetDatabase.SaveAssets(); return runtime;
    }
}
