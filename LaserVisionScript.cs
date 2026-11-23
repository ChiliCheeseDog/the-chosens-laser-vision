using System;
using System.Collections.Generic;
using ThunderRoad;
using UnityEngine;

namespace LaserVision
{
    // Uses the same verified ThunderScript/isFiring path as the supplied working spell.
    public sealed class LaserVisionScript : ThunderScript
    {
        public const string SpellId = "PrismLaserVision";
        private BeamSession session;
        private Creature owner;
        private float retryAfter;
        private string modDirectory;
        private readonly BeamPowerState power = new BeamPowerState();
        private ChosenSpellIcon icon;

        public override void ScriptLoaded(ModManager.ModData modData)
        {
            base.ScriptLoaded(modData);
            modDirectory = modData.fullPath;
            Debug.Log("[LaserVision] The Chosen's Laser Vision 0.2.1 public synthesized-audio release loaded.");
        }

        public override void ScriptUpdate()
        {
            if (icon != null) icon.Tick();
            Creature player = Player.currentCreature;
            if (!player || player != owner)
            {
                Cleanup();
                owner = player;
            }
            if (!player || !Player.local || Player.local.head == null || !Player.local.head.cam)
            { Cleanup(); return; }
            Transform head = Player.local.head.cam.transform;
            bool fire = LaserVisionOptions.Enabled && !player.isKilled &&
                (IsCasting(player.handLeft) || IsCasting(player.handRight));
            bool grip = (IsCasting(player.handLeft) && Grip(player.handLeft)) || (IsCasting(player.handRight) && Grip(player.handRight));
            power.Step(fire, grip, LaserVisionOptions.OverdriveEnabled, Time.unscaledDeltaTime);
            SpellCaster caster = IsCasting(player.handRight) ? player.handRight.caster : player.handLeft ? player.handLeft.caster : null;
            try
            {
                if (session == null && fire && Time.unscaledTime >= retryAfter) session = new BeamSession(head, player, modDirectory);
                if (session != null) session.Update(fire, caster, power.Charge, power.Overdrive);
            }
            catch (Exception e)
            {
                Debug.LogError("[LaserVision] Stopped safely: " + e);
                Cleanup();
                retryAfter = Time.unscaledTime + 5f;
            }
        }

        private static bool IsCasting(RagdollHand hand)
        {
            return hand && hand.caster != null && !hand.grabbedHandle &&
                hand.caster.spellInstance != null && hand.caster.spellInstance.id == SpellId &&
                (hand.caster.isFiring || (hand.playerHand != null && hand.playerHand.controlHand != null && hand.playerHand.controlHand.usePressed));
        }
        private static bool Grip(RagdollHand hand) { return hand && hand.playerHand != null && hand.playerHand.controlHand != null && hand.playerHand.controlHand.gripPressed; }
        private void Cleanup() { if (session != null) session.Dispose(); session = null; power.Step(false,false,false,0); }
        public override void ScriptEnable()
        {
            base.ScriptEnable();
            try { if (icon == null) icon = new ChosenSpellIcon(); }
            catch(Exception ex) { Debug.LogWarning("[LaserVision] Optional wheel icon: "+ex.Message); }
        }
        public override void ScriptDisable() { Cleanup(); if(icon != null)icon.Dispose();icon=null; owner = null; base.ScriptDisable(); }
        public override void ScriptUnload() { Cleanup(); if(icon != null)icon.Dispose();icon=null; owner = null; base.ScriptUnload(); }
    }

    internal sealed class BeamSession : IDisposable
    {
        private readonly Transform head;
        private readonly Creature player;
        private readonly BeamVisuals visuals;
        private readonly BeamAudio audio;
        private readonly SkinSear skin;
        private readonly ZandatsuBridge bridge = new ZandatsuBridge();
        private readonly RaycastHit[] hits = new RaycastHit[128];
        private readonly Collider[] originOverlaps = new Collider[32];
        private readonly HashSet<Creature> damaged = new HashSet<Creature>();
        private readonly Dictionary<Creature, float> burns = new Dictionary<Creature, float>();
        private readonly Dictionary<Creature, float> nextImpacts = new Dictionary<Creature, float>();
        private readonly Dictionary<Creature, float> nextPushes = new Dictionary<Creature, float>();
        private readonly Dictionary<int, float> cuts = new Dictionary<int, float>();
        private readonly HashSet<Creature> rayCreatures = new HashSet<Creature>();
        private readonly List<int> expiredCuts = new List<int>();
        private readonly List<Creature> expiredBurns = new List<Creature>();
        private readonly int mask;
        private bool wasFiring, history;
        private Vector3 previousOrigin, previousDirection, previousHit;
        private float nextCutTime, lastMaintenance, lastNoBridgeWarning;
        private int cutBudget;
        private SpellCaster activeCaster;
        private bool overdrive;
        private bool skinWarning;

        private struct BeamHit
        {
            public Vector3 End;
            public RaycastHit Hit;
            public bool Contact;
        }

        public BeamSession(Transform head, Creature player, string modDirectory)
        {
            this.head = head; this.player = player;
            // Do not exclude NPC body/armour layers. Reject player geometry explicitly below.
            mask = ~LayerMask.GetMask("Ignore Raycast", "TouchObject", "Zone", "LightProbeVolume", "BodyLocomotion", "PlayerLocomotion");
            visuals = new BeamVisuals(head);
            try { audio = new BeamAudio(head, modDirectory); }
            catch { visuals.Dispose(); throw; }
            try { skin = new SkinSear(head); }
            catch (Exception ex) { Debug.LogWarning("[LaserVision] Optional skin char unavailable: "+ex.Message); }
        }

        public void Update(bool firing, SpellCaster caster, float charge, bool powered)
        {
            activeCaster = caster; overdrive = powered;
            bridge.Refresh();
            visuals.FaceClearance = LaserVisionOptions.FaceClearance;
            visuals.SetTrail(LaserVisionOptions.TrailDuration, LaserVisionOptions.TrailStrength);
            visuals.SetPower(charge, powered);
            visuals.Tick();
            if (!firing)
            {
                if (wasFiring) visuals.SetFiring(false);
                audio.Update(false, false, 0f, LaserVisionOptions.Volume, 0f, false);
                wasFiring = history = false;
                Maintain();
                return;
            }
            if (!wasFiring) visuals.SetFiring(true);
            wasFiring = true;
            damaged.Clear();
            cutBudget = 2;
            // Emit at the eyes, not ahead of them: a forward offset can skip a thin wall.
            Vector3 origin = head.position - head.up * 0.008f;
            Vector3 direction = head.forward;
            Vector3 left = origin - head.right * 0.032f;
            Vector3 right = origin + head.right * 0.032f;
            BeamHit l = Trace(left, direction, true, false, Vector3.zero, Vector3.zero);
            BeamHit r = Trace(right, direction, true, false, Vector3.zero, Vector3.zero, 1);
            if (powered)
            {
                // A small physical fan gives the broad wave real coverage, with independent
                // wall occlusion. Rendering uses the nearest fan contact conservatively.
                float closest = Mathf.Min(Vector3.Distance(left, l.End), Vector3.Distance(right, r.End));
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 waveDirection = (direction + head.right * (j * 0.018f)).normalized;
                    BeamHit wave = Trace(origin, waveDirection, true, false, Vector3.zero, Vector3.zero, j < 0 ? 0 : 1);
                    float distance = Vector3.Distance(origin, wave.End);
                    if (distance < closest)
                    {
                        closest = distance;
                        l.Hit = r.Hit = wave.Hit; l.Contact = r.Contact = wave.Contact;
                    }
                }
                l.End = left + direction * closest; r.End = right + direction * closest;
            }
            Vector3 centerEnd = (l.End + r.End) * 0.5f;
            float angularSpeed = 0f;
            bool sampledScorches = false;
            SweepPlan plan;
            if (history && BeamSweepMath.TryCreate(V(previousOrigin), V(previousDirection), V(previousHit),
                V(origin), V(direction), V(centerEnd), Time.unscaledDeltaTime,
                Math.Max(20f, LaserVisionOptions.SweepSpeed), 0.5, 45.0, 0.5, 16, out plan))
            {
                angularSpeed = (float)plan.AngularSpeedDegrees;
                bool slicing = LaserVisionOptions.Slicing && bridge.Ready;
                if (slicing || LaserVisionOptions.Scorches)
                {
                    Vector3 normal = U(plan.PlaneNormal);
                    // Sample BOTH eye paths. Their offset matters for close targets.
                    for (int i = 0; i <= plan.SampleCount; i++)
                    {
                        double t = i / (double)plan.SampleCount;
                        Vector3 sampleOrigin = U(BeamSweepMath.OriginAt(V(previousOrigin), V(origin), t));
                        Vector3 sampleDirection = U(BeamSweepMath.DirectionAt(V(previousDirection), V(direction), t));
                        Vector3 tangent = Vector3.Cross(normal, sampleDirection).normalized;
                        Vector3 velocity = tangent * Mathf.Clamp(angularSpeed * Mathf.Deg2Rad * 3f, 1f, 12f);
                        // Align both sampled eye rays with the actual interpolated head orientation.
                        Vector3 sampleRight = Quaternion.FromToRotation(direction, sampleDirection) * head.right;
                        Trace(sampleOrigin - sampleRight * 0.032f, sampleDirection, false, slicing, normal, velocity, 0, true);
                        Trace(sampleOrigin + sampleRight * 0.032f, sampleDirection, false, slicing, normal, velocity, 1, true);
                    }
                    sampledScorches = true;
                }
            }
            if (!sampledScorches)
            {
                if (l.Contact) Scorch(l.Hit, 0);
                if (r.Contact) Scorch(r.Hit, 1);
            }
            if (LaserVisionOptions.Slicing && !bridge.Ready && Time.unscaledTime - lastNoBridgeWarning > 15f)
            {
                lastNoBridgeWarning = Time.unscaledTime;
                Debug.LogWarning("[LaserVision] Beam works, mesh slicing unavailable: " + bridge.Status);
            }
            visuals.Draw(left, l.End, l.Contact, l.Contact ? l.Hit.normal : Vector3.zero,
                right, r.End, r.Contact, r.Contact ? r.Hit.normal : Vector3.zero,
                LaserVisionOptions.CoreColor, LaserVisionOptions.GlowColor,
                Mathf.Clamp(LaserVisionOptions.WidthCm * 0.01f, 0.0025f, 0.05f) * (powered ? 5f : 1f),
                LaserVisionOptions.GlowStrength, LaserVisionOptions.Smear, angularSpeed);
            audio.Update(true, l.Contact || r.Contact, angularSpeed, LaserVisionOptions.Volume, charge, powered);
            previousOrigin = origin; previousDirection = direction; previousHit = centerEnd; history = true;
            Maintain();
        }

        private BeamHit Trace(Vector3 origin, Vector3 direction, bool combat, bool slicing, Vector3 normal, Vector3 velocity, int channel = 0, bool scorch = false)
        {
            float range = Mathf.Clamp(LaserVisionOptions.Range, 1f, 100f);
            float radius = Mathf.Clamp(LaserVisionOptions.WidthCm * 0.005f, 0.00125f, 0.025f) * (overdrive ? 4f : 1f);
            BeamHit result = new BeamHit { End = origin + direction * range };
            // SphereCast does not report colliders already overlapping its start.
            // Fail closed when an eye is inside geometry instead of firing out its far side.
            int overlapCount = Physics.OverlapSphereNonAlloc(origin, radius, originOverlaps, mask, QueryTriggerInteraction.Ignore);
            if (overlapCount >= originOverlaps.Length) { result.End = origin; return result; }
            for (int i = 0; i < overlapCount; i++)
            {
                Collider overlap = originOverlaps[i];
                if (!overlap || overlap.transform.IsChildOf(player.transform)) continue;
                Creature overlapCreature = ResolveCreature(overlap);
                if (overlapCreature && (overlapCreature == player || overlapCreature.isPlayer)) continue;
                result.End = origin;
                return result;
            }
            int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, range, mask, QueryTriggerInteraction.Ignore);
            // A filled buffer may omit the nearest wall. Fail closed instead of firing through unknown geometry.
            if (count >= hits.Length) { result.End = origin; return result; }
            Array.Sort(hits, 0, count, HitDistanceComparer.Instance);
            rayCreatures.Clear();
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (!hit.collider) continue;
                Creature creature = ResolveCreature(hit.collider);
                if (creature && (creature == player || creature.isPlayer)) continue;
                if (hit.collider.transform.IsChildOf(player.transform)) continue;
                // SphereCast point is on the surface, not necessarily on the beam axis.
                result.End = origin + direction * Mathf.Max(0f, hit.distance);
                result.Hit = hit; result.Contact = true;
                if (slicing && cutBudget > 0 && Time.unscaledTime >= nextCutTime)
                    TryCut(hit, creature, direction, normal, velocity);
                if (combat)
                {
                    if (creature) AffectCreature(creature, hit, direction);
                    else if (LaserVisionOptions.Contact == ContactStyle.Knockback && hit.rigidbody && !hit.rigidbody.isKinematic)
                        hit.rigidbody.AddForceAtPosition(direction * Mathf.Clamp(LaserVisionOptions.Push, 0f, 20f) * Mathf.Clamp(Time.deltaTime, 0f, 0.05f), hit.point, ForceMode.VelocityChange);
                }
                if (scorch) Scorch(hit, channel);
                if (!creature || !LaserVisionOptions.Pierce) return result;
                rayCreatures.Add(creature);
                if (rayCreatures.Count >= 3) return result;
            }
            if (LaserVisionOptions.Pierce) { result.End = origin + direction * range; result.Contact = false; }
            return result;
        }

        private void Scorch(RaycastHit hit, int channel)
        {
            if (!hit.collider || !LaserVisionOptions.Scorches || LaserVisionOptions.ScorchLifetime <= 0f) return;
            // Living skin uses UV char projection; surface ribbons on animated skin
            // would float or stretch. Detached pieces may use the surface heat overlay.
            if (ResolveCreature(hit.collider)) return;
            if (bridge.IsDetachedPiece(hit.collider) && !LaserVisionOptions.Burn) return;
            visuals.AddScorch(hit, Mathf.Clamp(LaserVisionOptions.WidthCm * 0.014f, 0.0035f, 0.07f),
                Mathf.Clamp(LaserVisionOptions.ScorchLifetime, 0f, 120f), channel);
        }

        private static RagdollPart ResolvePart(Collider collider)
        {
            if (!collider) return null;
            RagdollPart part = collider.GetComponentInParent<RagdollPart>();
            if (part) return part;
            ColliderGroup group = collider.GetComponentInParent<ColliderGroup>();
            return group && group.collisionHandler ? group.collisionHandler.ragdollPart : null;
        }
        private static Creature ResolveCreature(Collider collider)
        {
            RagdollPart part = ResolvePart(collider);
            return part && part.ragdoll ? part.ragdoll.creature : collider ? collider.GetComponentInParent<Creature>() : null;
        }

        private void AffectCreature(Creature creature, RaycastHit hit, Vector3 direction)
        {
            if (!damaged.Add(creature)) return; // Both eye rays together apply the selected DPS once.
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
            RagdollPart part = ResolvePart(hit.collider);
            if (LaserVisionOptions.Burn && part && skin != null)
            {
                try { skin.Paint(part,hit.point,hit.normal,LaserVisionOptions.WidthCm * 0.012f * (overdrive ? 3f : 1f)); }
                catch(Exception ex) { if(!skinWarning){skinWarning=true;Debug.LogWarning("[LaserVision] Optional skin char: "+ex.Message);} }
            }
            float nextImpact;
            if (LaserVisionOptions.DamagePerSecond > 0f && (!nextImpacts.TryGetValue(creature, out nextImpact) || Time.time >= nextImpact))
            {
                nextImpacts[creature] = Time.time + 0.10f;
                float amount = Mathf.Clamp(LaserVisionOptions.DamagePerSecond, 0f, 200f) * 0.10f * (overdrive ? LaserVisionOptions.OverdriveDamage : 1f);
                // Counter the game's distant/physics-off multiplier so holding a beam on
                // an animated target doesn't become an almost harmless damage trickle.
                if (creature.ragdoll && !creature.ragdoll.IsPhysicsEnabled() && creature.data != null && creature.data.physicsOffDamageMult > 0.001f)
                    amount /= creature.data.physicsOffDamageMult;
                bool crushing = LaserVisionOptions.Contact != ContactStyle.DamageOnly;
                var damage = new DamageStruct(crushing ? DamageType.Blunt : DamageType.Energy, amount) {
                    hitRagdollPart = part, pushLevel = -1, penetration = DamageStruct.Penetration.None
                };
                var collision = new CollisionInstance(damage) {
                    targetCollider = hit.collider,
                    targetColliderGroup = part ? part.colliderGroup : hit.collider.GetComponentInParent<ColliderGroup>(),
                    contactPoint = hit.point, contactNormal = hit.normal,
                    impactVelocity = crushing ? direction * Mathf.Clamp(LaserVisionOptions.ImpactSpeed, 5f, 60f) * (overdrive ? 1.5f : 1f) : Vector3.zero,
                    pressureForce = crushing ? direction * LaserVisionOptions.ImpactSpeed * (overdrive ? 2f : 1f) : Vector3.zero,
                    pressureRelativeVelocity = crushing ? direction * LaserVisionOptions.ImpactSpeed : Vector3.zero,
                    casterHand = activeCaster, intensity = overdrive ? 1f : 0.7f, incomplete = false
                };
                // Sends real localized creature-hit events for other mods to consume.
                // CrushingPressure deliberately applies no rigid-body translation.
                creature.Damage(collision);
            }
            if (LaserVisionOptions.Contact == ContactStyle.Knockback && LaserVisionOptions.Push > 0f && creature.ragdoll)
            {
                float nextPush;
                if (!nextPushes.TryGetValue(creature, out nextPush) || Time.time >= nextPush)
                {
                    nextPushes[creature] = Time.time + 0.35f;
                    creature.ragdoll.CancelGetUp();
                    creature.ragdoll.SetState(Ragdoll.State.Destabilized);
                }
                creature.AddForce(direction * Mathf.Clamp(LaserVisionOptions.Push, 0f, 20f) * dt * (overdrive ? 2f : 1f), ForceMode.VelocityChange, 0.8f, null);
            }
            float next;
            if (LaserVisionOptions.Burn && !creature.isKilled && (!burns.TryGetValue(creature, out next) || Time.time >= next))
            {
                // Finite duration: burning ends after the beam leaves instead of becoming permanent.
                creature.Inflict("Burning", this, 1.25f, 10f, true);
                burns[creature] = Time.time + 0.5f;
            }
        }

        private void TryCut(RaycastHit hit, Creature creature, Vector3 direction, Vector3 normal, Vector3 velocity)
        {
            UnityEngine.Object target = bridge.PieceTarget(hit.collider);
            if (!target && creature) target = creature;
            if (!target) return;
            int key = target.GetInstanceID();
            float next;
            if (cuts.TryGetValue(key, out next) && Time.unscaledTime < next) return;
            RagdollPart part = ResolvePart(hit.collider);
            // Move slightly inside the skin so a grazing hit doesn't just shave the contact triangle.
            Vector3 point = hit.point + direction * 0.012f;
            if (bridge.TryCut(hit.collider, creature, part, point, normal, velocity, LaserVisionOptions.Burn))
            {
                cuts[key] = Time.unscaledTime + Mathf.Clamp(LaserVisionOptions.CutCooldown, 0.1f, 1f);
                nextCutTime = Time.unscaledTime + 0.12f;
                cutBudget--;
            }
        }

        private void Maintain()
        {
            if (Time.unscaledTime - lastMaintenance < 3f) return;
            lastMaintenance = Time.unscaledTime;
            expiredCuts.Clear();
            foreach (var pair in cuts) if (pair.Value < Time.unscaledTime) expiredCuts.Add(pair.Key);
            foreach (int key in expiredCuts) cuts.Remove(key);
            expiredBurns.Clear();
            foreach (var pair in burns) if (!pair.Key || pair.Value + 2f < Time.time) expiredBurns.Add(pair.Key);
            foreach (Creature key in expiredBurns) burns.Remove(key);
            expiredBurns.Clear();
            foreach (var pair in nextImpacts) if (!pair.Key || pair.Value + 2f < Time.time) expiredBurns.Add(pair.Key);
            foreach (Creature key in expiredBurns) nextImpacts.Remove(key);
            expiredBurns.Clear();
            foreach (var pair in nextPushes) if (!pair.Key || pair.Value + 2f < Time.time) expiredBurns.Add(pair.Key);
            foreach (Creature key in expiredBurns) nextPushes.Remove(key);
        }
        private static SweepVector V(Vector3 v) { return new SweepVector(v.x, v.y, v.z); }
        private static Vector3 U(SweepVector v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }
        public void Dispose() { visuals.Dispose(); audio.Dispose(); if(skin!=null)skin.Dispose(); cuts.Clear(); burns.Clear(); nextImpacts.Clear(); nextPushes.Clear(); history = false; }

        private sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit x, RaycastHit y) { return x.distance.CompareTo(y.distance); }
        }
    }
}
