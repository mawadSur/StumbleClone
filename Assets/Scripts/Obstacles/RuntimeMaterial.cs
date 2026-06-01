using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// Runtime-spawned primitives (GameObject.CreatePrimitive) default to the built-in Standard
    /// material, whose shader is NOT in the URP build — so they render bright PINK in the player.
    /// This builds a URP/Lit material instead. URP/Lit is guaranteed to be in the build because
    /// the baked level geometry already uses it, so Shader.Find resolves it at runtime.
    public static class RuntimeMaterial
    {
        private static Shader _lit;

        private static Shader Lit()
        {
            if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit");
            if (_lit == null) _lit = Shader.Find("Standard"); // editor/last-ditch fallback
            return _lit;
        }

        public static Material Make(Color color, bool emissive = false)
        {
            var m = new Material(Lit());
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
            if (emissive && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", color);
            }
            return m;
        }

        /// Replace the renderer's (pink Standard) material with a URP/Lit one of the given colour.
        public static void Apply(GameObject go, Color color, bool emissive = false)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = Make(color, emissive);
        }
    }
}
