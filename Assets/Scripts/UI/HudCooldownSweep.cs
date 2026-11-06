using UnityEngine;
using UnityEngine.UI;

// Shared icon-sized sweep. Reads runtime cooldown progress; owns no gameplay clock.
public static class HudCooldownSweep
{
    public static Image Configure(Image icon,Image line,Color color,float thickness)
    {
        if(icon==null) return line;
        if(line==null)
        {
            var existing=icon.transform.Find("Cooldown Sweep");
            if(existing!=null) line=existing.GetComponent<Image>();
            if(line==null)
            {
                var node=new GameObject("Cooldown Sweep",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                node.layer=icon.gameObject.layer; node.transform.SetParent(icon.transform,false); line=node.GetComponent<Image>();
            }
        }
        line.raycastTarget=false; line.color=color;
        var rect=line.rectTransform; rect.pivot=new Vector2(.5f,.5f);
        rect.sizeDelta=new Vector2(0,Mathf.Max(.5f,thickness));
        Refresh(icon,line,0,false,thickness); return line;
    }
    public static void Refresh(Image icon,Image line,float progress,bool cooling,float thickness)
    {
        if(line==null) return;
        line.enabled=cooling && icon!=null;
        if(icon==null) return;
        progress=Mathf.Clamp01(progress);
        var rect=line.rectTransform; rect.anchorMin=new Vector2(0,progress); rect.anchorMax=new Vector2(1,progress);
        float inset=Mathf.Min(Mathf.Max(.5f,thickness)*.5f,icon.rectTransform.rect.height*.5f);
        rect.anchoredPosition=new Vector2(0,Mathf.Lerp(inset,-inset,progress));
    }
}
