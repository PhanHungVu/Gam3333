using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Object=UnityEngine.Object;

public static class PartSwapSummaryChecks
{
    static int checks;
    static void Check(bool valid,string message) { if(!valid) throw new Exception(message); checks++; Debug.Log("COMPACT SWAP PASS: "+message); }
    static StatEntry Stat(StatType type,float value)=>new StatEntry {type=type,value=value};
    static string Plain(string value)=>Regex.Replace(value,"<[^>]+>","");
    public static void RunBatch()
    {
        if(!Application.isBatchMode || !Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
            throw new Exception("Disposable project only");
        var oldData=ScriptableObject.CreateInstance<PartData>(); var newData=ScriptableObject.CreateInstance<PartData>();
        var oldObject=new GameObject("Current UI fixture"); var newObject=new GameObject("Incoming UI fixture");
        var current=oldObject.AddComponent<Part>(); var incoming=newObject.AddComponent<Part>(); current.data=oldData; incoming.data=newData;
        try
        {
            oldData.stats=new List<StatEntry>{Stat(StatType.Damage,100),Stat(StatType.CooldownReload,1),Stat(StatType.MoveSpeed,10)};
            newData.stats=new List<StatEntry>{Stat(StatType.Damage,50),Stat(StatType.CooldownReload,0.9f),Stat(StatType.MoveSpeed,6)};
            newData.subtype=PartSubtype.Projectile;
            int count=PartSwapDisplay.Comparison(current,incoming,out string text); string plain=Plain(text);
            Check(count==3&&plain.Contains("Damage --"),"A large damage loss shows a short negative indicator");
            Check(plain.Contains("Fire rate +")&&!plain.Contains("Fire rate -"),"Lower cooldown means improved fire rate");
            Check(plain.Contains("Movement --"),"Movement loss is visible without raw values");
            Check(text.Contains("#8FD6A2")&&text.Contains("#ED9990"),"Benefits and losses have distinct colours");
            Check(!Regex.IsMatch(plain,"[0-9]"),"Stat summaries contain no raw numbers or percentages");
            newData.stats=new List<StatEntry>{Stat(StatType.Damage,100),Stat(StatType.CooldownReload,1),Stat(StatType.MoveSpeed,10)};
            Check(PartSwapDisplay.Comparison(current,incoming,out text)==0&&text=="—","Unchanged stats produce no repeated rows");
            oldData.stats=new List<StatEntry>{Stat(StatType.CooldownReload,1)};
            newData.stats=new List<StatEntry>{Stat(StatType.CooldownReload,2)}; newData.subtype=PartSubtype.Melee;
            PartSwapDisplay.Comparison(current,incoming,out text);
            Check(Plain(text)=="Attack rate --","A slower melee interval is a loss, not an improvement");
            oldData.stats=new List<StatEntry>(); newData.stats=new List<StatEntry>{Stat(StatType.Ammo,30)};
            Check(PartSwapDisplay.Comparison(current,incoming,out text)==0,"Missing melee ammunition is not treated as zero capacity");
            oldData.stats=new List<StatEntry>{Stat(StatType.Health,100),Stat(StatType.MoveSpeed,10),Stat(StatType.Damage,10),Stat(StatType.CooldownReload,1),Stat(StatType.Range,1),Stat(StatType.MagSize,1)};
            newData.stats=new List<StatEntry>{Stat(StatType.Health,200),Stat(StatType.MoveSpeed,20),Stat(StatType.Damage,20),Stat(StatType.CooldownReload,0.5f),Stat(StatType.Range,2),Stat(StatType.MagSize,2)};
            Check(PartSwapDisplay.Comparison(current,incoming,out text)==4&&Plain(text).Split('\n').Length==4,"A crowded comparison is capped at four relevant changes");
            Check(PartSwapDisplay.Comparison(null,incoming,out text)==0,"Empty slots do not invent comparison values");
        }
        finally { Object.DestroyImmediate(oldObject); Object.DestroyImmediate(newObject); Object.DestroyImmediate(oldData); Object.DestroyImmediate(newData); }
        Debug.Log("COMPACT SWAP CHECKS COMPLETE: "+checks);
    }
}
