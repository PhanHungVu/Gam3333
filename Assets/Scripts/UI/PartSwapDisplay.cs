using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// Read-only UI formatting. Values come from Part.Stats; compatibility comes from
// the existing sockets, PartSlots and MechRulesConfig, never a second rule table.
public static class PartSwapDisplay
{
    // Basic incoming values above; only changed values per destination below.
    static readonly StatType[] order={StatType.Damage,StatType.CooldownReload,StatType.MoveSpeed,
        StatType.Health,StatType.Range,StatType.Ammo,StatType.MagSize,StatType.SplashRadius};
    public static string Name(Part part) => part==null ? "Empty" :
        part.Stats!=null && !string.IsNullOrWhiteSpace(part.Stats.partName) ? part.Stats.partName : part.name;
    public static string SocketName(PartCategory slot) => slot switch {
        PartCategory.ArmL=>"Left",PartCategory.ArmR=>"Right",
        PartCategory.ShoulderL=>"Left",PartCategory.ShoulderR=>"Right",
        PartCategory.Head=>"Head",PartCategory.Legs=>"Legs",_=>"Torso"
    };
    public static void CompatibleSlots(EquipManager owner,Part part,List<PartCategory> result)
    {
        result.Clear(); if(owner==null || part==null || part.Stats==null) return;
        var sockets=owner.GetComponentsInChildren<Socket>(true);
        foreach(var category in PartSlots.SlotsFor(part.partType)) foreach(var socket in sockets)
        {
            if(socket.socketType!=category) continue;
            bool fits=owner.rulesConfig!=null ? owner.rulesConfig.CanEquip(socket,part) : PartSlots.Fits(socket.socketType,part.partType);
            if(fits) { result.Add(socket.socketType); break; }
        }
    }
    static bool Has(PartData data,StatType type)
    { if(data!=null && data.stats!=null) foreach(var entry in data.stats) if(entry.type==type) return true; return false; }
    static string Label(StatType type,PartData incoming) => type switch {
        StatType.MoveSpeed=>"Movement",StatType.MagSize=>"Magazine",StatType.Ammo=>"Ammo",
        StatType.CooldownReload=>incoming.subtype==PartSubtype.Melee ? "Attack rate" : "Fire rate",
        StatType.Range=>"Range",StatType.SplashRadius=>"Blast size",_=>type.ToString()
    };
    static bool Value(PartData data,StatType type,out float value)
    {
        value=0; if(!Has(data,type)) return false;
        value=data.GetStat(type); if(float.IsNaN(value)||float.IsInfinity(value)) return false;
        // Both existing attack paths clamp the interval to at least 0.01 s.
        if(type==StatType.CooldownReload) value=1f/Mathf.Max(0.01f,value);
        return true;
    }
    static string Number(float value)=>value.ToString(value!=0 && Mathf.Abs(value)<0.01f ? "0.##e+0" : "0.##",CultureInfo.InvariantCulture);
    static string Unit(StatType type)=>type==StatType.CooldownReload ? "/s" : "";
    public static int BasicStats(Part incoming,out string text)
    {
        var data=incoming!=null ? incoming.Stats : null; var result=new StringBuilder(); int count=0;
        foreach(var type in order)
        {
            if(!Value(data,type,out float value)) continue;
            if(count++>0) result.Append('\n');
            result.Append(Label(type,data)).Append(' ').Append(Number(value)).Append(Unit(type));
            if(count==4) break;
        }
        text=result.ToString(); return count;
    }
    public static int Comparison(Part current,Part incoming,out string text)
    {
        var old=current!=null ? current.Stats : null; var next=incoming!=null ? incoming.Stats : null;
        var result=new StringBuilder(); int count=0;
        foreach(var type in order)
        {
            // Missing values are not zero (e.g. melee has no ammunition limit).
            if(!Value(next,type,out float after) || !Value(old,type,out float before)) continue;
            float delta=after-before;
            if(float.IsNaN(delta)||float.IsInfinity(delta)||Mathf.Abs(delta)<0.0001f) continue;
            bool better=delta>0;
            if(count++>0) result.Append('\n');
            result.Append(Label(type,next)).Append(" <color=").Append(better ? "#8FD6A2" : "#ED9990").Append('>')
                .Append(better ? "+" : "").Append(Number(delta)).Append(Unit(type)).Append("</color>");
            if(count==4) break;
        }
        text=result.Length==0 ? "—" : result.ToString(); return count;
    }
}
