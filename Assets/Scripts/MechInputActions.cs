using UnityEngine;
using UnityEngine.InputSystem;

// The imported asset is also Unity's project-wide/UI input asset. Gameplay must
// not enable/disable its shared live state, or retain it across asset reimports.
public static class MechInputActions
{
    public static InputActionAsset Create(InputActionAsset source)
    {
        if (source == null) return null;
        var instance = Object.Instantiate(source);
        instance.name = source.name + " (Gameplay Instance)";
        instance.hideFlags = HideFlags.DontSave;
        return instance;
    }

    public static void Release(ref InputActionAsset instance)
    {
        if (instance == null) return;
        instance.Disable();
        // Maps share native state. Dispose clears it on all maps and the asset;
        // subsequent calls are harmless and do not wait for end-of-frame Destroy.
        foreach (var map in instance.actionMaps) map.Dispose();
        if (Application.isPlaying) Object.Destroy(instance);
        else Object.DestroyImmediate(instance);
        instance = null;
    }
}
