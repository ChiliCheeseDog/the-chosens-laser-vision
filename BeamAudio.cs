using System;
using System.Collections.Generic;
using System.IO;
#if !AUDIO_CODEC_TEST
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace LaserVision
{
    // Small RIFF decoder: runtime clips are read directly from the installed mod.
    // It deliberately does not use UnityWebRequest, codecs, or desktop-only APIs.
    internal sealed class WavePcm
    {
        public int Rate, Channels;
        public float[] Samples;
        public int Frames { get { return Samples.Length / Channels; } }
        public double Duration { get { return Frames / (double)Rate; } }

        public static WavePcm Read(string path)
        {
            using (FileStream stream = File.OpenRead(path)) return Read(stream);
        }

        public static WavePcm Read(Stream stream)
        {
            if (!stream.CanSeek) throw new InvalidDataException("WAV stream must be seekable");
            using (BinaryReader reader = new BinaryReader(stream, System.Text.Encoding.ASCII, true))
            {
                if (stream.Length < 12 || new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected RIFF WAV");
                uint riffLength = reader.ReadUInt32();
                if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected WAVE form");
                long end = Math.Min(stream.Length, 8L + riffLength), dataPosition = -1;
                int dataLength = 0, format = 0, channels = 0, rate = 0, bits = 0, blockAlign = 0;
                while (stream.Position + 8 <= end)
                {
                    string id = new string(reader.ReadChars(4));
                    uint size = reader.ReadUInt32(); long chunkStart = stream.Position;
                    if (size > end - chunkStart) throw new InvalidDataException("Truncated WAV chunk");
                    if (id == "fmt ")
                    {
                        if (size < 16) throw new InvalidDataException("Invalid WAV format chunk");
                        format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                        reader.ReadUInt32(); blockAlign = reader.ReadUInt16(); bits = reader.ReadUInt16();
                        if (format == 0xfffe)
                        {
                            if (size < 40 || reader.ReadUInt16() < 22) throw new InvalidDataException("Invalid extensible WAV format");
                            reader.ReadUInt16(); reader.ReadUInt32(); format = reader.ReadUInt16();
                        }
                    }
                    else if (id == "data" && dataPosition < 0)
                    {
                        if (size > 64 * 1024 * 1024) throw new InvalidDataException("WAV exceeds 64 MiB limit");
                        dataPosition = chunkStart; dataLength = (int)size;
                    }
                    stream.Position = chunkStart + size + (size & 1);
                }
                if (dataPosition < 0 || channels < 1 || channels > 8 || rate < 8000 || rate > 192000 ||
                    (format != 1 && format != 3) || (bits != 8 && bits != 16 && bits != 24 && bits != 32) ||
                    (format == 3 && bits != 32) || blockAlign != channels * (bits / 8) || dataLength % blockAlign != 0)
                    throw new InvalidDataException("Unsupported or malformed WAV PCM data");
                int count = dataLength / (bits / 8);
                WavePcm result = new WavePcm { Rate = rate, Channels = channels, Samples = new float[count] };
                stream.Position = dataPosition;
                for (int i = 0; i < count; i++)
                {
                    float value;
                    if (format == 3) value = reader.ReadSingle();
                    else if (bits == 8) value = (reader.ReadByte() - 128) / 128f;
                    else if (bits == 16) value = reader.ReadInt16() / 32768f;
                    else if (bits == 24)
                    {
                        int v = reader.ReadByte() | (reader.ReadByte() << 8) | (reader.ReadByte() << 16);
                        if ((v & 0x800000) != 0) v |= unchecked((int)0xff000000);
                        value = v / 8388608f;
                    }
                    else value = (float)(reader.ReadInt32() / 2147483648.0);
                    result.Samples[i] = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(-1f, Math.Min(1f, value));
                }
                return result;
            }
        }

        // These operations are also used by the included preparation script/tests.
        public WavePcm Slice(double startSeconds, double seconds)
        {
            int first = Math.Max(0, Math.Min(Frames - 1, (int)(startSeconds * Rate)));
            int length = Math.Max(1, Math.Min(Frames - first, (int)(seconds * Rate)));
            float[] copy = new float[length * Channels]; Array.Copy(Samples, first * Channels, copy, 0, copy.Length);
            return new WavePcm { Rate = Rate, Channels = Channels, Samples = copy };
        }
        public void Normalize(float peak, bool fade)
        {
            double mean = 0; for (int i = 0; i < Samples.Length; i++) mean += Samples[i]; mean /= Math.Max(1, Samples.Length);
            float maximum = 0;
            for (int i = 0; i < Samples.Length; i++) { Samples[i] -= (float)mean; maximum = Math.Max(maximum, Math.Abs(Samples[i])); }
            float gain = maximum > 0.00001f ? Math.Min(8f, peak / maximum) : 1;
            int edgeFrames = Math.Max(1, (int)(Rate * 0.006));
            for (int i = 0; i < Samples.Length; i++)
            {
                int frame = i / Channels;
                float envelope = fade ? Math.Min(1f, Math.Min(frame / (float)edgeFrames, (Frames - frame - 1) / (float)edgeFrames)) : 1f;
                Samples[i] = Math.Max(-1f, Math.Min(1f, Samples[i] * gain * envelope));
            }
        }
        public WavePcm Seamless(double crossfadeSeconds)
        {
            int overlap = Math.Max(1, Math.Min(Frames / 4, (int)(crossfadeSeconds * Rate)));
            int length = Frames - overlap;
            float[] data = new float[length * Channels];
            Array.Copy(Samples, data, data.Length);
            for (int frame = 0; frame < overlap; frame++)
            {
                float t = (float)(0.5 - 0.5 * Math.Cos(Math.PI * frame / Math.Max(1, overlap - 1)));
                for (int channel = 0; channel < Channels; channel++)
                    data[frame * Channels + channel] = Samples[frame * Channels + channel] * t + Samples[(length + frame) * Channels + channel] * (1f - t);
            }
            return new WavePcm { Rate = Rate, Channels = Channels, Samples = data };
        }
        public void Write16(string path)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + Samples.Length * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1);
                writer.Write((ushort)Channels); writer.Write(Rate); writer.Write(Rate * Channels * 2);
                writer.Write((ushort)(Channels * 2)); writer.Write((ushort)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(Samples.Length * 2);
                for (int i = 0; i < Samples.Length; i++) writer.Write((short)Math.Round(Math.Max(-1f, Math.Min(0.9999695f, Samples[i])) * 32768f));
            }
        }
    }

#if !AUDIO_CODEC_TEST
    public sealed class BeamAudio : IDisposable
    {
        readonly GameObject root;
        readonly AudioSource energy, bass, contact, ignition, chargeSource, movement;
        readonly List<AudioClip> clips = new List<AudioClip>();
        readonly AudioClip ignitionClip, chargeClip, whooshA, whooshB, shutdown;
        bool wasActive, wasCharging, wasOverdrive, disposed;
        float nextWhoosh, previousSpeed, phase;
        int whooshIndex;

        public BeamAudio(Transform head, string modDirectory)
        {
            root = new GameObject("LaserVision_Audio");
            try
            {
                if (head) root.transform.SetParent(head, false);
                energy=Source(root,130); bass=Source(root,140); contact=Source(root,145);
                ignition=Source(root,120); chargeSource=Source(root,125); movement=Source(root,135);
                energy.clip=Load(modDirectory,"SampleEnergyLoop.wav",0); energy.loop=true;
                bass.clip=Synth("LaserVision_SubEnergy",1f,1); bass.loop=true;
                contact.clip=Load(modDirectory,"ContactLoop.wav",2); contact.loop=true;
                ignitionClip=Load(modDirectory,"BeamIgnition.wav",3);
                chargeClip=Load(modDirectory,"BeamCharge.wav",4);
                whooshA=Load(modDirectory,"BeamWhooshA.wav",5);
                whooshB=Load(modDirectory,"BeamWhooshB.wav",5);
                shutdown=Synth("LaserVision_Shutdown",0.16f,6);
            }
            catch { Dispose(); throw; }
        }

        static AudioSource Source(GameObject owner,int priority)
        {
            AudioSource source=owner.AddComponent<AudioSource>();
            source.playOnAwake=false; source.spatialBlend=0; source.dopplerLevel=0;
            source.priority=priority; source.volume=0;
            return source;
        }
        AudioClip Load(string directory,string file,int fallback)
        {
            // Public release: all audio is generated from oscillators and noise.
            // The creator's privately supplied recordings are not redistributed
            // and are never read, even if an older Audio folder remains installed.
            float duration = fallback == 4 ? 0.5f : fallback == 3 ? 0.28f : fallback == 5 ? 0.36f : 1f;
            return Synth("Chosen_PublicSynth_"+fallback,duration,fallback);
        }

        public void Update(bool active,bool impact,float angularSpeed,float volume,float charge01,bool overdrive)
        {
            if(disposed || !root) return;
            volume=Mathf.Clamp01(volume); angularSpeed=Mathf.Max(0,angularSpeed); charge01=Mathf.Clamp01(charge01);
            float dt=Mathf.Clamp(Time.unscaledDeltaTime,0.001f,0.1f);
            bool charging=charge01>0.001f && !overdrive;
            if(charging && !wasCharging)
            {
                chargeSource.clip=chargeClip; chargeSource.loop=false;
                // The supplied ~one-second rise is compressed to the half-second charge window.
                chargeSource.pitch=Mathf.Clamp(chargeClip.length/0.5f,0.6f,2.5f);
                chargeSource.volume=volume*0.72f; chargeSource.Play();
            }
            if(!charging && wasCharging) chargeSource.Stop();
            if(active && (!wasActive || (overdrive && !wasOverdrive)))
            {
                if(!energy.isPlaying) energy.Play(); if(!bass.isPlaying) bass.Play(); if(!contact.isPlaying) contact.Play();
                ignition.Stop(); ignition.pitch=overdrive?0.85f:1f; ignition.volume=volume*(overdrive?0.78f:0.62f);
                ignition.PlayOneShot(ignitionClip);
            }
            if(!active && wasActive)
            {
                ignition.Stop(); movement.Stop(); ignition.pitch=1f; ignition.volume=volume*0.28f; ignition.PlayOneShot(shutdown);
            }
            phase+=dt;
            float movementAmount=Mathf.Clamp01(angularSpeed/350f);
            float instability=overdrive?0.88f+0.12f*Mathf.Sin(phase*37f)*Mathf.Sin(phase*13f):1f;
            float energyTarget=active?volume*(overdrive?0.63f:0.42f)*instability:0;
            float bassTarget=active?volume*(overdrive?0.24f:0.12f):0;
            float contactTarget=active&&impact?volume*(overdrive?0.35f:0.23f):0;
            energy.volume=Mathf.MoveTowards(energy.volume,energyTarget,dt*3f);
            bass.volume=Mathf.MoveTowards(bass.volume,bassTarget,dt*2f);
            contact.volume=Mathf.MoveTowards(contact.volume,contactTarget,dt*3f);
            float pitchTarget=(overdrive?0.88f:1f)+movementAmount*0.17f;
            energy.pitch=Mathf.Lerp(energy.pitch,pitchTarget,1f-Mathf.Exp(-dt*9f));
            bass.pitch=Mathf.Lerp(bass.pitch,overdrive?0.85f:1f,1f-Mathf.Exp(-dt*8f));
            contact.pitch=Mathf.Lerp(contact.pitch,0.94f+movementAmount*0.2f,1f-Mathf.Exp(-dt*7f));
            bool newlyFast=angularSpeed>=95f && previousSpeed<95f;
            bool renewedSweep=angularSpeed>=180f && Time.unscaledTime>=nextWhoosh;
            if(active && Time.unscaledTime>=nextWhoosh && (newlyFast||renewedSweep))
            {
                movement.Stop(); movement.clip=(whooshIndex++&1)==0?whooshA:whooshB;
                movement.pitch=0.94f+movementAmount*0.28f; movement.volume=volume*(overdrive?0.65f:0.46f);
                movement.Play(); nextWhoosh=Time.unscaledTime+Mathf.Max(0.34f,movement.clip.length/movement.pitch+0.02f);
            }
            // Long sample tails should not stack on repeated button taps or sweep reversals.
            if(!active && energy.volume<0.001f && bass.volume<0.001f && contact.volume<0.001f)
            { energy.Stop(); bass.Stop(); contact.Stop(); }
            wasActive=active; wasCharging=charging; wasOverdrive=overdrive; previousSpeed=angularSpeed;
        }

        AudioClip Synth(string name,float seconds,int kind)
        {
            const int rate=24000; int count=(int)(seconds*rate); float[] samples=new float[count];
            uint state=89117231; double phaseValue=0; float filtered=0;
            for(int i=0;i<count;i++)
            {
                double t=i/(double)rate, progress=i/(double)count;
                state^=state<<13; state^=state>>17; state^=state<<5;
                float noise=(state&65535)/32767.5f-1f; filtered=filtered*0.86f+noise*0.14f;
                double sample;
                if(kind==0||kind==1)
                    sample=0.32*Math.Sin(t*Math.PI*2*90)+0.15*Math.Sin(t*Math.PI*2*180)+
                        0.09*Math.Sin(t*Math.PI*2*360)+filtered*0.10;
                else if(kind==2) sample=filtered*0.55+noise*0.08+0.06*Math.Sin(t*Math.PI*2*480);
                else
                {
                    double frequency=kind==4?100+950*progress*progress:kind==6?750*(1-progress)+70:120+600*(1-progress);
                    phaseValue+=frequency*Math.PI*2/rate;
                    double envelope=kind==4?Math.Sin(Math.PI*progress*0.8):Math.Sin(Math.PI*progress)*(1-progress);
                    sample=(Math.Sin(phaseValue)*0.30+Math.Sin(phaseValue*2.01)*0.13+filtered*0.24)*envelope;
                }
                samples[i]=(float)sample;
            }
            WavePcm wav=new WavePcm { Rate=rate,Channels=1,Samples=samples };
            if(kind<=2) wav=wav.Seamless(0.025); else wav.Normalize(0.7f,true);
            AudioClip clip=AudioClip.Create(name,wav.Frames,1,rate,false); clips.Add(clip); clip.SetData(wav.Samples,0); return clip;
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true;
            if(root) Object.Destroy(root);
            foreach(AudioClip clip in clips) if(clip) Object.Destroy(clip);
            clips.Clear();
        }
    }
#endif
}
