using System.ComponentModel;

namespace VigorSearch;

public class Row
{
    [DisplayName("#")] public int Index { get; init; }
    [DisplayName("スキル1")] public string Skill1 { get; init; }
    [DisplayName("Lv")] public int Lv1 { get; init; }
    [DisplayName("スキル2")] public string Skill2 { get; init; }
    [DisplayName("Lv")] public int Lv2 { get; init; }
    [DisplayName("スロット")] public string Slot { get; init; }

    [Browsable(false)] public XorShiftState Before { get; init; }
    [Browsable(false)] public XorShiftState After { get; init; }

    [DisplayName("State")] public string StateBefore => Before.ToString();
   
}

public static class Generator
{
    static readonly HashSet<string> HeadSkills = new() { "超会心", "攻撃", "火属性攻撃強化", "体術" };
    static readonly HashSet<string> LastSkills = new() { "狂化", "狂竜症【翔】", "状態異常確定蓄積", "剛心" };

    static Skill[] PickTable(uint r, out int div)
    {
        if (r <= 15) { div = 93; return SkillData.C; }
        if (r <= 53) { div = 182; return SkillData.B; }
        if (r <= 85) { div = 224; return SkillData.A; }
        div = 100; return SkillData.S;
    }

    static int PickIndex(uint r, Skill[] table)
    {
        int pos = 0;
        for (int i = 0; i < table.Length; i++)
        {
            pos += table[i].w;
            if (r < pos) return i;
        }
        return -1; // 該当なし
    }

    static int GetLevel(int[] th, int rng)
    {
        for (int i = 0; i < th.Length; i++)
            if (rng <= th[i]) return i + 1;
        return th.Length;
    }

    static string SlotName(uint r)
    {
        int pos = 0;
        foreach (var (name, w) in SkillData.Slots)
        {
            pos += w;
            if (r < pos) return name;
        }
        return "?";
    }

    /// <summary>1件分を生成し、rngを9回分進める。</summary>
    public static Row Generate(ref XorShiftState rng, int index)
    {
        var before = rng;
        uint rnd = rng.S3;

        // 1つ目のスキル
        var t1 = PickTable(rnd % 100, out int div);
        rnd = rng.Next();
        int i1 = PickIndex(rnd % (uint)div, t1);

        // 2つ目のスキル
        rnd = rng.Next();
        var t2 = PickTable(rnd % 100, out div);
        rnd = rng.Next();
        if (i1 >= 0 && t2 == t1)
        {
            string n1 = t1[i1].name;
            int w = t1[i1].w;
            if (HeadSkills.Contains(n1)) div -= (w - 1);
            else if (LastSkills.Contains(n1)) div -= (w + 1);
            else div -= w;
            t2 = t2.Where(x => x.name != n1).ToArray();
        }
        int i2 = PickIndex(rnd % (uint)div, t2);

        // レベル
        rng.Next();
        rnd = rng.Next();
        int lv1 = i1 >= 0 ? t1[i1].s + GetLevel(t1[i1].th, (int)(rnd % 100)) : 0;

        rng.Next();
        rnd = rng.Next();
        int lv2 = i2 >= 0 ? t2[i2].s2 + GetLevel(t2[i2].th2, (int)(rnd % 100)) : 0;

        // スロット
        rnd = rng.Next();
        string slot = SlotName(rnd % 100);
        rng.Next();

        return new Row
        {
            Index = index,
            Skill1 = i1 >= 0 ? t1[i1].name : "?",
            Lv1 = lv1,
            Skill2 = i2 >= 0 ? t2[i2].name : "?",
            Lv2 = lv2,
            Slot = slot,
            Before = before,
            After = rng,
        };
    }
}
