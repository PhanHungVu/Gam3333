using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Read-only UI formatting. Values come from Part.Stats; compatibility comes from
// the existing sockets, PartSlots and MechRulesConfig, never a second rule table.
public static class PartSwapDisplay
{
    // Show only player-facing changes, at most four lines per destination.
    static readonly StatType[] order={StatType.Damage,StatType.CooldownReload,StatType.MoveSpeed,
        StatType.Health,StatType.Range,StatType.MagSize,StatType.Ammo,StatType.SplashRadius};
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
    public static int Comparison(Part current,Part incoming,out string text)
    {
        var old=current!=null ? current.Stats : null; var next=incoming!=null ? incoming.Stats : null;
        var result=new StringBuilder(); int count=0;
        foreach(var type in order)
        {
            // Missing values are not zero (e.g. melee has no ammunition limit).
            if(!Has(next,type) || !Has(old,type)) continue;
            float before=old.GetStat(type),delta=next.GetStat(type)-before;
            if(float.IsNaN(delta)||float.IsInfinity(delta)||Mathf.Abs(delta)<0.0001f) continue;
            bool better=type==StatType.CooldownReload ? delta<0 : delta>0;
            int strength=Mathf.Abs(delta)/Mathf.Max(0.001f,Mathf.Abs(before))>=0.25f ? 2 : 1;
            if(count++>0) result.Append('\n');
            result.Append(Label(type,next)).Append(" <color=").Append(better ? "#8FD6A2" : "#ED9990").Append('>')
                .Append(new string(better ? '+' : '-',strength)).Append("</color>");
            if(count==4) break;
        }
        text=result.Length==0 ? "—" : result.ToString(); return count;
    }
}
