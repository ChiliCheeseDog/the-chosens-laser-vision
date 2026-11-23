using System;

namespace LaserVision
{
    /// <summary>Engine-independent vector used by sweep tests and the Unity adapter.</summary>
    public struct SweepVector
    {
        public readonly double X, Y, Z;
        public SweepVector(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public bool IsFinite { get { return Finite(X) && Finite(Y) && Finite(Z); } }
        internal static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        public static SweepVector operator +(SweepVector a, SweepVector b) { return new SweepVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static SweepVector operator -(SweepVector a, SweepVector b) { return new SweepVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static SweepVector operator *(SweepVector a, double s) { return new SweepVector(a.X*s,a.Y*s,a.Z*s); }
        public static double Dot(SweepVector a, SweepVector b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        public static SweepVector Cross(SweepVector a, SweepVector b) { return new SweepVector(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X); }
        public SweepVector Unit { get { double n = Length; return n > 1e-12 ? this*(1.0/n) : new SweepVector(); } }
    }

    public struct SweepPlan
    {
        public SweepVector PlanePoint, PlaneNormal, Direction;
        public double AngleDegrees, AngularSpeedDegrees;
        /// <summary>Number of intervals, not rays. Cast indices 0 through SampleCount inclusive.</summary>
        public int SampleCount;
    }

    /// <summary>
    /// Defines a slash from head rotation, never from cosmetic trail vertices. Both smear and
    /// cutting should consume this same plan. Each sample still needs its own occlusion raycast.
    /// </summary>
    public static class BeamSweepMath
    {
        private const double RadiansToDegrees = 180.0 / Math.PI;

        public static bool TryCreate(SweepVector previousOrigin, SweepVector previousDirection,
            SweepVector previousHit, SweepVector origin, SweepVector direction, SweepVector hit,
            double deltaTime, double minimumDegreesPerSecond, double maximumOriginStep,
            double maximumAngleDegrees, double sampleSpacingDegrees, int maximumSamples,
            out SweepPlan plan)
        {
            plan = new SweepPlan();
            if (!previousOrigin.IsFinite || !origin.IsFinite || !previousDirection.IsFinite ||
                !direction.IsFinite || !previousHit.IsFinite || !hit.IsFinite ||
                !SweepVector.Finite(deltaTime) || deltaTime <= 0 || deltaTime > 0.25 ||
                !SweepVector.Finite(minimumDegreesPerSecond) || minimumDegreesPerSecond < 0 ||
                !SweepVector.Finite(maximumOriginStep) || maximumOriginStep < 0 ||
                !SweepVector.Finite(maximumAngleDegrees) || maximumAngleDegrees <= 0 || maximumAngleDegrees >= 180 ||
                !SweepVector.Finite(sampleSpacingDegrees) || sampleSpacingDegrees <= 0 || maximumSamples < 1)
                return false;

            if ((origin - previousOrigin).Length > maximumOriginStep ||
                previousDirection.Length < 1e-8 || direction.Length < 1e-8) return false;

            SweepVector a = previousDirection.Unit, b = direction.Unit;
            double cosine = Math.Max(-1.0, Math.Min(1.0, SweepVector.Dot(a, b)));
            double angle = Math.Acos(cosine) * RadiansToDegrees;
            // Subpixel eye/head tremor is not a deliberate slash, even with a tiny frame time.
            if (angle < 0.05 || angle > maximumAngleDegrees || angle / deltaTime < minimumDegreesPerSecond)
                return false;

            SweepVector normal = SweepVector.Cross(a, b);
            if (normal.Length < 1e-8) return false;
            plan.PlanePoint = (previousHit + hit) * 0.5;
            plan.PlaneNormal = normal.Unit;
            // Tangent at the middle ray: useful for separation force / velocity passed to the cutter.
            plan.Direction = SweepVector.Cross(plan.PlaneNormal, (a + b).Unit).Unit;
            plan.AngleDegrees = angle;
            plan.AngularSpeedDegrees = angle / deltaTime;
            // Compare in double before conversion to avoid integer overflow with unusual settings.
            plan.SampleCount = (int)Math.Max(1.0, Math.Min((double)maximumSamples, Math.Ceiling(angle / sampleSpacingDegrees)));
            return true;
        }

        public static SweepVector OriginAt(SweepVector previous, SweepVector current, double fraction)
        {
            double t = Math.Max(0.0, Math.Min(1.0, fraction));
            return previous * (1.0 - t) + current * t;
        }

        /// <summary>Shortest-arc spherical interpolation. TryCreate rejects 180-degree jumps.</summary>
        public static SweepVector DirectionAt(SweepVector previous, SweepVector current, double fraction)
        {
            double t = Math.Max(0.0, Math.Min(1.0, fraction));
            SweepVector a = previous.Unit, b = current.Unit;
            double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, SweepVector.Dot(a, b))));
            double sine = Math.Sin(angle);
            if (Math.Abs(sine) < 1e-8) return (a * (1.0 - t) + b * t).Unit;
            return (a * (Math.Sin((1.0 - t) * angle) / sine) + b * (Math.Sin(t * angle) / sine)).Unit;
        }
    }
}
