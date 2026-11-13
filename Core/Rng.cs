namespace VigorSearch;

public struct XorShiftState
{
    public uint S0, S1, S2, S3;

    public XorShiftState(uint s0, uint s1, uint s2, uint s3)
    {
        S0 = s0; S1 = s1; S2 = s2; S3 = s3;
    }

    public uint Next()
    {
        uint oldS0 = S0, oldS1 = S1, oldS2 = S2, oldS3 = S3;

        uint t0 = oldS0 ^ (oldS0 << 15);
        uint t1 = (oldS3 >> 17) ^ t0;
        uint newS3 = oldS3 ^ ((t1 >> 4) ^ t0);

        S0 = oldS1;
        S1 = oldS2;
        S2 = oldS3;
        S3 = newS3;
        return newS3;
    }

    public override string ToString() => $"{S0:X8} {S1:X8} {S2:X8} {S3:X8}";
}
