using Boscali.ExperimentalMissileBoom;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
string folder = args.Length > 0 ? args[0] : "../Sounds";
foreach (string path in Directory.GetFiles(folder, "MissileBoom*.wav"))
{
    byte[] data = File.ReadAllBytes(path);
    Check(PcmWave.TryDecode16(data, out var samples, out int channels, out int rate), "Decode " + path);
    Check(channels == 2 && rate == 44100, "Pinned format");
    Check(samples.Length > 44100 / 2 && samples.Length < 44100 * 4, "Expected bounded duration");
    Check(samples.All(s => s >= -1 && s <= 1) && samples.Any(s => Math.Abs(s) > .01f), "Non-silent normalized audio");
    Check(!PcmWave.TryDecode16(data.Take(25).ToArray(), out _, out _, out _), "Reject truncated RIFF");
    byte[] corrupt = (byte[])data.Clone(); Array.Fill(corrupt, (byte)255, 16, 4);
    Check(!PcmWave.TryDecode16(corrupt, out _, out _, out _), "Reject overflow chunk");
}
for (int count = 2; count <= 8; count++)
for (int old = 0; old < count; old++)
{
    var seen = new HashSet<int>();
    for (int draw = 0; draw < count - 1; draw++)
    {
        int next = PcmWave.NonRepeatingIndex(count, old, draw);
        Check(next >= 0 && next < count && next != old, "No repeat / valid range");
        seen.Add(next);
    }
    Check(seen.Count == count - 1, "All other clips reachable");
}
Check(PcmWave.NonRepeatingIndex(0, -1, 0) == -1, "Empty fallback");
Check(PcmWave.NonRepeatingIndex(1, 0, 0) == 0, "One-clip fallback");
Check(!PcmWave.TryDecode16(null, out _, out _, out _), "Null input");
Check(!PcmWave.TryDecode16(new byte[12], out _, out _, out _), "Invalid header");
Console.WriteLine($"PASS {checks} recording decode and non-repeat checks");
