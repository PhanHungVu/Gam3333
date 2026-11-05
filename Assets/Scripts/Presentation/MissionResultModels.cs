using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Actual registered participants only, with renderer-only copies of their current equipped models.
public sealed class MissionResultModels : MonoBehaviour
{
    GameObject stage;
    RenderTexture texture;
    public int DisplayedParticipants { get; private set; }
    public void Show(IReadOnlyList<MissionOutcomeController.Participant> participants, RawImage image)
    {
        stage = new GameObject("Result Models"); stage.transform.position = new Vector3(0,-10000,0);
        var copies = new List<CombatVisualSnapshot>();
        foreach (var participant in participants)
        {
            if (participant.Health == null) continue;
            var copy = CombatVisualSnapshot.Create(participant.Health.transform, "Participant " + participant.Id, 31, true);
            if (copy.RendererCount == 0) { Destroy(copy.Root); continue; }
            copies.Add(copy);
        }
        DisplayedParticipants = copies.Count;
        for (int i = 0; i < copies.Count; i++)
        {
            var copy = copies[i]; var root = copy.Root.transform;
            root.SetParent(stage.transform, true);
            float scale = 3 / Mathf.Max(.1f, copy.Bounds.size.y);
            var bottom = new Vector3(copy.Bounds.center.x, copy.Bounds.min.y, copy.Bounds.center.z);
            Vector3 localBottom = root.InverseTransformPoint(bottom);
            root.localScale *= scale; root.rotation = Quaternion.Euler(0,180,0);
            root.position += stage.transform.position + new Vector3((i-(copies.Count-1)*.5f)*3,0,0) - root.TransformPoint(localBottom);
        }
        var camera = new GameObject("Result Camera").AddComponent<Camera>(); camera.transform.SetParent(stage.transform, false);
        camera.fieldOfView = 35;
        camera.transform.localPosition = new Vector3(0,2.4f,-Mathf.Max(6,copies.Count*1.8f));
        camera.transform.LookAt(stage.transform.position + Vector3.up*1.6f); camera.cullingMask = 1<<31;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f,.045f,.05f); camera.nearClipPlane = .1f;
        var light = new GameObject("Result Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
        light.cullingMask = 1<<31; light.transform.SetParent(stage.transform, false); light.transform.localRotation = Quaternion.Euler(35,-25,0);
        texture = new RenderTexture(1400,480,24); camera.targetTexture = texture; image.texture = texture;
    }
    void OnDestroy()
    {
        if (stage != null) Destroy(stage);
        if (texture != null) { texture.Release(); Destroy(texture); }
    }
}
