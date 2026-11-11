using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KropClientEffects
{
    internal static class NightVisionOverlay
    {
        private static GameObject root;
        private static NightVisionSpeckGraphic specks;
        private static NightVisionWipeGraphic wipe;
        private static int speckCount;
        private static float nextSpecks, phaseStart;
        private static int phase; // 1 = closing cover, 2 = opening/fading cover
        private static bool entering;
        private static Action flip;
        private static NightVision owner;
        internal static bool Busy => phase != 0;

        private static T Graphic<T>(string name, int order) where T : Graphic
        {
            if (root == null)
            {
                root = new GameObject("Unified NVG overlays") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(root);
            }
            var host = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            host.transform.SetParent(root.transform, false);
            var canvas = host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
            var child = new GameObject(name + " mesh", typeof(RectTransform), typeof(CanvasRenderer));
            child.transform.SetParent(host.transform, false);
            var rect = child.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            T graphic = child.AddComponent<T>(); graphic.raycastTarget = false;
            return graphic;
        }

        internal static void SetSpecks(int count)
        {
            speckCount = Mathf.Clamp(count, 0, 400);
            if (speckCount == 0) { if (specks != null) specks.gameObject.SetActive(false); return; }
            if (specks == null) specks = Graphic<NightVisionSpeckGraphic>("NVG sensor specks", short.MinValue);
            specks.gameObject.SetActive(true);
            if (Time.unscaledTime >= nextSpecks || specks.Count != speckCount)
            { nextSpecks = Time.unscaledTime + .05f; specks.Scatter(speckCount); }
        }

        internal static void Begin(bool isEntering, NightVision native, Action callback)
        {
            if (wipe == null) wipe = Graphic<NightVisionWipeGraphic>("NVG raise lower", short.MaxValue);
            owner = native; flip = callback; entering = isEntering; phase = 1; phaseStart = Time.unscaledTime;
            wipe.gameObject.SetActive(true); wipe.Set(0f, 1f, entering, false);
        }

        internal static void Tick()
        {
            if (phase == 0) return;
            if (owner == null || NightVision.i != owner || !Settings.Active || !NightVisionEffects.Transition.Value)
            { CancelTransition(); return; }
            float elapsed = Time.unscaledTime - phaseStart;
            if (phase == 1)
            {
                float t = Mathf.Clamp01(elapsed / .22f); wipe.Set(t, 1f, entering, false);
                if (t < 1f) return;
                var callback = flip; flip = null;
                phase = 2; phaseStart = Time.unscaledTime;
                callback?.Invoke();
            }
            else
            {
                float t = Mathf.Clamp01(elapsed / (entering ? .4f : .5f));
                wipe.Set(entering ? 1f - t : 1f, entering ? 1f : 1f - t, entering, entering);
                if (t >= 1f) CancelTransition();
            }
        }

        internal static void CancelTransition()
        {
            phase = 0; flip = null; owner = null;
            if (wipe != null) wipe.gameObject.SetActive(false);
        }

        internal static void Clear()
        {
            CancelTransition(); speckCount = 0; nextSpecks = 0f;
            if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            root = null; specks = null; wipe = null;
        }
    }

    // One bounded UI mesh instead of one GameObject/Canvas update per speck.
    internal sealed class NightVisionSpeckGraphic : MaskableGraphic
    {
        private readonly Vector2[] positions = new Vector2[400];
        private readonly System.Random random = new System.Random();
        internal int Count { get; private set; }
        internal void Scatter(int count)
        {
            Count = Mathf.Clamp(count, 0, positions.Length);
            for (int i = 0; i < Count; i++) positions[i] = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = GetPixelAdjustedRect();
            for (int i = 0; i < Count; i++)
            {
                float x = Mathf.Lerp(rect.xMin, rect.xMax, positions[i].x), y = Mathf.Lerp(rect.yMin, rect.yMax, positions[i].y);
                NightVisionWipeGraphic.Quad(mesh, x - 1f, y - 1f, x + 1f, y + 1f, Color.white, Color.white);
            }
        }
    }

    internal sealed class NightVisionWipeGraphic : MaskableGraphic
    {
        private float coverage, alpha; private bool top, lids;
        internal void Set(float amount, float opacity, bool fromTop, bool eyelids)
        { coverage = Mathf.Clamp01(amount); alpha = Mathf.Clamp01(opacity); top = fromTop; lids = eyelids; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = GetPixelAdjustedRect();
            Color solid = new Color(0f, 0f, 0f, alpha), clear = new Color(0f, 0f, 0f, 0f);
            float height = rect.height * coverage * (lids ? .5f : 1f), feather = Mathf.Min(80f, rect.height * .08f);
            if (top || lids)
            {
                Quad(mesh, rect.xMin, rect.yMax - height, rect.xMax, rect.yMax, solid, solid);
                Quad(mesh, rect.xMin, rect.yMax - height - feather, rect.xMax, rect.yMax - height, clear, solid);
            }
            if (!top || lids)
            {
                Quad(mesh, rect.xMin, rect.yMin, rect.xMax, rect.yMin + height, solid, solid);
                Quad(mesh, rect.xMin, rect.yMin + height, rect.xMax, rect.yMin + height + feather, solid, clear);
            }
        }
        internal static void Quad(VertexHelper mesh, float x0, float y0, float x1, float y1, Color lower, Color upper)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x0, y0), lower, Vector2.zero); mesh.AddVert(new Vector3(x1, y0), lower, Vector2.right);
            mesh.AddVert(new Vector3(x1, y1), upper, Vector2.one); mesh.AddVert(new Vector3(x0, y1), upper, Vector2.up);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }

    [HarmonyPatch(typeof(NightVision), "Toggle")]
    internal static class NightVisionManualTransitionPatch
    {
        private static bool allow;
        private static readonly System.Reflection.FieldInfo Selected = AccessTools.Field(typeof(NightVision), "nightVisSelected");
        private static readonly System.Reflection.MethodInfo Blocked = AccessTools.Method(typeof(NightVision), "BlockToggle");
        private static bool Prefix()
        {
            if (allow || !Settings.Active || !NightVisionEffects.Transition.Value || NightVision.i == null || Selected == null || Blocked == null) return true;
            if ((bool)Blocked.Invoke(NightVision.i, null)) return true;
            if (NightVisionOverlay.Busy) return false;
            var native = NightVision.i;
            bool wasSelected = (bool)Selected.GetValue(native);
            NightVisionOverlay.Begin(!wasSelected, native, () =>
            {
                // Do not turn a deferred keypress into activation on a different
                // camera/owner, or flip again after another mod/user changed state.
                if (native == null || NightVision.i != native || (bool)Selected.GetValue(native) != wasSelected) return;
                try { allow = true; NightVision.Toggle(); } finally { allow = false; }
            });
            return false;
        }
    }
}
