
namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// Pure name scoring that picks the canopy glass renderer out of an aircraft
    /// hierarchy. A positive score means "plausibly canopy glass"; gunsights, HUD
    /// glass, mirrors and lamps score zero or negative and never win. Engine-agnostic;
    /// covered by CanopyScoringTests.
    /// </summary>
    internal static class CanopyScoring
    {
        public static bool PlausibleBounds(float eyeDistance, float diameter) =>
            eyeDistance >= 0f && eyeDistance <= 4f && diameter >= 0.08f && diameter <= 32f;

        public static int ScoreSurface(string obj, string mat, string shader, bool transparent,
            float eyeDistance, float diameter)
        {
            if ((!transparent && !KnownInteriorCanopy(obj, mat, shader)) || !PlausibleBounds(eyeDistance, diameter)) return 0;
            int score = ScoreGlass(obj, mat, shader);
            // Transparency also describes instruments and effects; require glass evidence.
            return score >= 80 ? score : 0;
        }

        private static bool KnownInteriorCanopy(string obj, string mat, string shader)
        {
            // Real F-99 and COIN interior glazing uses this precise shader at an
            // opaque queue. Both object and material must identify glazing; never
            // broaden the exception to unrelated shaders or generic opaque panels.
            if (!string.Equals(shader, "Shader Graphs/canopy_int", System.StringComparison.OrdinalIgnoreCase)) return false;
            string objectName = (obj ?? string.Empty).ToLowerInvariant();
            string materialName = (mat ?? string.Empty).ToLowerInvariant();
            bool namedGlass = ContainsAny(objectName, "glass", "window", "windscreen", "windshield");
            if (objectName.Contains("frame") && !namedGlass) return false;
            return (namedGlass || objectName.Contains("canopy")) &&
                ContainsAny(materialName, "canopy", "glass", "window", "windscreen", "windshield");
        }

        public static int ScoreGlass(string objectName, string materialName, string shaderName)
        {
            string obj = (objectName ?? string.Empty).ToLowerInvariant();
            string mat = (materialName ?? string.Empty).ToLowerInvariant();
            string shd = (shaderName ?? string.Empty).ToLowerInvariant();

            // Hard exclusions first: glazing that is NOT the canopy.
            if (ContainsAny(obj, "gunsight", "sightglass", "hud", "hmd", "mirror", "mfd", "mfdscreen")) return -500;
            if (ContainsAny(mat, "gunsight", "hud", "hmd", "mirror", "mfd", "reticle", "sight")) return -500;
            // Some meshes reuse all aircraft materials, including canopy, on the
            // fuselage and intake. A transparent canopy-named material alone must
            // not turn those non-glass structural meshes into rain surfaces.
            if (ContainsAny(obj, "fuselage", "intake") &&
                !ContainsAny(obj, "canopy", "glass", "window", "windscreen", "windshield")) return -500;
            // "light" alone is not evidence of a lamp: the F-99's materials are
            // named Aryx_LightFighter1_Canopy_Int. Exclude fixture names without
            // rejecting aircraft type names containing that substring.
            if (ContainsAny(obj, "lamp", "beacon", "strobe", "tire", "wheel", "gear") || IsLightFixture(obj)) return -500;
            if (ContainsAny(mat, "lamp", "beacon", "strobe", "tire", "emissive") || IsLightFixture(mat)) return -500;

            int score = 0;
            if (ContainsAny(obj, "canopy")) score += 400;
            if (ContainsAny(obj, "windscreen", "windshield")) score += 350;
            if (ContainsAny(obj, "cockpitglass", "cockpit_glass", "glasscanopy")) score += 300;
            if (ContainsAny(mat, "canopy")) score += 250;
            if (ContainsAny(mat, "windscreen", "windshield")) score += 220;
            if (ContainsAny(mat, "glass") && !ContainsAny(mat, "fiberglass", "plexiglass_nose")) score += 120;
            if (ContainsAny(obj, "cockpit") && ContainsAny(mat, "glass", "transparent")) score += 150;
            if (ContainsAny(shd, "glass", "transparent")) score += 60;
            if (ContainsAny(obj, "glass")) score += 80;
            return score;
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.Contains(needles[i])) return true;
            }
            return false;
        }

        private static bool IsLightFixture(string name)
        {
            if (name.EndsWith("light") || name.EndsWith("lights")) return true;
            for (int at = 0; at < name.Length; at++)
            {
                if (at + 5 > name.Length || string.CompareOrdinal(name, at, "light", 0, 5) != 0) continue;
                int end = at + 5;
                if (end == name.Length || !char.IsLetterOrDigit(name[end]) ||
                    (name[end] == 's' && (end + 1 == name.Length || !char.IsLetterOrDigit(name[end + 1])))) return true;
            }
            return false;
        }
    }
}
