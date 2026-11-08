// DBPF helpers. Ported from the original CCMerger (Lazy Duchess), whose package code
// was in turn modified from FreeSO (https://github.com/riperiperi/FreeSO, MPL 2.0)
// and SimUnity2 (https://github.com/LazyDuchess/SimUnity2).

namespace CCMerger.Dbpf;

internal static class Hash
{
    /// <summary>
    /// Entries whose GroupID is 0xFFFFFFFF ("local group") get a GroupID derived from the
    /// package file name (without extension). This is the same CRC-24 variant the game uses.
    /// </summary>
    public static uint GroupHash(string name)
    {
        name = name.Trim().ToLowerInvariant();
        long crc = 0x00B704CE;
        foreach (char c in name)
        {
            crc ^= c << 16;
            for (int i = 0; i < 8; i++)
            {
                crc <<= 1;
                if ((crc & 0x1000000) != 0)
                    crc ^= 0x01864CFB;
            }
        }
        return (uint)((crc & 0x00FFFFFF) | 0x7F000000);
    }
}
