using System.Text;

namespace VigorSearch;

public record RecoveryResult(bool Success, XorShiftState State, string Log);

/// <summary>
/// 観測したスキル名の並びから、XorShiftの初期State(128bit)を復元する。
/// Pythonの記号シミュレーション + GF(2)のガウス消去をそのまま移植したもの。
/// </summary>
public static class StateRecovery
{
    const int Stride = 9;  // 1件あたりのNext()回数
    const int Offset = 1;  // 1行目の抽選が、復元stateからの1回目のNext()

    // スキル名 -> %224 の取りうる値(Aテーブルの重みから作る)
    static readonly Dictionary<string, int[]> Types = BuildTypes();

    static Dictionary<string, int[]> BuildTypes()
    {
        var d = new Dictionary<string, int[]>();
        int pos = 0;
        foreach (var s in SkillData.A)
        {
            d[s.name] = Enumerable.Range(pos, s.w).ToArray();
            pos += s.w;
        }
        return d;
    }

    // 取りうる値すべてで共通する下位5bit (224は32の倍数なので下位5bitはそのまま観測できる)
    static (uint mask, uint value) CommonBits(int[] rs)
    {
        uint mask = 31, v = (uint)rs[0] & 31;
        foreach (var r in rs) mask &= ~(((uint)r & 31) ^ v);
        return (mask, v & mask);
    }

    // ---------- 記号シミュレーション ----------
    // 32bit値 = 「初期state128bitのXOR」を表すUInt128マスクの配列(長さ32)
    static UInt128[] Shl(UInt128[] w, int k)
    {
        var r = new UInt128[32];
        for (int i = k; i < 32; i++) r[i] = w[i - k];
        return r;
    }

    static UInt128[] Shr(UInt128[] w, int k)
    {
        var r = new UInt128[32];
        for (int i = 0; i + k < 32; i++) r[i] = w[i + k];
        return r;
    }

    static UInt128[] Xor(UInt128[] a, UInt128[] b)
    {
        var r = new UInt128[32];
        for (int i = 0; i < 32; i++) r[i] = a[i] ^ b[i];
        return r;
    }

    static UInt128[][] SymbolicOutputs(int maxStep)
    {
        var s = new UInt128[4][];
        for (int n = 0; n < 4; n++)
        {
            s[n] = new UInt128[32];
            for (int i = 0; i < 32; i++) s[n][i] = UInt128.One << (n * 32 + i);
        }

        var outs = new UInt128[maxStep + 1][];
        for (int step = 1; step <= maxStep; step++)
        {
            var t0 = Xor(s[0], Shl(s[0], 15));
            var t1 = Xor(Shr(s[3], 17), t0);
            var n3 = Xor(s[3], Xor(Shr(t1, 4), t0));
            s[0] = s[1]; s[1] = s[2]; s[2] = s[3]; s[3] = n3;
            outs[step] = n3;
        }
        return outs;
    }

    public static RecoveryResult Recover(string path)
    {
        var log = new StringBuilder();
        var lines = File.ReadAllLines(path, Encoding.UTF8)
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0)
                        .ToArray();

        var obs = new List<(int step, uint mask, uint value)>();
        for (int k = 0; k < lines.Length; k++)
        {
            if (!Types.TryGetValue(lines[k], out var vals)) continue; // 「はずれ」等は飛ばす(行番号は進める)
            var (m, v) = CommonBits(vals);
            obs.Add((Offset + Stride * k, m, v));
        }

        log.AppendLine($"行数 {lines.Length} / 使える観測 {obs.Count}");
        if (obs.Count == 0)
        {
            log.AppendLine("使える観測がありません");
            return new RecoveryResult(false, default, log.ToString());
        }
        return Solve(obs, log);
    }

    // ---------- 計算 ----------
    static RecoveryResult Solve(List<(int step, uint mask, uint value)> obs, StringBuilder log)
    {
        int maxStep = obs.Max(o => o.step);
        var outs = SymbolicOutputs(maxStep);

        // 最上位ビット位置 -> (mask, rhs)
        var has = new bool[128];
        var bm = new UInt128[128];
        var br = new int[128];

        foreach (var (step, mask, value) in obs)
        {
            for (int b = 0; b < 32; b++)
            {
                if (((mask >> b) & 1) == 0) continue;

                UInt128 m = outs[step][b];
                int rhs = (int)((value >> b) & 1);
                bool inserted = false;

                while (m != UInt128.Zero)
                {
                    int p = 127 - (int)UInt128.LeadingZeroCount(m);
                    if (has[p])
                    {
                        m ^= bm[p];
                        rhs ^= br[p];
                    }
                    else
                    {
                        has[p] = true; bm[p] = m; br[p] = rhs;
                        inserted = true;
                        break;
                    }
                }

                if (!inserted && rhs != 0)
                {
                    log.AppendLine("矛盾: 観測の誤り or 位置ずれ");
                    return new RecoveryResult(false, default, log.ToString());
                }
            }
        }

        int rank = has.Count(h => h);
        log.AppendLine($"rank = {rank}/128");
        if (rank < 128)
        {
            log.AppendLine($"観測不足。自由度 {128 - rank}bit");
            return new RecoveryResult(false, default, log.ToString());
        }

        // 低いビットから順に確定
        var x = new int[128];
        for (int p = 0; p < 128; p++)
        {
            int v = br[p];
            for (int q = 0; q < p; q++)
                if (((bm[p] >> q) & UInt128.One) != UInt128.Zero && x[q] != 0) v ^= 1;
            x[p] = v;
        }

        var S = new uint[4];
        for (int i = 0; i < 128; i++)
            if (x[i] != 0) S[i / 32] |= 1u << (i % 32);
        var state = new XorShiftState(S[0], S[1], S[2], S[3]);
        log.AppendLine($"S0={S[0]:X8} S1={S[1]:X8} S2={S[2]:X8} S3={S[3]:X8}");

        // 検証: 復元したstateを実際に進めて観測と照合
        var byStep = obs.ToDictionary(o => o.step);
        var st = state;
        bool ok = true;
        for (int step = 1; step <= maxStep; step++)
        {
            uint n3 = st.Next();
            if (byStep.TryGetValue(step, out var o) && ((n3 ^ o.value) & o.mask) != 0)
                ok = false;
        }
        log.AppendLine(ok ? "検証OK" : "検証NG");
        return new RecoveryResult(ok, state, log.ToString());
    }
}
