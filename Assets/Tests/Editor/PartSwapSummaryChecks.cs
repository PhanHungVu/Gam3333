using System;
using System.Collections.Generic;
using System.Globalization;
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
            oldData.stats=new List<StatEntry>{Stat(StatType.Damage,40)};
            newData.stats=new List<StatEntry>{Stat(StatType.Damage,50)};
            PartSwapDisplay.Comparison(current,incoming,out string text);
            Check(Plain(text)=="Damage +10"&&text.Contains("#8FD6A2"),"New 50 damage versus old 40 shows the exact green +10 difference");
            oldData.stats=new List<StatEntry>{Stat(StatType.Damage,50)};
            newData.stats=new List<StatEntry>{Stat(StatType.Damage,40)};
            PartSwapDisplay.Comparison(current,incoming,out text);
            Check(Plain(text)=="Damage -10"&&text.Contains("#ED9990"),"New 40 damage versus old 50 shows the exact red -10 difference");
            oldData.stats=new List<StatEntry>{Stat(StatType.Damage,100),Stat(StatType.CooldownReload,1),Stat(StatType.MoveSpeed,10)};
            newData.stats=new List<StatEntry>{Stat(StatType.Damage,50),Stat(StatType.CooldownReload,0.9f),Stat(StatType.MoveSpeed,6)};
            newData.subtype=PartSubtype.Projectile;
            int count=PartSwapDisplay.Comparison(current,incoming,out text); string plain=Plain(text);
            Check(count==3&&plain.Contains("Damage -50"),"Damage comparisons show incoming minus current, without repeating base values");
            Check(plain.Contains("Fire rate +0.11/s"),"Fire rate difference compares attacks per second instead of mislabelling cooldown seconds");
            Check(plain.Contains("Movement -4"),"Movement loss shows its actual numeric difference");
            Check(text.Contains("#8FD6A2")&&text.Contains("#ED9990"),"Benefits and losses have distinct colours");
            Check(!plain.Contains("++")&&!plain.Contains("--")&&!plain.Contains("%"),"Numeric comparisons contain no qualitative signs or percentage bars");
            Check(PartSwapDisplay.BasicStats(incoming,out string basic)==3&&basic=="Damage 50\nFire rate 1.11/s\nMovement 6","Incoming basics show numeric values in the same units as the comparisons");
            newData.stats=new List<StatEntry>{Stat(StatType.Damage,100),Stat(StatType.CooldownReload,1),Stat(StatType.MoveSpeed,10)};
            Check(PartSwapDisplay.Comparison(current,incoming,out text)==0&&text=="—","Unchanged stats produce no repeated rows");
            oldData.stats=new List<StatEntry>{Stat(StatType.CooldownReload,1)};
            newData.stats=new List<StatEntry>{Stat(StatType.CooldownReload,2)}; newData.subtype=PartSubtype.Melee;
            PartSwapDisplay.Comparison(current,incoming,out text);
            Check(Plain(text)=="Attack rate -0.5/s","A slower melee interval shows the actual attack-rate loss");
            oldData.stats=new List<StatEntry>(); newData.stats=new List<StatEntry>{Stat(StatType.Ammo,30)};
            Check(PartSwapDisplay.Comparison(current,incoming,out text)==0,"Missing melee ammunition is not treated as zero capacity");
            oldData.stats=new List<StatEntry>{Stat(StatType.Health,100),Stat(StatType.MoveSpeed,10),Stat(StatType.Damage,10),Stat(StatType.CooldownReload,1),Stat(StatType.Range,1),Stat(StatType.MagSize,1)};
            newData.stats=new List<StatEntry>{Stat(StatType.Health,200),Stat(StatType.MoveSpeed,20),Stat(StatType.Damage,20),Stat(StatType.CooldownReload,0.5f),Stat(StatType.Range,2),Stat(StatType.MagSize,2)};
            Check(PartSwapDisplay.Comparison(current,incoming,out text)==4&&Plain(text).Split('\n').Length==4,"A crowded comparison is capped at four relevant changes");
            Check(PartSwapDisplay.Comparison(null,incoming,out text)==0,"Empty slots do not invent comparison values");
            Check(PartSwapDisplay.BasicStats(incoming,out basic)==4&&basic.Split('\n').Length==4,"Incoming basics also stay capped at four player-facing stats");
            newData.stats=new List<StatEntry>{Stat(StatType.SingleShot,1),Stat(StatType.AutoAim,1),Stat(StatType.ProjectileSpeed,100)};
            Check(PartSwapDisplay.BasicStats(incoming,out basic)==0&&basic=="","Technical flags and projectile internals do not fill the incoming header");
            oldData.stats=new List<StatEntry>{Stat(StatType.Damage,40)};
            newData.stats=new List<StatEntry>{Stat(StatType.Damage,float.NaN),Stat(StatType.Health,float.PositiveInfinity)};
            Check(PartSwapDisplay.BasicStats(incoming,out basic)==0&&PartSwapDisplay.Comparison(current,incoming,out text)==0,"Non-finite stats never reach either readout");
            var culture=CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
                newData.stats=new List<StatEntry>{Stat(StatType.Damage,50.5f)};
                PartSwapDisplay.Comparison(current,incoming,out text);
                Check(Plain(text)=="Damage +10.5","Decimal differences retain consistent formatting across Editor locales");
            }
            finally { CultureInfo.CurrentCulture=culture; }
        }
        finally { Object.DestroyImmediate(oldObject); Object.DestroyImmediate(newObject); Object.DestroyImmediate(oldData); Object.DestroyImmediate(newData); }
        Debug.Log("COMPACT SWAP CHECKS COMPLETE: "+checks);
    }
}
