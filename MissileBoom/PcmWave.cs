using System;

namespace Boscali.ExperimentalMissileBoom
{
    // Only the signed 16-bit PCM format used by the three pinned recordings is accepted.
    // The pure decoder also permits asset validation without loading a Unity runtime.
    internal static class PcmWave
    {
        internal static int NonRepeatingIndex(int count, int previous, int draw)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            if (previous < 0 || previous >= count) return draw % count;
            int next = draw % (count - 1);
            return next >= previous ? next + 1 : next;
        }

        internal static bool TryDecode16(byte[] data, out float[] samples, out int channels, out int rate)
        {
            samples = null; channels = rate = 0;
            if (data == null || data.Length < 12 || !Tag(data, 0, "RIFF") || !Tag(data, 8, "WAVE")) return false;
            long declaredEnd = (long)BitConverter.ToUInt32(data, 4) + 8;
            if (declaredEnd < 12 || declaredEnd > data.Length) return false;
            int end = (int)declaredEnd, offset = -1, length = 0, block = 0;
            bool format = false;
            for (int p = 12; p <= end - 8;)
            {
                uint size = BitConverter.ToUInt32(data, p + 4);
                long next = (long)p + 8 + size + (size & 1);
                if (next > end) return false;
                int content = p + 8;
                if (Tag(data, p, "fmt "))
                {
                    if (size < 16 || BitConverter.ToUInt16(data, content) != 1 || BitConverter.ToUInt16(data, content + 14) != 16) return false;
                    channels = BitConverter.ToUInt16(data, content + 2);
                    rate = BitConverter.ToInt32(data, content + 4);
                    block = BitConverter.ToUInt16(data, content + 12);
                    if (channels < 1 || channels > 2 || rate < 8000 || rate > 192000 || block != channels * 2) return false;
                    format = true;
                }
                else if (Tag(data, p, "data")) { offset = content; length = (int)size; }
                p = (int)next;
            }
            if (!format || offset < 0 || length <= 0 || length % block != 0 || length > 8 * 1024 * 1024) return false;
            samples = new float[length / 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(data, offset + i * 2) / 32768f;
            return true;
        }

        private static bool Tag(byte[] bytes, int start, string value)
        {
            if (start < 0 || start > bytes.Length - value.Length) return false;
            for (int i = 0; i < value.Length; i++) if (bytes[start + i] != value[i]) return false;
            return true;
        }
    }
}
