using System;
using System.IO;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    // Load the original Boscali visor and first-person body assets; no canopy reflection pass.
    internal static class PilotShaderBundle
    {
        private static AssetBundle bundle;
        private static Shader visor, body;
        private static Mesh firstPerson;
        private static Texture2D bodyAlbedo, bodyNormal, bodyMask;
        private static bool attempted;
        internal static Shader GetVisor()
        {
            if (attempted) return visor;
            attempted = true;
            try
            {
                using (Stream stream = typeof(PilotShaderBundle).Assembly.GetManifestResourceStream("BoscaliClientExperimental.Immersion.pilot.bundle"))
                {
                    if (stream == null || stream.Length > 4 * 1024 * 1024) return null;
                    using (var bytes = new MemoryStream())
                    {
                        stream.CopyTo(bytes);
                        bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
                    }
                }
                if (bundle == null) return null;
                foreach (Shader shader in bundle.LoadAllAssets<Shader>())
                {
                    if (shader == null || !shader.isSupported) continue;
                    if (shader.name == "Boscali/VisorOverlay") visor = shader;
                    if (shader.name == "Boscali/PilotBody") body = shader;
                }
                firstPerson = bundle.LoadAsset<Mesh>("assets/pilotshader/pilot-first-person.asset");
                if (firstPerson != null && (firstPerson.vertexCount > 16384 || firstPerson.subMeshCount != 1)) firstPerson = null;
                bodyAlbedo = bundle.LoadAsset<Texture2D>("assets/pilotshader/body/pilot_albedo.png");
                bodyNormal = bundle.LoadAsset<Texture2D>("assets/pilotshader/body/pilot_normal.png");
                bodyMask = bundle.LoadAsset<Texture2D>("assets/pilotshader/body/pilot_mask.png");
            }
            catch (Exception e) { BoscaliClientExperimental.Immersion.ImmersionFeature.LogWarning("Visor bundle load failed: " + e.Message); }
            return visor;
        }
        internal static Shader GetBody() { GetVisor(); return body; }
        internal static Mesh GetFirstPersonMesh() { GetVisor(); return firstPerson; }
        internal static bool GetBodyTextures(out Texture2D albedo, out Texture2D normal, out Texture2D mask)
        {
            GetVisor();
            albedo = bodyAlbedo; normal = bodyNormal; mask = bodyMask;
            return albedo != null && normal != null && mask != null;
        }
        internal static void Release()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null; visor = body = null; firstPerson = null;
            bodyAlbedo = bodyNormal = bodyMask = null; attempted = false;
        }
    }
}
