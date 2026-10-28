using System;
using System.Reflection;

// 血条·装填倒计时 v1.25.0 的**离线反射闸门**。
//
// 目的：不启动游戏，直接拿游戏程序集验证"Mod 里那套反射查找 + 取值"到底能不能命中。
// 判据必须是**与 Mod 源码逐字一致的查找逻辑**（FindFieldAlong 的 BindingFlags），
// 否则这里绿了、游戏里照样空着（"假修复"）。
class Program
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    static FieldInfo FindFieldAlong(Type t, string name)
    {
        Type cur = t;
        while (cur != null)
        {
            FieldInfo fi = cur.GetField(name, F);
            if (fi != null) { return fi; }
            cur = cur.BaseType;
        }
        return null;
    }

    static int fails = 0;

    static void Check(string title, FieldInfo fi, Type declareType)
    {
        if (fi == null)
        {
            Console.WriteLine("FAIL  " + title + " -> 字段找不到");
            fails++;
            return;
        }
        bool ok = fi.DeclaringType == declareType;
        Console.WriteLine((ok ? "OK    " : "FAIL  ") + title
            + " -> " + fi.DeclaringType.Name + "." + fi.Name
            + " : " + fi.FieldType.Name
            + (fi.IsLiteral ? " [const]" : "")
            + (fi.IsStatic ? " [static]" : ""));
        if (!ok) { fails++; }
    }

    static void Main(string[] args)
    {
        string gameDir = args[0];
        Assembly asm = Assembly.LoadFrom(System.IO.Path.Combine(gameDir, "PlantsVsZombies.dll"));

        // ---------- 1. 磁力花盆：_digesting / _digestTimer / const DigestTime ----------
        Type pot = asm.GetType("TowerDefensePlantMagnetPot", true);
        Console.WriteLine("== " + pot.FullName + " : " + pot.BaseType.Name);
        FieldInfo fDigesting = FindFieldAlong(pot, "_digesting");
        FieldInfo fDigestTimer = FindFieldAlong(pot, "_digestTimer");
        Check("磁力花盆 _digesting", fDigesting, pot);
        Check("磁力花盆 _digestTimer", fDigestTimer, pot);
        if (fDigesting != null && fDigesting.FieldType != typeof(bool))
        {
            Console.WriteLine("FAIL  _digesting 不是 bool，是 " + fDigesting.FieldType.Name); fails++;
        }
        if (fDigestTimer != null && fDigestTimer.FieldType != typeof(double))
        {
            Console.WriteLine("FAIL  _digestTimer 不是 double，是 " + fDigestTimer.FieldType.Name); fails++;
        }
        // const DigestTime 必须先 GetRawConstantValue（GetValue 对 literal 会抛）
        FieldInfo fConst = FindFieldAlong(pot, "DigestTime");
        Check("磁力花盆 const DigestTime", fConst, pot);
        if (fConst != null)
        {
            if (!fConst.IsLiteral)
            {
                Console.WriteLine("FAIL  DigestTime 不是 literal（预期 const）"); fails++;
            }
            try
            {
                object raw = fConst.GetRawConstantValue();
                Console.WriteLine("      GetRawConstantValue() = " + raw);
                if (!(raw is double d) || Math.Abs(d - 20.0) > 1e-9)
                {
                    Console.WriteLine("FAIL  DigestTime 常量不是 20.0"); fails++;
                }
                // 实测记录（与 v1.17.0 的 MaxLevel 教训不同）：
                // 这里是 `public const double`，FieldInfo.GetValue 在 .NET 上**不抛**、能直接取值；
                // 但**不依赖**这一点 —— Mod 里仍走 GetRawConstantValue（对 literal 是唯一安全通道）。
                object viaGetValue = null;
                try { viaGetValue = fConst.GetValue(null); } catch (Exception ex) { viaGetValue = "抛:" + ex.GetType().Name; }
                Console.WriteLine("      GetValue(null) = " + viaGetValue + "（两条通道都记下来，Mod 用前者）");
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  GetRawConstantValue 失败: " + ex.Message); fails++;
            }
        }

        // ---------- 2. 内卷投手：_fireNum / maxFireNum ----------
        Type cab = asm.GetType("TowerDefensePlantCabbagepultInside", true);
        Console.WriteLine();
        Console.WriteLine("== " + cab.FullName + " : " + cab.BaseType.Name);
        FieldInfo fFireNum = FindFieldAlong(cab, "<fireNum>k__BackingField");
        Console.WriteLine("      <fireNum>k__BackingField = " + (fFireNum == null ? "null（如预期，手写备份字段）" : "存在"));
        if (fFireNum != null) { Console.WriteLine("FAIL  预期该类没有编译器生成 backing field"); fails++; }
        fFireNum = FindFieldAlong(cab, "_fireNum");
        FieldInfo fMax = FindFieldAlong(cab, "maxFireNum");
        Check("内卷投手 _fireNum", fFireNum, cab);
        Check("内卷投手 maxFireNum", fMax, cab);
        if (fFireNum != null && fFireNum.FieldType != typeof(int))
        {
            Console.WriteLine("FAIL  _fireNum 不是 int"); fails++;
        }
        if (fMax != null && fMax.FieldType != typeof(int))
        {
            Console.WriteLine("FAIL  maxFireNum 不是 int"); fails++;
        }

        // ---------- 3. 识别判据的唯一性：maxFireNum 在**角色类**里只有它 ----------
        Console.WriteLine();
        Console.WriteLine("== 识别判据唯一性（maxFireNum）");
        Type charBase = asm.GetType("TowerDefenseCharacter", true);
        int maxCount = 0;
        string who = "";
        foreach (Type t in asm.GetTypes())
        {
            if (!charBase.IsAssignableFrom(t)) { continue; }
            try
            {
                if (t.GetField("maxFireNum", F) != null)
                {
                    maxCount++;
                    who += " " + t.Name;
                }
            }
            catch { }
        }
        Console.WriteLine("      角色类里带 maxFireNum 的类型数 = " + maxCount + "（预期 1）" + who);
        if (maxCount != 1) { fails++; }

        // 说明：Godot 源码生成器还会产出若干个**嵌套静态类 `PropertyName`**
        //   （每个带 [Export] 的脚本一个），里面也有一行 `public static readonly StringName maxFireNum`
        //   —— 它们不是角色、也不在 character.GetType() 的继承链上，故不影响判据；
        //   这里把数量打出来，免得以后有人拿"全库搜索"当唯一性证据被误导。
        int genCount = 0;
        foreach (Type t in asm.GetTypes())
        {
            if (t.Name != "PropertyName") { continue; }
            try { if (t.GetField("maxFireNum", F) != null) { genCount++; } } catch { }
        }
        Console.WriteLine("      （另有 " + genCount + " 个生成器静态类 PropertyName 含同名字段，非角色类）");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "==> 全部通过（反射路径在真实程序集上成立）"
                                    : "==> 有 " + fails + " 项失败");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
