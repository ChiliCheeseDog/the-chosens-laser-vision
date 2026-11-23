using ThunderRoad;
using UnityEngine;

namespace LaserVision
{
    public enum CoreStyle { White, Black, GlowColor }
    public enum GlowStyle { Red, White, Blue, Cyan, Purple, Green, Gold, Pink, Orange }
    public enum ContactStyle { CrushingPressure, Knockback, DamageOnly }

    public static class LaserVisionOptions
    {
        [ModOption("Enabled", category = "The Chosen's Laser Vision", order = 0)]
        public static bool Enabled = true;
        [ModOption("Core color", category = "Appearance", order = 0)]
        public static CoreStyle Core = CoreStyle.White;
        [ModOption("Glow color", category = "Appearance", order = 1)]
        public static GlowStyle Glow = GlowStyle.Red;
        [ModOption("Beam width (cm)", "Physical beam diameter; damage checks follow this size", "WidthValues", category = "Appearance", order = 2, defaultValueIndex = 3)]
        public static float WidthCm = 1f;
        [ModOption("Glow strength", "Visual glow size and intensity", "GlowValues", category = "Appearance", order = 3, defaultValueIndex = 3)]
        public static float GlowStrength = 1f;
        [ModOption("Motion smear", category = "Appearance", order = 4)]
        public static bool Smear = true;
        [ModOption("Clear view near face (meters)", "Hides the first part of the beam visually; collision still starts at your eyes", "FaceValues", category = "Appearance", order = 7, defaultValueIndex = 5)]
        public static float FaceClearance = 0.25f;
        [ModOption("Smear duration", "Filled fading ribbon behind fast sweeps", "TrailValues", category = "Appearance", order = 8, defaultValueIndex = 4)]
        public static float TrailDuration = 0.10f;
        [ModOption("Smear intensity", "Opacity of the sweep ribbon", "GlowValues", category = "Appearance", order = 9, defaultValueIndex = 4)]
        public static float TrailStrength = 1.25f;
        [ModOption("Surface scorch marks", category = "Appearance", order = 5)]
        public static bool Scorches = true;
        [ModOption("Ground scorch lifetime (seconds)", "Surface ribbons fade; native skin char lasts until the game resets the character's reveal mask", "LifetimeValues", category = "Appearance", order = 6, defaultValueIndex = 3)]
        public static float ScorchLifetime = 30f;

        [ModOption("Damage per second", "Shared by both eyes; delivered as localized blunt impacts", "DamageValues", category = "Combat", order = 0, defaultValueIndex = 18)]
        public static float DamagePerSecond = 90f;
        [ModOption("Steady contact mode", "Crushing = localized blunt hits without added body force. Knockback = physical push. Deformation depends on other installed mods.", category = "Combat", order = -1)]
        public static ContactStyle Contact = ContactStyle.CrushingPressure;
        [ModOption("Crushing impact speed", "Blunt collision velocity metadata for hit reactions and compatible deformation mods; not guaranteed Carnage support", "ImpactValues", category = "Combat", order = 7, defaultValueIndex = 4)]
        public static float ImpactSpeed = 25f;
        [ModOption("Burn targets", "Applies a short renewable burn and heated cut edges", category = "Combat", order = 1)]
        public static bool Burn = true;
        [ModOption("Push strength", "Only used by Knockback mode; zero disables pushing", "PushValues", category = "Combat", order = 2, defaultValueIndex = 8)]
        public static float Push = 8f;
        [ModOption("Pierce enemies", "Off: ends on first enemy. On: penetrates up to three enemies; walls and objects always block", category = "Combat", order = 3, defaultValueIndex = 0)]
        public static bool Pierce = false;
        [ModOption("Range (meters)", "Solid scenery always blocks the beam", "RangeValues", category = "Combat", order = 4, defaultValueIndex = 3)]
        public static float Range = 40f;

        [ModOption("Dynamic mesh slicing", "Requires your installed Zandatsu 0.7.0; never substitutes joint-only dismemberment", category = "Slicing", order = 0)]
        public static bool Slicing = true;
        [ModOption("Minimum sweep speed (deg/s)", "Holding still damages and pushes; deliberate head sweeps cut", "SpeedValues", category = "Slicing", order = 1, defaultValueIndex = 4)]
        public static float SweepSpeed = 40f;
        [ModOption("Cut cooldown (seconds)", "Per target; limits repeated cuts and Quest CPU spikes", "CooldownValues", category = "Slicing", order = 2, defaultValueIndex = 4)]
        public static float CutCooldown = 0.5f;
        [ModOption("Sound volume", "Original synthesized charge, ignition, energy, sweep and searing sounds", "VolumeValues", category = "Audio", order = 0, defaultValueIndex = 6)]
        public static float Volume = 0.6f;
        [ModOption("Overdrive enabled", "Hold trigger plus grip on the casting hand for half a second to unleash a broad unstable energy wave", category = "Overdrive", order = 0)]
        public static bool OverdriveEnabled = true;
        [ModOption("Overdrive damage multiplier", "Applied to direct damage while the charged wave is active", "PowerValues", category = "Overdrive", order = 1, defaultValueIndex = 3)]
        public static float OverdriveDamage = 4f;

        public static ModOptionFloat[] FaceValues() { return ModOptionFloat.CreateArray(0f, 0.6f, 0.05f); }
        public static ModOptionFloat[] TrailValues() { return ModOptionFloat.CreateArray(0.02f, 0.18f, 0.02f); }
        public static ModOptionFloat[] ImpactValues() { return ModOptionFloat.CreateArray(5f, 60f, 5f); }
        public static ModOptionFloat[] PowerValues() { return ModOptionFloat.CreateArray(1f, 8f, 1f); }

        public static ModOptionFloat[] WidthValues() { return ModOptionFloat.CreateArray(0.25f, 5f, 0.25f); }
        public static ModOptionFloat[] GlowValues() { return ModOptionFloat.CreateArray(0.25f, 2f, 0.25f); }
        public static ModOptionFloat[] LifetimeValues() { return ModOptionFloat.CreateArray(0f, 120f, 10f); }
        public static ModOptionFloat[] DamageValues() { return ModOptionFloat.CreateArray(0f, 200f, 5f); }
        public static ModOptionFloat[] PushValues() { return ModOptionFloat.CreateArray(0f, 20f, 1f); }
        public static ModOptionFloat[] RangeValues() { return ModOptionFloat.CreateArray(10f, 100f, 10f); }
        public static ModOptionFloat[] SpeedValues() { return ModOptionFloat.CreateArray(20f, 160f, 5f); }
        public static ModOptionFloat[] CooldownValues() { return ModOptionFloat.CreateArray(0.1f, 1f, 0.1f); }
        public static ModOptionFloat[] VolumeValues() { return ModOptionFloat.CreateArray(0f, 1f, 0.1f); }
        public static Color GlowColor
        {
            get
            {
                switch (Glow)
                {
                    case GlowStyle.White: return Color.white;
                    case GlowStyle.Blue: return new Color(0.12f, 0.3f, 1f);
                    case GlowStyle.Cyan: return Color.cyan;
                    case GlowStyle.Purple: return new Color(0.65f, 0.08f, 1f);
                    case GlowStyle.Green: return new Color(0.1f, 1f, 0.2f);
                    case GlowStyle.Gold: return new Color(1f, 0.6f, 0.08f);
                    case GlowStyle.Pink: return new Color(1f, 0.12f, 0.55f);
                    case GlowStyle.Orange: return new Color(1f, 0.24f, 0.012f);
                    default: return new Color(1f, 0.025f, 0.01f);
                }
            }
        }
        public static Color CoreColor { get { return Core == CoreStyle.Black ? Color.black : Core == CoreStyle.GlowColor ? GlowColor : Color.white; } }
    }
}
