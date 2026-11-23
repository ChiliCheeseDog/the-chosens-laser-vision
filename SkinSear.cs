using System;
using System.Collections;
using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.Reveal;
using UnityEngine;
using UnityEngine.Rendering;

namespace LaserVision
{
    // Uses the same green reveal channel as the game's char status. Paints UVs,
    // not world-space stickers, so the mark follows skinned body deformation.
    internal sealed class SkinSear : IDisposable
    {
        private readonly GameObject host;
        private readonly SearRunner runner;
        private readonly Texture2D stamp;
        private float nextPaint;
        private bool warned;
        public SkinSear(Transform parent)
        {
            host = new GameObject("Chosen_SkinSear"); host.transform.SetParent(parent, false);
            runner = host.AddComponent<SearRunner>();
            stamp = new Texture2D(32,32,TextureFormat.RGBA32,false) { wrapMode=TextureWrapMode.Clamp };
            Color[] pixels = new Color[1024];
            for (int y=0;y<32;y++) for(int x=0;x<32;x++)
            {
                float radius = new Vector2((x-15.5f)/16f,(y-15.5f)/16f).magnitude;
                float strength = Mathf.Clamp01((1f-radius)*3f);
                pixels[y*32+x] = new Color(strength,strength,strength,strength);
            }
            stamp.SetPixels(pixels); stamp.Apply(false,true);
        }
        public void Paint(RagdollPart part, Vector3 point, Vector3 normal, float size)
        {
            if (!part || Time.time < nextPaint || !host || !RevealMaskProjection.uvSpaceMaterial) return;
            nextPaint = Time.time + 0.12f;
            var controllers = new List<RevealMaterialController>();
            foreach (var data in part.renderers)
            {
                if (data == null) continue;
                if (data.revealDecal && data.revealDecal.revealMaterialController && !controllers.Contains(data.revealDecal.revealMaterialController))
                    controllers.Add(data.revealDecal.revealMaterialController);
                RevealMaterialController split;
                if (data.splitRenderer && data.splitRenderer.TryGetComponent<RevealMaterialController>(out split) && !controllers.Contains(split)) controllers.Add(split);
            }
            if (controllers.Count == 0) return;
            Vector3 up = Vector3.Cross(normal, Vector3.right);
            if (up.sqrMagnitude < 0.01f) up = Vector3.Cross(normal, Vector3.up);
            var reveal = new[] { new RevealData { blendOp = BlendOp.Add, colorMask = ColorWriteMask.Green } };
            try { runner.StartCoroutine(Guard(RevealMaskProjection.ProjectAsync(point+normal*0.025f,-normal,up.normalized,0.075f,
                Mathf.Clamp(size,0.012f,0.09f),stamp,new Vector4(0,1,0,0),controllers,reveal,null))); }
            catch (Exception ex) { Warn(ex); }
        }
        private IEnumerator Guard(IEnumerator job)
        {
            while (true)
            {
                bool more;
                try { more=job.MoveNext(); }
                catch(Exception ex) { Warn(ex); yield break; }
                if (!more) yield break;
                yield return job.Current;
            }
        }
        private void Warn(Exception e) { if (!warned) { warned=true; Debug.LogWarning("[LaserVision] Skin char unavailable on this renderer: "+e.Message); } }
        public void Dispose() { if (host) { runner.StopAllCoroutines(); UnityEngine.Object.Destroy(host); } if (stamp) UnityEngine.Object.Destroy(stamp); }
    }
    public sealed class SearRunner : MonoBehaviour { }
}
