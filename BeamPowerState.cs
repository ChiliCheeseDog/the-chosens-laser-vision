using System;
namespace LaserVision
{
    // Pure input state: releasing either control cancels charge immediately.
    public sealed class BeamPowerState
    {
        private double held;
        public float Charge { get; private set; }
        public bool Overdrive { get; private set; }
        public void Step(bool firing, bool grip, bool enabled, double deltaTime)
        {
            if (!firing || !grip || !enabled) { held = 0; Charge = 0; Overdrive = false; return; }
            // Unscaled time: slow motion does not turn a half-second gesture into seconds.
            if (!double.IsNaN(deltaTime) && !double.IsInfinity(deltaTime) && deltaTime > 0)
                held = Math.Min(0.5, held + Math.Min(deltaTime, 0.1));
            Charge = (float)Math.Min(1, held / 0.5);
            Overdrive = held >= 0.5 - 1e-8;
        }
    }
}
