using System;
using System.Reflection;
using ThunderRoad;
using UnityEngine;

namespace LaserVision
{
    // A compatibility adapter for the user's separately installed Zandatsu 0.7.0.
    // No Zandatsu code or assets are embedded and no existing assembly is patched.
    public sealed class ZandatsuBridge
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private object script;
        private Type scriptType;
        private Type pendingType;
        private Type pieceType;
        private Type pieceBodyType;
        private MethodInfo claim;
        private MethodInfo cutPiece;
        private MethodInfo healthAllows;
        private FieldInfo enabled;
        private FieldInfo recutEnabled;
        private PropertyInfo goreAllowed;
        private FieldInfo cutCreature, cutPoint, cutNormal, cutVelocity, cutPart, cutItem, cutFire;
        private FieldInfo bodyPiece, pieceBusy;
        private PropertyInfo pieceDead;
        private float nextRefresh;
        private float nextError;
        private string status = "Waiting for Zandatsu 0.7.0";

        public string Status { get { Refresh(); return status; } }
        public bool Ready { get { Refresh(); return script != null && IsAllowed(); } }

        // All colliders on the same detached piece return the same identity,
        // allowing the caller to enforce a per-piece cut cooldown.
        public UnityEngine.Object PieceTarget(Collider collider)
        {
            Refresh();
            if (script == null || !collider) return null;
            try { return FindPiece(collider) as UnityEngine.Object; }
            catch (Exception ex) { ReportError(ex); return null; }
        }

        public bool IsDetachedPiece(Collider collider)
        {
            return PieceTarget(collider) != null;
        }

        public void Refresh()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 2f;
            object found = null;
            try
            {
                foreach (ModManager.ModData mod in ModManager.loadedMods)
                {
                    if (mod == null || mod.thunderScripts == null) continue;
                    foreach (ThunderScript candidate in mod.thunderScripts)
                    {
                        if (candidate != null && candidate.GetType().FullName == "Zandatsu.ZandatsuScript")
                        {
                            found = candidate;
                            break;
                        }
                    }
                    if (found != null) break;
                }
                if (found == null)
                {
                    script = null;
                    SetStatus("Zandatsu not loaded; mesh cutting unavailable");
                    return;
                }
                if (!ReferenceEquals(script, found)) Bind(found);
                IsAllowed();
            }
            catch (Exception ex)
            {
                script = null;
                SetStatus("Zandatsu interface unavailable; mesh cutting disabled");
                ReportError(ex);
            }
        }

        private void Bind(object candidate)
        {
            // Validate every signature before permitting any cut request. A changed
            // private API must disable the bridge, not corrupt Zandatsu's queues.
            script = null;
            scriptType = candidate.GetType();
            pendingType = scriptType.GetNestedType("PendingCut", BindingFlags.NonPublic);
            if (pendingType == null || !pendingType.IsValueType) throw new MissingMemberException("Zandatsu PendingCut");
            cutCreature = RequireField(pendingType, "creature", typeof(Creature), InstanceMembers);
            cutPoint = RequireField(pendingType, "point", typeof(Vector3), InstanceMembers);
            cutNormal = RequireField(pendingType, "normal", typeof(Vector3), InstanceMembers);
            cutVelocity = RequireField(pendingType, "bladeVelocity", typeof(Vector3), InstanceMembers);
            cutPart = RequireField(pendingType, "hitPart", typeof(RagdollPart), InstanceMembers);
            cutItem = RequireField(pendingType, "item", typeof(Item), InstanceMembers);
            cutFire = RequireField(pendingType, "fire", typeof(float), InstanceMembers);
            enabled = RequireField(scriptType, "Enabled", typeof(bool), StaticMembers);
            recutEnabled = RequireField(scriptType, "RecutPieces", typeof(bool), StaticMembers);
            goreAllowed = scriptType.GetProperty("GoreAllowed", StaticMembers);
            if (goreAllowed == null || goreAllowed.PropertyType != typeof(bool) || goreAllowed.GetGetMethod(true) == null)
                throw new MissingMemberException("Zandatsu GoreAllowed");
            claim = scriptType.GetMethod("Claim", InstanceMembers, null, new Type[] { pendingType }, null);
            if (claim == null || claim.ReturnType != typeof(bool)) throw new MissingMethodException("Zandatsu Claim");
            healthAllows = scriptType.GetMethod("HealthAllows", StaticMembers, null, new Type[] { typeof(Creature), typeof(float) }, null);
            if (healthAllows == null || healthAllows.ReturnType != typeof(bool)) throw new MissingMethodException("Zandatsu HealthAllows");
            pieceType = scriptType.Assembly.GetType("Zandatsu.SlicedPiece", false);
            pieceBodyType = scriptType.Assembly.GetType("Zandatsu.PieceBody", false);
            if (pieceType == null || pieceBodyType == null || !typeof(Component).IsAssignableFrom(pieceType) || !typeof(Component).IsAssignableFrom(pieceBodyType))
                throw new MissingMemberException("Zandatsu detached piece types");
            bodyPiece = RequireField(pieceBodyType, "piece", pieceType, InstanceMembers);
            pieceBusy = RequireField(pieceType, "busy", typeof(bool), InstanceMembers);
            pieceDead = pieceType.GetProperty("IsDead", InstanceMembers);
            if (pieceDead == null || pieceDead.PropertyType != typeof(bool) || pieceDead.GetGetMethod(true) == null)
                throw new MissingMemberException("Zandatsu IsDead");
            cutPiece = scriptType.GetMethod("CutPiece", InstanceMembers, null,
                new Type[] { pieceType, typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(Item), typeof(int), typeof(float) }, null);
            if (cutPiece == null || cutPiece.ReturnType != typeof(void)) throw new MissingMethodException("Zandatsu CutPiece");
            script = candidate;
        }

        private static FieldInfo RequireField(Type owner, string name, Type fieldType, BindingFlags flags)
        {
            FieldInfo field = owner.GetField(name, flags);
            if (field == null || field.FieldType != fieldType) throw new MissingFieldException(owner.FullName, name);
            return field;
        }

        private bool IsAllowed()
        {
            if (script == null) return false;
            try
            {
                if (!(bool)enabled.GetValue(null)) { SetStatus("Zandatsu disabled in mod options"); return false; }
                if (!(bool)goreAllowed.GetValue(null, null)) { SetStatus("Mesh cutting disabled by game content settings"); return false; }
                SetStatus("Zandatsu mesh cutting ready");
                return true;
            }
            catch (Exception ex)
            {
                script = null;
                SetStatus("Zandatsu interface failed; mesh cutting disabled");
                ReportError(ex);
                return false;
            }
        }

        public bool TryCut(Collider hitCollider, Creature creature, RagdollPart part, Vector3 point,
            Vector3 normal, Vector3 swingVelocity, bool burn)
        {
            Refresh();
            if (!IsAllowed() || !Finite(point) || !Finite(normal) || !Finite(swingVelocity) || normal.sqrMagnitude < 0.000001f) return false;
            normal.Normalize();
            try
            {
                // A detached physics body carries PieceBody rather than Creature.
                // Prefer it even if a parent hierarchy still references a creature.
                object piece = FindPiece(hitCollider);
                if (piece != null)
                {
                    if (!(bool)recutEnabled.GetValue(null) || (bool)pieceDead.GetValue(piece, null) || (bool)pieceBusy.GetValue(piece)) return false;
                    Component component = piece as Component;
                    if (!component || !component.gameObject.activeInHierarchy) return false;
                    cutPiece.Invoke(script, new object[] { piece, point, normal, swingVelocity, null, -1, burn ? 1f : 0f });
                    return (bool)pieceBusy.GetValue(piece);
                }
                if (!creature || creature.isPlayer || !creature.gameObject.activeInHierarchy) return false;
                // Respect Zandatsu's own health threshold for living characters.
                if (!(bool)healthAllows.Invoke(null, new object[] { creature, 0f })) return false;
                object pending = Activator.CreateInstance(pendingType);
                cutCreature.SetValue(pending, creature);
                cutPoint.SetValue(pending, point);
                cutNormal.SetValue(pending, normal);
                cutVelocity.SetValue(pending, swingVelocity);
                cutPart.SetValue(pending, part);
                cutItem.SetValue(pending, null);
                cutFire.SetValue(pending, burn ? 1f : 0f);
                return (bool)claim.Invoke(script, new object[] { pending });
            }
            catch (Exception ex)
            {
                ReportError(ex);
                return false;
            }
        }

        private object FindPiece(Collider collider)
        {
            if (!collider) return null;
            for (Transform node = collider.transform; node != null; node = node.parent)
            {
                Component body = node.GetComponent(pieceBodyType);
                if (body)
                {
                    object piece = bodyPiece.GetValue(body);
                    Component pieceComponent = piece as Component;
                    if (pieceComponent) return piece;
                }
                Component direct = node.GetComponent(pieceType);
                if (direct) return direct;
            }
            return null;
        }

        private static bool Finite(Vector3 value)
        {
            return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z)
                || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
        }

        private void SetStatus(string value)
        {
            if (status == value) return;
            status = value;
            Debug.Log("[LaserVision] " + value);
        }

        private void ReportError(Exception error)
        {
            if (Time.unscaledTime < nextError) return;
            nextError = Time.unscaledTime + 10f;
            Exception cause = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
            Debug.LogWarning("[LaserVision] Zandatsu bridge: " + cause.GetType().Name + ": " + cause.Message);
        }
    }
}
