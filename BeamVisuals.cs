using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LaserVision
{
    /// <summary>Procedural graphics; face clearance and trails never alter damage rays.</summary>
    public sealed class BeamVisuals : IDisposable
    {
        const int HistoryCount = 24, FanRows = 32, FanBands = 6, MarkCount = 256, WavePoints = 28;
        readonly Transform head;
        readonly GameObject root;
        readonly Material solid, additive, scorchMaterial, heatMaterial, fanMaterial;
        readonly Texture2D beamTexture, markTexture;
        readonly LineRenderer[] cores = new LineRenderer[2], halos = new LineRenderer[2], outerHalos = new LineRenderer[2];
        readonly LineRenderer[] tendrils = new LineRenderer[6], flares = new LineRenderer[6], sparks = new LineRenderer[24];
        readonly LineRenderer[] rings = new LineRenderer[4], impactRings = new LineRenderer[2];
        readonly Vector3[] wavePoints = new Vector3[WavePoints];
        readonly History[] history = new History[HistoryCount];
        readonly Mark[] marks = new Mark[MarkCount];
        readonly RaycastHit[] rayHits = new RaycastHit[64];
        readonly Fan[] fans = new Fan[2];
        readonly Mesh markMesh, heatMesh;
        readonly Transform markTransform, heatTransform;
        readonly Vector3[] markVertices = new Vector3[MarkCount * 4];
        readonly Color[] markColors = new Color[MarkCount * 4], heatColors = new Color[MarkCount * 4];
        readonly Transform[] lastSurface = new Transform[2];
        readonly Vector3[] lastLocalPoint = new Vector3[2], lastNormal = new Vector3[2];
        readonly float[] lastScorchTime = new float[2];
        readonly int[] lastMark = { -1, -1 };
        int historyCursor, markCursor;
        bool firing, disposed, overdrive;
        float charge, trailDuration = 0.10f, trailStrength = 0.7f, lastMovingSpeed, lastMotionTime = -100f;
        public float FaceClearance = 0.25f;

        struct History
        {
            public Vector3 leftOrigin, rightOrigin, leftEnd, rightEnd;
            public float time;
            public bool valid;
        }
        struct Mark
        {
            public Transform surface;
            public Vector3 a, b, normal;
            public float width, created, life;
        }
        sealed class Fan
        {
            public Mesh mesh;
            public Transform transform;
            public MeshRenderer renderer;
            // Two soft skins supply real thickness: an eye-origin plane alone
            // is edge-on to its own VR camera and almost invisible in first person.
            public readonly Vector3[] vertices = new Vector3[FanRows * FanBands * 2];
            public readonly Color[] colors = new Color[FanRows * FanBands * 2];
            public readonly Vector3[] directions = new Vector3[FanRows];
            public readonly float[] lengths = new float[FanRows], ages = new float[FanRows];
        }

        public BeamVisuals(Transform head)
        {
            this.head = head; root = new GameObject("PrismLaserVision_Graphics");
            try
            {
                if (head) root.transform.SetParent(head, false);
                beamTexture = MakeBeamTexture(); markTexture = MakeMarkTexture();
                Shader shader = FindShader();
                if (!shader) throw new InvalidOperationException("No supported transparent shader; refusing an invisible laser.");
                solid = MakeMaterial(shader, beamTexture, false, "Prism_Core", 3020);
                additive = MakeMaterial(shader, beamTexture, true, "Prism_Energy", 3010);
                fanMaterial = MakeMaterial(shader, beamTexture, true, "Prism_SweepFan", 3005);
                scorchMaterial = MakeMaterial(shader, markTexture, false, "Prism_Sear", 2995);
                heatMaterial = MakeMaterial(shader, markTexture, true, "Prism_Heat", 3000);
                for (int eye = 0; eye < 2; eye++)
                {
                    outerHalos[eye] = MakeLine("OuterCorona", additive);
                    halos[eye] = MakeLine("Halo", additive); cores[eye] = MakeLine("Core", solid);
                    impactRings[eye] = MakeLine("ImpactShockRing", additive, 40, true);
                    for (int j = 0; j < 3; j++)
                    {
                        tendrils[eye * 3 + j] = MakeLine("OverdriveTendril", additive, WavePoints);
                        flares[eye * 3 + j] = MakeLine("ContactStar", additive);
                    }
                    for (int j = 0; j < 12; j++) sparks[eye * 12 + j] = MakeLine("ContactSpark", additive);
                    rings[eye * 2] = MakeLine("ChargeRing", additive, 48, true);
                    rings[eye * 2 + 1] = MakeLine("ChargeWavefront", additive, 48, true);
                    fans[eye] = MakeFan(eye);
                }
                Vector2[] uv = new Vector2[MarkCount * 4]; int[] triangles = new int[MarkCount * 6];
                for (int i = 0; i < MarkCount; i++)
                {
                    int v = i * 4, t = i * 6;
                    uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(1, 0);
                    uv[v + 2] = new Vector2(0, 1); uv[v + 3] = new Vector2(1, 1);
                    triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                    triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                }
                MeshRenderer renderer;
                markMesh = MakeMesh("ScorchedSurfaces", scorchMaterial, out markTransform, out renderer);
                InitializeMesh(markMesh, markVertices, markColors, uv, triangles);
                heatMesh = MakeMesh("ResidualHeat", heatMaterial, out heatTransform, out renderer);
                InitializeMesh(heatMesh, markVertices, heatColors, uv, triangles);
                SetFiring(false);
            }
            catch { Dispose(); throw; }
        }

        public void SetPower(float charge01, bool overdrive)
        { charge = Mathf.Clamp01(charge01); this.overdrive = overdrive; }
        public void SetTrail(float duration, float strength)
        { trailDuration = Mathf.Clamp(duration, 0.025f, 0.18f); trailStrength = Mathf.Clamp(strength, 0, 2f); }

        static Shader FindShader()
        {
            // Vertex colours are needed for fan and merged scorch fading.
            string[] names = { "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit", "Sprites/Default" };
            foreach (string name in names) { Shader s = Shader.Find(name); if (s && s.isSupported) return s; }
            return null;
        }
        static Material MakeMaterial(Shader shader, Texture texture, bool add, string name, int queue)
        {
            Material m = new Material(shader) { name = name, renderQueue = queue };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            SetFloat(m, "_Surface", 1); SetFloat(m, "_Blend", add ? 2 : 0);
            SetFloat(m, "_Mode", add ? 4 : 2); SetFloat(m, "_ColorMode", 0);
            SetFloat(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloat(m, "_DstBlend", (float)(add ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            SetFloat(m, "_ZWrite", 0); SetFloat(m, "_Cull", 0); SetFloat(m, "_AlphaClip", 0);
            m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_ALPHABLEND_ON"); return m;
        }
        static void SetFloat(Material m, string property, float value)
        { if (m.HasProperty(property)) m.SetFloat(property, value); }

        LineRenderer MakeLine(string name, Material material, int points = 2, bool loop = false)
        {
            GameObject go = new GameObject(name); go.transform.SetParent(root.transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material;
            line.useWorldSpace = true; line.positionCount = points; line.loop = loop;
            line.alignment = LineAlignment.View; line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = loop ? 0 : 3; line.numCornerVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off; line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.enabled = false; return line;
        }
        Mesh MakeMesh(string name, Material material, out Transform transform, out MeshRenderer renderer)
        {
            GameObject go = new GameObject(name); transform = go.transform;
            transform.SetParent(root.transform, false); SetWorldIdentity(transform);
            MeshFilter filter = go.AddComponent<MeshFilter>(); renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false; renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Mesh mesh = new Mesh { name = name }; mesh.MarkDynamic(); filter.sharedMesh = mesh; return mesh;
        }
        static void InitializeMesh(Mesh mesh, Vector3[] vertices, Color[] colors, Vector2[] uv, int[] triangles)
        { mesh.vertices = vertices; mesh.colors = colors; mesh.uv = uv; mesh.triangles = triangles; }
        Fan MakeFan(int eye)
        {
            Fan fan = new Fan(); fan.mesh = MakeMesh("SweepFan_" + eye, fanMaterial, out fan.transform, out fan.renderer);
            Vector2[] uv = new Vector2[FanRows * FanBands * 2]; int[] triangles = new int[(FanRows - 1) * (FanBands - 1) * 12];
            for (int skin = 0; skin < 2; skin++)
            for (int row = 0; row < FanRows; row++) for (int band = 0; band < FanBands; band++)
                uv[skin * FanRows * FanBands + row * FanBands + band] = new Vector2(band / (float)(FanBands - 1), 0.5f);
            int t = 0;
            for (int skin = 0; skin < 2; skin++)
            for (int row = 0; row < FanRows - 1; row++) for (int band = 0; band < FanBands - 1; band++)
            {
                int v = skin * FanRows * FanBands + row * FanBands + band;
                triangles[t++] = v; triangles[t++] = v + FanBands; triangles[t++] = v + 1;
                triangles[t++] = v + 1; triangles[t++] = v + FanBands; triangles[t++] = v + FanBands + 1;
            }
            InitializeMesh(fan.mesh, fan.vertices, fan.colors, uv, triangles); fan.renderer.enabled = false; return fan;
        }
        static void SetWorldIdentity(Transform t)
        {
            if (!t) return;
            t.position = Vector3.zero; t.rotation = Quaternion.identity;
            Vector3 s = t.parent ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(Mathf.Abs(s.x) > 0.0001f ? 1 / s.x : 1,
                Mathf.Abs(s.y) > 0.0001f ? 1 / s.y : 1, Mathf.Abs(s.z) > 0.0001f ? 1 / s.z : 1);
        }
        static void Configure(LineRenderer line, float width, Color color, bool show)
        {
            if (!line) return; line.enabled = show; if (!show) return;
            line.startWidth = line.endWidth = width; line.startColor = line.endColor = color;
        }
        static void Line(LineRenderer line, Vector3 a, Vector3 b, float width, Color color, bool show)
        { Configure(line, width, color, show); if (show && line) { line.SetPosition(0, a); line.SetPosition(1, b); } }
        public void SetFiring(bool active)
        {
            if (disposed) return;
            if (!active)
            {
                Array.Clear(history, 0, history.Length); Array.Clear(lastSurface, 0, lastSurface.Length);
                lastMovingSpeed = 0; lastMotionTime = -100f;
                Hide(cores); Hide(halos); Hide(outerHalos); Hide(tendrils); Hide(flares); Hide(sparks); Hide(rings); Hide(impactRings);
                foreach (Fan fan in fans) if (fan != null && fan.renderer) fan.renderer.enabled = false;
            }
            firing = active;
        }
        static void Hide(LineRenderer[] lines)
        { foreach (LineRenderer line in lines) if (line) line.enabled = false; }

        public void Draw(Vector3 leftOrigin, Vector3 leftEnd, bool leftHit, Vector3 leftNormal,
            Vector3 rightOrigin, Vector3 rightEnd, bool rightHit, Vector3 rightNormal,
            Color core, Color glow, float width, float glowStrength, bool smear, float angularSpeed)
        {
            if (disposed || !solid || !firing) return;
            width = Mathf.Clamp(width, 0.002f, 0.30f); glowStrength = Mathf.Clamp(glowStrength, 0.1f, 5f);
            core.a = 1; glow.a = Mathf.Clamp01(0.30f + glowStrength * 0.18f);
            Color energy = glow * glowStrength; energy.a = glow.a;
            DrawEye(0, leftOrigin, leftEnd, leftHit, leftNormal, core, energy, width);
            DrawEye(1, rightOrigin, rightEnd, rightHit, rightNormal, core, energy, width);
            History previous = history[(historyCursor + HistoryCount - 1) % HistoryCount];
            if (previous.valid && (Vector3.Distance(previous.leftOrigin, leftOrigin) > 0.25f ||
                Vector3.Angle(previous.leftEnd - previous.leftOrigin, leftEnd - leftOrigin) > 40f))
                Array.Clear(history, 0, history.Length);
            history[historyCursor] = new History { leftOrigin = leftOrigin, leftEnd = leftEnd,
                rightOrigin = rightOrigin, rightEnd = rightEnd, time = Time.unscaledTime, valid = true };
            historyCursor = (historyCursor + 1) % HistoryCount;
            if (angularSpeed > 25f) { lastMovingSpeed = angularSpeed; lastMotionTime = Time.unscaledTime; }
            float motionFade = Mathf.Clamp01(1f - (Time.unscaledTime - lastMotionTime) / trailDuration);
            float visualSpeed = 25f + Mathf.Max(0, lastMovingSpeed - 25f) * motionFade;
            bool show = smear && trailStrength > 0 && motionFade > 0;
            BuildFan(0, leftOrigin, leftEnd, Color.Lerp(energy, core, 0.16f), show, visualSpeed, width);
            BuildFan(1, rightOrigin, rightEnd, Color.Lerp(energy, core, 0.16f), show, visualSpeed, width);
        }
        void DrawEye(int eye, Vector3 origin, Vector3 end, bool hit, Vector3 normal, Color core, Color energy, float width)
        {
            Vector3 delta = end - origin; float distance = delta.magnitude;
            Vector3 dir = distance > 0.001f ? delta / distance : (head ? head.forward : Vector3.forward);
            float clearance = Mathf.Clamp(FaceClearance, 0, 1.5f); bool visible = distance > clearance + 0.005f;
            Vector3 start = origin + dir * Mathf.Min(clearance, distance);
            float power = overdrive ? charge : 0f;
            float pulse = 1f + (0.025f + power * 0.1f) * Mathf.Sin(Time.time * (37f + eye * 2f));
            Color outer = energy; outer.a *= overdrive ? 0.17f + power * 0.12f : 0.12f;
            Line(outerHalos[eye], start, end, width * (overdrive ? 8f : 6f), outer, visible);
            Line(halos[eye], start, end, width * (overdrive ? 4.2f : 3.6f) * pulse, energy, visible);
            Configure(cores[eye], width * pulse, core, visible); cores[eye].positionCount = overdrive ? WavePoints : 2;
            if (visible && !overdrive) { cores[eye].SetPosition(0, start); cores[eye].SetPosition(1, end); }
            Vector3 side = head ? head.right : Vector3.right; side = Vector3.ProjectOnPlane(side, dir).normalized;
            if (side.sqrMagnitude < 0.1f) side = Vector3.Cross(dir, Vector3.up).normalized;
            Vector3 up = Vector3.Cross(dir, side).normalized;
            if (visible && overdrive)
            {
                for (int i = 0; i < WavePoints; i++)
                {
                    float t = i / (float)(WavePoints - 1), envelope = Mathf.Sin(t * Mathf.PI);
                    float phase = t * 25f - Time.time * 31f + eye * 2.3f;
                    Vector3 offset = (side * Mathf.Sin(phase) + up * Mathf.Cos(phase * 1.17f)) * width * 0.12f * envelope * (0.3f + power);
                    wavePoints[i] = Vector3.Lerp(start, end, t) + offset;
                }
                cores[eye].SetPositions(wavePoints);
            }
            for (int filament = 0; filament < 3; filament++)
            {
                LineRenderer line = tendrils[eye * 3 + filament];
                Color c = Color.Lerp(energy, core, filament == 0 ? 0.55f : 0.1f); c.a *= 0.55f + 0.3f * power;
                Configure(line, Mathf.Max(0.002f, width * (0.14f + filament * 0.035f)), c, visible && overdrive);
                if (!visible || !overdrive) continue;
                for (int i = 0; i < WavePoints; i++)
                {
                    float t = i / (float)(WavePoints - 1), envelope = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.65f);
                    float phase = t * (24f + filament * 8f) - Time.time * (28f + filament * 9f) + filament * 2.1f + eye;
                    float radius = width * (0.7f + power * 0.9f) * envelope;
                    wavePoints[i] = Vector3.Lerp(start, end, t) + (side * Mathf.Sin(phase) + up * Mathf.Cos(phase * 1.11f)) * radius;
                }
                line.SetPositions(wavePoints);
            }
            DrawChargeRings(eye, start, end, side, up, width, energy, visible && (overdrive || charge > 0.01f));
            Contact(eye, end, normal, hit && distance > clearance * 0.7f, width, core, energy);
        }
        void DrawChargeRings(int eye, Vector3 start, Vector3 end, Vector3 side, Vector3 up, float width, Color color, bool show)
        {
            float distance = Vector3.Distance(start, end);
            for (int r = 0; r < 2; r++)
            {
                float progress = Mathf.Repeat(Time.time * (2.5f + charge * 2f) + r * 0.5f, 1f);
                Vector3 center = Vector3.Lerp(start, end, Mathf.Min(0.14f, 0.25f / Mathf.Max(distance, 0.1f)) * (r + progress));
                float radius = (0.025f + charge * 0.055f + width * 0.7f) * (0.7f + progress * 0.8f);
                Color c = color; c.a *= (0.35f + charge * 0.65f) * (1f - progress);
                LineRenderer line = rings[eye * 2 + r]; Configure(line, 0.003f + width * 0.055f, c, show);
                if (!show) continue;
                for (int i = 0; i < 48; i++)
                {
                    float angle = i / 48f * Mathf.PI * 2f, ripple = 1f + 0.06f * Mathf.Sin(angle * 7f - Time.time * 18f);
                    line.SetPosition(i, center + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius * ripple);
                }
            }
        }
        void Contact(int eye, Vector3 point, Vector3 normal, bool hit, float width, Color core, Color energy)
        {
            Vector3 viewRight = head ? head.right : Vector3.right, viewUp = head ? head.up : Vector3.up;
            Vector3 center = point + normal * 0.006f;
            float strength = overdrive ? 1.5f + charge * 1.5f : 1f;
            float flicker = 0.8f + 0.2f * Mathf.Sin(Time.time * 73f + eye * 3f);
            float size = Mathf.Max(width * 2.4f, 0.025f) * strength * flicker;
            Color whiteHot = Color.Lerp(energy, new Color(2, 1.8f, 1.6f, 1), 0.4f);
            for (int j = 0; j < 3; j++)
            {
                float angle = j * Mathf.PI / 3f + Time.time * (overdrive ? 7f : 2f);
                Vector3 axis = viewRight * Mathf.Cos(angle) + viewUp * Mathf.Sin(angle);
                Color c = j == 0 ? whiteHot : energy; c.a *= j == 0 ? 0.9f : 0.6f;
                Line(flares[eye * 3 + j], center - axis * size, center + axis * size, size * (j == 0 ? 0.55f : 0.26f), c, hit);
            }
            Vector3 tangent = Vector3.Cross(normal, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.1f) tangent = Vector3.Cross(normal, Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            for (int j = 0; j < 12; j++)
            {
                float freq = overdrive ? 9f : 6f, cycle = Mathf.Repeat(Time.time * freq + j * 0.618034f, 1f);
                float generation = Mathf.Floor(Time.time * freq + j * 0.618034f), angle = j * 2.399963f + generation * 1.731f;
                Vector3 dir = (normal * (0.4f + 0.3f * Mathf.Sin(j + generation)) + tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)).normalized;
                float reach = (0.075f + 0.025f * (j % 4)) * strength;
                Vector3 start = center + dir * reach * cycle + Vector3.down * cycle * cycle * 0.035f;
                Color c = Color.Lerp(new Color(1.8f, 0.45f, 0.06f, 1), energy, 0.45f);
                c = Color.Lerp(c, whiteHot, 1f - cycle); c.a = (1f - cycle) * 0.9f;
                Line(sparks[eye * 12 + j], start, start + dir * (0.012f + width * 0.15f) * (1.4f - cycle),
                    Mathf.Max(0.0012f, width * 0.08f) * (1.1f - cycle * 0.6f), c, hit);
            }
            float ringTime = Mathf.Repeat(Time.time * (overdrive ? 5f : 3f) + eye * 0.35f, 1f);
            Color ringColor = energy; ringColor.a *= (1f - ringTime) * (overdrive ? 0.65f : 0.2f);
            LineRenderer ring = impactRings[eye]; Configure(ring, Mathf.Max(0.002f, width * 0.1f), ringColor, hit);
            if (hit) for (int i = 0; i < 40; i++)
            {
                float angle = i / 40f * Mathf.PI * 2f;
                ring.SetPosition(i, center + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * size * (0.4f + ringTime));
            }
        }

        void BuildFan(int eye, Vector3 origin, Vector3 currentEnd, Color color, bool show, float angularSpeed, float width)
        {
            Fan fan = fans[eye]; if (fan == null) return;
            float clearance = Mathf.Clamp(FaceClearance, 0, 1.5f), currentLength = Vector3.Distance(origin, currentEnd);
            if (!show || currentLength <= clearance + 0.01f) { fan.renderer.enabled = false; return; }
            int rows = 0; float now = Time.unscaledTime;
            Vector3 newerDirection = (currentEnd - origin).normalized; float newerAge = 0, newerLength = currentLength;
            fan.directions[rows] = newerDirection; fan.ages[rows] = 0; fan.lengths[rows++] = currentLength;
            for (int index = 1; index < HistoryCount && rows < FanRows; index++)
            {
                History h = history[(historyCursor + HistoryCount - 1 - index) % HistoryCount];
                if (!h.valid) break;
                float age = now - h.time; if (age > trailDuration || age <= newerAge) break;
                Vector3 oldOrigin = eye == 0 ? h.leftOrigin : h.rightOrigin, oldEnd = eye == 0 ? h.leftEnd : h.rightEnd;
                Vector3 oldDelta = oldEnd - oldOrigin; if (oldDelta.sqrMagnitude < 0.0001f) break;
                Vector3 oldDirection = oldDelta.normalized; float angle = Vector3.Angle(newerDirection, oldDirection);
                if (angle > 30f) break;
                int steps = Mathf.Clamp(Mathf.CeilToInt(angle / 1.5f), 1, 8);
                for (int step = 1; step <= steps && rows < FanRows; step++)
                {
                    float t = step / (float)steps;
                    Vector3 direction = Vector3.Slerp(newerDirection, oldDirection, t).normalized;
                    float length = Mathf.Min(currentLength, Mathf.Lerp(newerLength, oldDelta.magnitude, t));
                    fan.directions[rows] = direction; fan.ages[rows] = Mathf.Lerp(newerAge, age, t);
                    fan.lengths[rows] = ClipVisualRay(origin, direction, length); rows++;
                }
                newerDirection = oldDirection; newerAge = age; newerLength = oldDelta.magnitude;
            }
            if (rows < 2) { fan.renderer.enabled = false; return; }
            float motion = Mathf.Clamp01((angularSpeed - 25f) / 180f) * trailStrength;
            Vector3 thicknessAxis = Vector3.Cross(fan.directions[0], fan.directions[rows - 1]).normalized;
            if (thicknessAxis.sqrMagnitude < 0.1f) thicknessAxis = head ? head.up : Vector3.up;
            for (int row = 0; row < FanRows; row++)
            {
                bool valid = row < rows; int r = Mathf.Min(row, rows - 1);
                // Shorten both triangle edges to the nearest adjacent occluder.
                float length = fan.lengths[r];
                if (r > 0) length = Mathf.Min(length, fan.lengths[r - 1]);
                if (r + 1 < rows) length = Mathf.Min(length, fan.lengths[r + 1]);
                float ageFade = Mathf.Clamp01(1f - fan.ages[r] / trailDuration); ageFade *= ageFade;
                float tailFade = r == rows - 1 ? 0 : 1f;
                float alpha = color.a * motion * ageFade * tailFade * (overdrive ? 0.36f : 0.26f);
                for (int band = 0; band < FanBands; band++)
                {
                    float t = band / (float)(FanBands - 1);
                    Vector3 position = origin + fan.directions[r] * Mathf.Lerp(Mathf.Min(clearance, length), length, t);
                    float thickness = Mathf.Max(width * 0.85f, 0.006f) * (0.3f + 0.7f * t);
                    float radial = Mathf.Sin(t * Mathf.PI * 0.85f) * Mathf.Clamp01(t * 8f);
                    Color c = color; c.a = valid && length > clearance ? alpha * radial : 0;
                    for (int skin = 0; skin < 2; skin++)
                    {
                        int v = skin * FanRows * FanBands + row * FanBands + band;
                        fan.vertices[v] = position + thicknessAxis * thickness * (skin == 0 ? -1f : 1f);
                        fan.colors[v] = c;
                    }
                }
            }
            SetWorldIdentity(fan.transform); fan.mesh.vertices = fan.vertices; fan.mesh.colors = fan.colors;
            fan.mesh.RecalculateBounds(); fan.renderer.enabled = true;
        }
        float ClipVisualRay(Vector3 origin, Vector3 direction, float length)
        {
            int mask = ~LayerMask.GetMask("TouchObject", "Zone", "LightProbeVolume", "BodyLocomotion", "Ignore Raycast", "PlayerLocomotion");
            int count = Physics.RaycastNonAlloc(origin, direction, rayHits, length, mask, QueryTriggerInteraction.Ignore);
            if (count >= rayHits.Length) return 0;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = rayHits[i]; ThunderRoad.Creature creature = h.collider ? h.collider.GetComponentInParent<ThunderRoad.Creature>() : null;
                if (creature && creature.isPlayer) continue; length = Mathf.Min(length, h.distance);
            }
            return length;
        }

        public void AddScorch(RaycastHit hit, float width, float lifetime, int channel = 0)
        {
            if (disposed || !scorchMaterial || !hit.collider || lifetime <= 0) return;
            channel = Mathf.Clamp(channel, 0, 1); Transform surface = hit.collider.transform; float now = Time.time;
            float markWidth = Mathf.Clamp(width * 1.9f, 0.010f, 0.35f);
            Vector3 point = hit.point, a = point, normal = hit.normal;
            bool connected = lastSurface[channel] == surface && now - lastScorchTime[channel] < 0.12f;
            Vector3 old = connected ? surface.TransformPoint(lastLocalPoint[channel]) : point;
            connected = connected && Vector3.Dot(lastNormal[channel], normal) > 0.88f && Vector3.Distance(old, point) < Mathf.Max(0.22f, markWidth * 7f);
            if (connected && Vector3.Distance(old, point) < Mathf.Max(0.003f, markWidth * 0.2f) && lastMark[channel] >= 0)
            {
                int i = lastMark[channel]; Mark existing = marks[i];
                if (existing.surface == surface && now - existing.created < 0.3f)
                {
                    existing.created = now; existing.life = lifetime; existing.width = Mathf.Max(existing.width, markWidth);
                    marks[i] = existing; lastScorchTime[channel] = now; return;
                }
            }
            Vector3 tangent;
            if (connected) { a = old; tangent = (point - a).normalized; }
            else
            {
                tangent = Vector3.Cross(normal, head ? head.right : Vector3.right).normalized;
                if (tangent.sqrMagnitude < 0.1f) tangent = Vector3.Cross(normal, Vector3.up).normalized;
                a -= tangent * markWidth * 0.15f;
            }
            a -= tangent * markWidth * 0.35f; point += tangent * markWidth * 0.35f;
            marks[markCursor] = new Mark { surface = surface,
                a = surface.InverseTransformPoint(a + normal * 0.0025f), b = surface.InverseTransformPoint(point + normal * 0.0025f),
                normal = surface.InverseTransformDirection(normal), width = markWidth, created = now, life = Mathf.Clamp(lifetime, 0.05f, 120f) };
            lastMark[channel] = markCursor; markCursor = (markCursor + 1) % MarkCount;
            lastSurface[channel] = surface; lastLocalPoint[channel] = surface.InverseTransformPoint(hit.point);
            lastNormal[channel] = normal; lastScorchTime[channel] = now;
        }
        public void Tick()
        {
            if (disposed || !markMesh) return;
            SetWorldIdentity(markTransform); SetWorldIdentity(heatTransform); float now = Time.time;
            for (int i = 0; i < MarkCount; i++)
            {
                Mark m = marks[i]; int v = i * 4;
                if (!m.surface || !m.surface.gameObject.activeInHierarchy || now - m.created >= m.life)
                {
                    for (int k = 0; k < 4; k++) { markVertices[v + k] = Vector3.zero; markColors[v + k] = heatColors[v + k] = Color.clear; }
                    continue;
                }
                Vector3 a = m.surface.TransformPoint(m.a), b = m.surface.TransformPoint(m.b);
                Vector3 normal = m.surface.TransformDirection(m.normal).normalized, side = Vector3.Cross(normal, b - a).normalized * m.width * 0.5f;
                markVertices[v] = a - side; markVertices[v + 1] = a + side; markVertices[v + 2] = b - side; markVertices[v + 3] = b + side;
                float age = now - m.created, fade = Mathf.Clamp01((m.life - age) / Mathf.Min(2f, m.life)), hot = Mathf.Exp(-age * 2.8f) * fade;
                for (int k = 0; k < 4; k++)
                {
                    markColors[v + k] = new Color(0.018f, 0.006f, 0.004f, fade * 0.92f);
                    heatColors[v + k] = new Color(2f, 0.2f, 0.008f, hot * 0.8f);
                }
            }
            markMesh.vertices = markVertices; markMesh.colors = markColors; markMesh.RecalculateBounds();
            heatMesh.vertices = markVertices; heatMesh.colors = heatColors; heatMesh.RecalculateBounds();
        }
        static Texture2D MakeBeamTexture()
        {
            Texture2D t = new Texture2D(8, 64, TextureFormat.RGBA32, false) { name = "Prism_SoftBeam", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Color[] p = new Color[8 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 8; x++)
            { float edge = Mathf.Abs((y + 0.5f) / 32f - 1f); p[y * 8 + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Max(0, 1 - edge), 1.8f)); }
            t.SetPixels(p); t.Apply(false, true); return t;
        }
        static Texture2D MakeMarkTexture()
        {
            Texture2D t = new Texture2D(64, 32, TextureFormat.RGBA32, false) { name = "Prism_SearedStroke", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Color[] p = new Color[64 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 64; x++)
            {
                float across = Mathf.Abs((x + 0.5f) / 32f - 1f), along = Mathf.Abs((y + 0.5f) / 16f - 1f);
                float rounded = Mathf.Clamp01((1f - across * across - Mathf.Pow(along, 8)) * 4f);
                float grain = 0.86f + 0.14f * Mathf.Sin(x * 16.31f + y * 12.77f) * Mathf.Sin(x * 4.9f - y * 7.1f);
                p[y * 64 + x] = new Color(1, 1, 1, rounded * grain);
            }
            t.SetPixels(p); t.Apply(false, true); return t;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; if (root) Object.Destroy(root);
            if (solid) Object.Destroy(solid); if (additive) Object.Destroy(additive);
            if (scorchMaterial) Object.Destroy(scorchMaterial); if (heatMaterial) Object.Destroy(heatMaterial); if (fanMaterial) Object.Destroy(fanMaterial);
            if (beamTexture) Object.Destroy(beamTexture); if (markTexture) Object.Destroy(markTexture);
            if (markMesh) Object.Destroy(markMesh); if (heatMesh) Object.Destroy(heatMesh);
            foreach (Fan fan in fans) if (fan != null && fan.mesh) Object.Destroy(fan.mesh);
        }
    }
}
