using System;
using System.Collections.Generic;
using System.Reflection;
using ThunderRoad;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using BlendMode = UnityEngine.Rendering.BlendMode;

namespace LaserVision
{
    // A wheel-local glyph: it adds presentation beneath the existing spell Orb
    // without replacing the Orb, its linked spell, or the wheel's input handling.
    public sealed class ChosenSpellIcon : IDisposable
    {
        private const string SpellId = "PrismLaserVision";
        private const string IconName = "ChosenLaserVisionIcon";
        private static readonly FieldInfo OrbsField = typeof(WheelMenu).GetField("orbs", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly List<GameObject> liveIcons = new List<GameObject>();
        private Mesh whiteMesh, redMesh;
        private Material whiteMaterial, redMaterial;
        private bool disposed, loggedFailure;
        private float nextTick;

        public ChosenSpellIcon()
        {
            EventManager.OnPlayerOpenedSpellWheel += OnWheelOpened;
        }

        public void Tick()
        {
            if (disposed || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.08f;
            OnWheelOpened();
        }

        private void OnWheelOpened()
        {
            if (disposed) return;
            try
            {
                Apply(WheelMenuSpell.left);
                Apply(WheelMenuSpell.right);
                for (int i = liveIcons.Count - 1; i >= 0; i--)
                    if (!liveIcons[i]) liveIcons.RemoveAt(i);
            }
            catch (Exception ex)
            {
                if (!loggedFailure)
                {
                    loggedFailure = true;
                    Debug.LogWarning("[LaserVision] Custom wheel icon unavailable: " + ex.Message);
                }
            }
        }

        private void Apply(WheelMenuSpell wheel)
        {
            if (!wheel || !wheel.isShown || OrbsField == null) return;
            List<WheelMenu.Orb> orbs = OrbsField.GetValue(wheel) as List<WheelMenu.Orb>;
            if (orbs == null) return;
            for (int i = 0; i < orbs.Count; i++)
            {
                WheelMenu.Orb orb = orbs[i];
                if (orb == null || orb.id != SpellId || !orb.transform) continue;
                Transform existing = orb.transform.Find(IconName);
                if (!existing)
                {
                    if (!EnsureAssets()) return;
                    GameObject icon = new GameObject(IconName);
                    icon.layer = wheel.layer;
                    icon.transform.SetParent(orb.transform, false);
                    icon.transform.localPosition = new Vector3(0f, 0f, -0.003f);
                    icon.transform.localRotation = Quaternion.identity;
                    icon.transform.localScale = Vector3.one * 0.027f;
                    AddMesh(icon.transform, "Figure", whiteMesh, whiteMaterial, wheel.layer);
                    AddMesh(icon.transform, "EyeRaysAndRing", redMesh, redMaterial, wheel.layer);
                    liveIcons.Add(icon);
                    existing = icon.transform;
                }
                // Preserve the wheel's normal selection logic; highlight visually only.
                float scale = wheel.selectedOrb == orb ? 0.0305f : 0.027f;
                existing.localScale = Vector3.one * scale;
            }
        }

        private bool EnsureAssets()
        {
            if (whiteMesh && redMesh && whiteMaterial && redMaterial) return true;
            Shader shader = null;
            string[] names = { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default", "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit" };
            for (int i = 0; i < names.Length; i++)
            {
                shader = Shader.Find(names[i]);
                if (shader && shader.isSupported) break;
                shader = null;
            }
            if (!shader)
            {
                if (!loggedFailure) Debug.LogWarning("[LaserVision] No supported glyph shader; wheel selection remains available.");
                loggedFailure = true;
                return false;
            }
            if (!whiteMaterial) whiteMaterial = MakeMaterial(shader, Color.white, "ChosenIconWhite");
            if (!redMaterial) redMaterial = MakeMaterial(shader, new Color(1f, 0.025f, 0.015f, 1f), "ChosenIconRed");
            if (!whiteMesh)
            {
                GlyphBuilder white = new GlyphBuilder();
                // Head upper-right, forward-leaning torso and bent arms/legs.
                white.Ring(new Vector2(0.26f, 0.35f), 0.195f, 0.055f, 28);
                white.Stroke(new Vector2(0.25f, 0.145f), new Vector2(0.04f, -0.20f), 0.065f);
                white.Stroke(new Vector2(0.18f, 0.025f), new Vector2(-0.12f, -0.035f), 0.060f);
                white.Stroke(new Vector2(-0.12f, -0.035f), new Vector2(-0.22f, 0.16f), 0.060f);
                white.Stroke(new Vector2(0.17f, 0.015f), new Vector2(0.39f, -0.13f), 0.060f);
                white.Stroke(new Vector2(0.39f, -0.13f), new Vector2(0.48f, 0.015f), 0.060f);
                white.Stroke(new Vector2(0.04f, -0.20f), new Vector2(-0.27f, -0.38f), 0.065f);
                white.Stroke(new Vector2(-0.27f, -0.38f), new Vector2(-0.44f, -0.66f), 0.065f);
                white.Stroke(new Vector2(0.04f, -0.20f), new Vector2(0.25f, -0.44f), 0.065f);
                white.Stroke(new Vector2(0.25f, -0.44f), new Vector2(0.55f, -0.53f), 0.065f);
                whiteMesh = white.Build("ChosenIconStickFigure");
            }
            if (!redMesh)
            {
                GlyphBuilder red = new GlyphBuilder();
                red.Ring(Vector2.zero, 0.94f, 0.035f, 64);
                // Parallel eye rays point left, entirely inside the thin red ring.
                red.Stroke(new Vector2(0.13f, 0.395f), new Vector2(-0.82f, 0.47f), 0.038f);
                red.Stroke(new Vector2(0.12f, 0.305f), new Vector2(-0.86f, 0.365f), 0.038f);
                redMesh = red.Build("ChosenIconRedLasers");
            }
            return true;
        }

        private static Material MakeMaterial(Shader shader, Color color, string name)
        {
            Material material = new Material(shader) { name = name, renderQueue = 3100 };
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            Set(material, "_Cull", 0f);
            Set(material, "_ZWrite", 0f);
            Set(material, "_ZTest", (float)CompareFunction.LessEqual);
            Set(material, "_Surface", 1f);
            Set(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            Set(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        private static void Set(Material material, string property, float value)
        { if (material.HasProperty(property)) material.SetFloat(property, value); }

        private static void AddMesh(Transform parent, string name, Mesh mesh, Material material, int layer)
        {
            GameObject child = new GameObject(name);
            child.layer = layer;
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EventManager.OnPlayerOpenedSpellWheel -= OnWheelOpened;
            for (int i = 0; i < liveIcons.Count; i++) if (liveIcons[i]) Object.Destroy(liveIcons[i]);
            liveIcons.Clear();
            if (whiteMesh) Object.Destroy(whiteMesh);
            if (redMesh) Object.Destroy(redMesh);
            if (whiteMaterial) Object.Destroy(whiteMaterial);
            if (redMaterial) Object.Destroy(redMaterial);
        }

        private sealed class GlyphBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();
            public void Stroke(Vector2 a, Vector2 b, float width)
            {
                Vector2 delta = b - a;
                if (delta.sqrMagnitude < 0.00001f) return;
                Vector2 side = new Vector2(-delta.y, delta.x).normalized * width * 0.5f;
                Quad(a - side, a + side, b - side, b + side);
            }
            public void Ring(Vector2 center, float radius, float width, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments, b = (i + 1) * Mathf.PI * 2f / segments;
                    Vector2 da = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                    Quad(center + da * (radius - width * 0.5f), center + da * (radius + width * 0.5f),
                        center + db * (radius - width * 0.5f), center + db * (radius + width * 0.5f));
                }
            }
            private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                int offset = vertices.Count;
                vertices.Add(new Vector3(a.x, a.y, 0)); vertices.Add(new Vector3(b.x, b.y, 0));
                vertices.Add(new Vector3(c.x, c.y, 0)); vertices.Add(new Vector3(d.x, d.y, 0));
                // Explicit back faces handle viewing either side of the hand wheel
                // even if a fallback shader ignores its culling material property.
                int[] indices = { 0, 2, 1, 1, 2, 3, 1, 2, 0, 3, 2, 1 };
                for (int i = 0; i < indices.Length; i++) triangles.Add(offset + indices[i]);
            }
            public Mesh Build(string name)
            {
                Mesh mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                Color[] colors = new Color[vertices.Count];
                Vector2[] uv = new Vector2[vertices.Count];
                for (int i = 0; i < colors.Length; i++) { colors[i] = Color.white; uv[i] = new Vector2(0.5f, 0.5f); }
                mesh.colors = colors; mesh.uv = uv;
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
