using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pooled world-space view. No raycaster, buttons, device reads or equipment calls.
public sealed class PartSwapWorldView : MonoBehaviour
{
    [Serializable] public sealed class SocketCard
    {
        public RectTransform root;
        public Image background;
        public TMP_Text binding,slot,current,comparison;
        [NonSerialized] public PartCategory category;
        [NonSerialized] public bool available;
    }
    [SerializeField] Canvas canvas;
    [SerializeField] CanvasGroup group;
    [SerializeField] RectTransform panel,newPartCard;
    [SerializeField] TMP_Text partName,partType,statsLeft,statsRight,footer;
    [SerializeField] Image accent;
    [SerializeField] SocketCard[] cards;
    [Header("Render over scene geometry")]
    [SerializeField] Material panelOverlayMaterial;
    [SerializeField] Shader textOverlayShader;
    [SerializeField,HideInInspector] int compactLayoutVersion;
    readonly List<PartCategory> slots=new List<PartCategory>(7);
    readonly Dictionary<Material,Material> overlayFonts=new Dictionary<Material,Material>();
    static readonly Color cardColor=new Color(0.16f,0.20f,0.21f,0.98f);
    Color accentColor;
    Image statusBacking;
    public RectTransform Panel=>panel;
    public Canvas Canvas=>canvas;
    public CanvasGroup Group=>group;
    public IReadOnlyList<PartCategory> DisplayedSlots=>slots;
    public float Height=>panel.rect.height;
    public string DisplayedName=>partName.text;
    public SocketCard Card(int index)=>cards[index];
    void Awake()
    {
        // The status sits below the comparison cards; give it the same readable backing over bright scenery.
        statusBacking=new GameObject("Pickup Status Backing",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image)).GetComponent<Image>();
        statusBacking.transform.SetParent(footer.transform.parent,false);
        statusBacking.transform.SetSiblingIndex(footer.transform.GetSiblingIndex());
        statusBacking.raycastTarget=false; statusBacking.color=new Color(.10f,.13f,.14f,.94f);
        var statusRect=statusBacking.rectTransform;
        statusRect.anchorMin=footer.rectTransform.anchorMin; statusRect.anchorMax=footer.rectTransform.anchorMax; statusRect.pivot=footer.rectTransform.pivot;
        foreach(var graphic in GetComponentsInChildren<Graphic>(true))
        {
            if(graphic is TMP_Text label)
            {
                var source=label.fontSharedMaterial;
                if(source==null || textOverlayShader==null) continue;
                if(!overlayFonts.TryGetValue(source,out var overlay))
                {
                    // Preserve this font's atlas/style without changing shared
                    // font assets or the materials used by other game HUDs.
                    overlay=new Material(source) { name=source.name+" (Part Swap Overlay)",
                        shader=textOverlayShader,renderQueue=4000,hideFlags=HideFlags.DontSave };
                    overlayFonts.Add(source,overlay);
                }
                label.fontSharedMaterial=overlay;
            }
            else if(panelOverlayMaterial!=null) graphic.material=panelOverlayMaterial;
        }
    }
    void OnDestroy()
    {
        foreach(var material in overlayFonts.Values) if(material!=null) Destroy(material);
        overlayFonts.Clear();
    }
    public void Populate(Part part,EquipManager owner,MechPlayerInput input,PickupDetector detector)
    {
        partName.text=PartSwapDisplay.Name(part);
        partType.text=part.Stats==null ? "" : part.partType.ToString();
        statsLeft.text=""; statsLeft.gameObject.SetActive(false);
        int basicLines=PartSwapDisplay.BasicStats(part,out string basicStats);
        statsRight.text=basicStats; statsRight.gameObject.SetActive(basicLines>0);
        float upperHeight=Mathf.Max(104,22+basicLines*26);
        statsRight.rectTransform.sizeDelta=new Vector2(308,Mathf.Max(26,basicLines*26));
        newPartCard.sizeDelta=new Vector2(660,upperHeight);
        PartSwapDisplay.CompatibleSlots(owner,part,slots);
        accentColor=detector!=null ? detector.HighlightColour(part.partType) : new Color(0.8f,0.73f,0.5f);
        accent.color=accentColor;
        float y=upperHeight+8;
        for(int i=0;i<cards.Length;i++) cards[i].root.gameObject.SetActive(i<slots.Count);
        for(int i=0;i<slots.Count;i+=2)
        {
            int lines=0;
            for(int j=i;j<Mathf.Min(i+2,slots.Count);j++)
            {
                var card=cards[j]; card.category=slots[j];
                string key=input!=null ? input.SwapBindingLabel(slots[j]) : null;
                card.available=!string.IsNullOrEmpty(key) && (detector==null || detector.IsNearby(part));
                card.binding.text=string.IsNullOrEmpty(key) ? "Unavailable" : "["+key+"] "+PartSwapDisplay.SocketName(slots[j])+(card.available ? "" : " (nearby only)");
                card.slot.text=""; card.slot.gameObject.SetActive(false);
                var current=owner.GetEquipped(slots[j]); card.current.text=PartSwapDisplay.Name(current);
                lines=Mathf.Max(lines,PartSwapDisplay.Comparison(current,part,out string comparison)); card.comparison.text=comparison;
            }
            float height=86+Mathf.Max(1,lines)*28;
            for(int j=i;j<Mathf.Min(i+2,slots.Count);j++)
            {
                var card=cards[j]; float width=slots.Count==1 ? 660 : 324;
                card.root.anchoredPosition=new Vector2((j%2)*(324+12),-y);
                card.root.sizeDelta=new Vector2(width,height); card.background.color=cardColor;
                card.comparison.rectTransform.sizeDelta=new Vector2(width-24,height-86);
                foreach(var label in new[]{card.binding,card.slot,card.current})
                { var size=label.rectTransform.sizeDelta; size.x=width-24; label.rectTransform.sizeDelta=size; }
                card.binding.color=card.available ? accentColor : new Color(0.65f,0.65f,0.65f);
            }
            y+=height+8;
        }
        bool multiple=detector!=null && detector.DistinctNearbyPartCount>1;
        string cycle=input!=null ? input.CycleBindingLabel : null;
        var preferred=owner.PreferredSlotFor(part);
        string pickup=input!=null ? input.PickupBindingLabel : null;
        var retrieval=detector!=null ? detector.Retrieval : null;
        footer.text=part.IsRetrieving ? "Retrieving..." : preferred.HasValue && !string.IsNullOrEmpty(pickup) && retrieval!=null
            ? !retrieval.InRange(part) ? "Out of range ("+retrieval.pickupRange.ToString("0.#")+"m)"
                : !retrieval.CanStart(part) ? "Pickup blocked" : "["+pickup+"] Retrieve: "+PartSwapDisplay.SocketName(preferred.Value)
            : "";
        if(multiple && !string.IsNullOrEmpty(cycle)) footer.text+=(footer.text.Length>0 ? "    " : "")+"["+cycle+"] Next";
        if(slots.Count==0 && !part.IsRetrieving) footer.text="Unavailable";
        footer.gameObject.SetActive(footer.text.Length>0);
        if(statusBacking!=null)
        {
            statusBacking.gameObject.SetActive(footer.text.Length>0);
            statusBacking.rectTransform.anchoredPosition=new Vector2(0,-y+4);
            statusBacking.rectTransform.sizeDelta=new Vector2(660,28);
        }
        panel.sizeDelta=new Vector2(660,y+(footer.text.Length>0 ? 28 : 0));
        footer.rectTransform.anchoredPosition=new Vector2(14,-y);
        UnityEngine.Canvas.ForceUpdateCanvases();
    }
    public void SetAlpha(float alpha) { group.alpha=alpha; canvas.enabled=alpha>0.001f; }
    public void Acknowledge(PartCategory slot,float progress)
    {
        foreach(var card in cards) if(card.root.gameObject.activeSelf && card.category==slot)
        {
            card.background.color=Color.Lerp(cardColor,accentColor,0.25f*Mathf.Sin(progress*Mathf.PI));
            card.binding.text="EQUIPPED";
        }
    }
    public void Clear()
    {
        SetAlpha(0); slots.Clear(); partName.text=partType.text=statsLeft.text=statsRight.text=footer.text="";
        foreach(var card in cards) { card.binding.text=card.slot.text=card.current.text=card.comparison.text=""; card.root.gameObject.SetActive(false); }
    }
}
