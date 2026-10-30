using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Explicit UI-only setup; no scenes, input assets, part assets or gameplay tuning are rewritten.
public static class PartSwapWorldUiSetup
{
    public const string ViewPath="Assets/Prefabs/UI/PartSwapWorldUI.prefab";
    public const string PlayerPath="Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab";
    [MenuItem("Tools/Mech/UI/Set Up World Part Swap UI")]
    public static void UpgradeAssets()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before setting up UI.");
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath);
        var prefab=asset!=null ? asset.GetComponent<PartSwapWorldView>() : null;
        if(prefab==null) prefab=CreateView();
        UpgradeCompactLayout();
        UpgradeOverlayRendering();
        prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath).GetComponent<PartSwapWorldView>();
        var player=PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var presenter=player.GetComponent<PartSwapWorldHudPresenter>();
            if(presenter==null) presenter=player.AddComponent<PartSwapWorldHudPresenter>();
            var serialized=new SerializedObject(presenter);
            serialized.FindProperty("viewPrefab").objectReferenceValue=prefab; serialized.ApplyModifiedPropertiesWithoutUndo();
            var detector=player.GetComponentInChildren<PickupDetector>(true);
            if(detector==null) throw new InvalidOperationException("The shared player has no existing pickup detector.");
            detector.worldSpaceHud=presenter;
            PrefabUtility.RecordPrefabInstancePropertyModifications(detector);
            PrefabUtility.SaveAsPrefabAsset(player,PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.SaveAssets(); Debug.Log("WORLD PART SWAP UI READY: existing selected part, sockets and bindings reused");
    }
    static void UpgradeOverlayRendering()
    {
        const string folder="Assets/Materials/UI";
        const string materialPath=folder+"/Part Swap Overlay.mat";
        if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Materials","UI");
        var shader=Shader.Find("Mech/UI/Part Swap Overlay");
        var textShader=Shader.Find("TextMeshPro/Distance Field Overlay");
        if(shader==null || textShader==null) throw new InvalidOperationException("Part swap overlay shaders are missing.");
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(material==null)
        {
            material=new Material(shader) { name="Part Swap Overlay" };
            AssetDatabase.CreateAsset(material,materialPath);
        }
        var root=PrefabUtility.LoadPrefabContents(ViewPath);
        try
        {
            var fields=new SerializedObject(root.GetComponent<PartSwapWorldView>());
            Set(fields,"panelOverlayMaterial",material); Set(fields,"textOverlayShader",textShader);
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,ViewPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static void UpgradeCompactLayout()
    {
        var root=PrefabUtility.LoadPrefabContents(ViewPath);
        try
        {
            var fields=new SerializedObject(root.GetComponent<PartSwapWorldView>());
            if(fields.FindProperty("compactLayoutVersion").intValue>=2) return;
            var upper=(RectTransform)fields.FindProperty("newPartCard").objectReferenceValue;
            upper.Find("New").GetComponent<TMP_Text>().text="NEW";
            Style((TMP_Text)fields.FindProperty("partName").objectReferenceValue,14,34,310,36,28);
            Style((TMP_Text)fields.FindProperty("partType").objectReferenceValue,14,74,310,24,20);
            var unused=(TMP_Text)fields.FindProperty("statsLeft").objectReferenceValue;
            unused.text=""; unused.gameObject.SetActive(false);
            var basic=(TMP_Text)fields.FindProperty("statsRight").objectReferenceValue;
            Style(basic,338,12,308,104,21); basic.richText=false; basic.text=""; basic.gameObject.SetActive(true);
            upper.sizeDelta=new Vector2(660,126);
            var cards=fields.FindProperty("cards");
            for(int i=0;i<cards.arraySize;i++)
            {
                var card=cards.GetArrayElementAtIndex(i);
                Style((TMP_Text)card.FindPropertyRelative("binding").objectReferenceValue,12,10,300,30,26);
                var slot=(TMP_Text)card.FindPropertyRelative("slot").objectReferenceValue; slot.text=""; slot.gameObject.SetActive(false);
                Style((TMP_Text)card.FindPropertyRelative("current").objectReferenceValue,12,44,300,26,21);
                var comparison=(TMP_Text)card.FindPropertyRelative("comparison").objectReferenceValue;
                Style(comparison,12,76,300,112,22); comparison.richText=true;
            }
            fields.FindProperty("compactLayoutVersion").intValue=2; fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,ViewPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static void Style(TMP_Text label,float x,float y,float width,float height,float size)
    {
        label.rectTransform.anchoredPosition=new Vector2(x,-y); label.rectTransform.sizeDelta=new Vector2(width,height);
        label.fontSize=size; label.enableAutoSizing=true; label.fontSizeMin=size-2; label.fontSizeMax=size;
    }
    static PartSwapWorldView CreateView()
    {
        var root=new GameObject("Part Swap World UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(PartSwapWorldView));
        try
        {
            root.layer=5;
            var rect=root.GetComponent<RectTransform>(); rect.pivot=new Vector2(0.5f,0); rect.sizeDelta=new Vector2(660,460);
            var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; canvas.sortingOrder=12;
            canvas.overrideSorting=true; canvas.additionalShaderChannels=AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.Normal|AdditionalCanvasShaderChannels.Tangent;
            var group=root.GetComponent<CanvasGroup>(); group.alpha=0; group.blocksRaycasts=false; group.interactable=false;
            var view=root.GetComponent<PartSwapWorldView>(); var fields=new SerializedObject(view);
            Set(fields,"canvas",canvas); Set(fields,"group",group); Set(fields,"panel",rect);
            var upper=Panel("New Part",rect,660,204,new Color(0.055f,0.075f,0.08f,0.97f));
            Set(fields,"newPartCard",upper);
            var accent=Panel("Type Accent",upper,5,204,new Color(0.15f,0.8f,0.82f,1)).GetComponent<Image>();
            var accentRect=accent.rectTransform; accentRect.anchorMin=new Vector2(0,0); accentRect.anchorMax=new Vector2(0,1); accentRect.sizeDelta=new Vector2(5,0);
            Set(fields,"accent",accent);
            Label("New",upper,"NEW PART",14,10,632,20,17,FontStyles.Bold);
            Set(fields,"partName",Label("Part Name",upper,"",14,34,632,34,29,FontStyles.Bold));
            Set(fields,"partType",Label("Part Type",upper,"",14,69,632,22,19));
            Set(fields,"statsLeft",Label("Stats Left",upper,"",14,98,310,156,22));
            Set(fields,"statsRight",Label("Stats Right",upper,"",338,98,308,156,22));
            var cards=fields.FindProperty("cards"); cards.arraySize=7;
            for(int i=0;i<7;i++)
            {
                var card=Panel("Socket "+(i+1),rect,324,230,new Color(0.16f,0.20f,0.21f,0.98f)); card.gameObject.SetActive(false);
                var entry=cards.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("root").objectReferenceValue=card;
                entry.FindPropertyRelative("background").objectReferenceValue=card.GetComponent<Image>();
                entry.FindPropertyRelative("binding").objectReferenceValue=Label("Binding",card,"",12,10,300,24,19,FontStyles.Bold);
                entry.FindPropertyRelative("slot").objectReferenceValue=Label("Socket Name",card,"",12,36,300,26,23,FontStyles.Bold);
                entry.FindPropertyRelative("current").objectReferenceValue=Label("Equipped Part",card,"",12,65,300,24,21);
                var comparison=Label("Comparison",card,"",12,98,300,290,19);
                comparison.enableAutoSizing=true; comparison.fontSizeMin=16; comparison.fontSizeMax=19;
                entry.FindPropertyRelative("comparison").objectReferenceValue=comparison;
            }
            Set(fields,"footer",Label("Footer",rect,"",14,430,632,26,18));
            fields.ApplyModifiedPropertiesWithoutUndo();
            foreach(string child in new[]{"Part Name","Stats Left","Stats Right"})
            {
                var label=upper.Find(child).GetComponent<TMP_Text>(); label.enableAutoSizing=true;
                label.fontSizeMax=label.fontSize; label.fontSizeMin=child=="Part Name" ? 22 : 18;
            }
            foreach(var graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget=false;
            var asset=PrefabUtility.SaveAsPrefabAsset(root,ViewPath); return asset.GetComponent<PartSwapWorldView>();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static void Set(SerializedObject fields,string name,UnityEngine.Object value)=>fields.FindProperty(name).objectReferenceValue=value;
    static RectTransform Panel(string name,Transform parent,float width,float height,Color color)
    {
        var node=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image)); node.layer=5; node.transform.SetParent(parent,false);
        var rect=node.GetComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.sizeDelta=new Vector2(width,height);
        var image=node.GetComponent<Image>(); image.color=color; image.raycastTarget=false; return rect;
    }
    static TMP_Text Label(string name,Transform parent,string text,float x,float y,float width,float height,float size,FontStyles style=FontStyles.Normal)
    {
        var node=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI)); node.layer=5; node.transform.SetParent(parent,false);
        var label=node.GetComponent<TextMeshProUGUI>();
        var rect=label.rectTransform; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(width,height);
        label.font=TMP_Settings.defaultFontAsset; label.text=text; label.fontSize=size; label.fontStyle=style;
        label.color=new Color(0.94f,0.93f,0.86f); label.alignment=TextAlignmentOptions.TopLeft;
        label.textWrappingMode=TextWrappingModes.NoWrap; label.overflowMode=TextOverflowModes.Ellipsis; label.raycastTarget=false;
        label.richText=false;
        return label;
    }
}
