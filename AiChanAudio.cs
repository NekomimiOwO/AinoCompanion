using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElsaPetMod
{
    public sealed class AiChanAudio : MonoBehaviour
    {
        public float minDistance = 1f;
        public float maxDistance = 25f;
        public float barkCooldown = 0.25f;

        private AudioSource source;
        private AudioClip[] barkClips;
        private float nextBarkTime;
        private bool initialized;

        private readonly string[] allowedClips =
        {
            "barksmall1",
            "barksmall2",
            "barksmall3"
        };

        private void Awake()
        {
            GameObject audioChild = new GameObject("AiChan_Audio_Source");
            audioChild.transform.SetParent(transform, false);
            audioChild.transform.localPosition = Vector3.zero;

            source = audioChild.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Linear;

            // Desativa a distorção de pitch por velocidade e pulo
            source.dopplerLevel = 0f;

            LoadBarkClips();
        }

        private IEnumerator Start()
        {
            for (int i = 0; i < 20; i++)
            {
                if (initialized &&
                    barkClips != null &&
                    barkClips.Length > 0)
                {
                    yield break;
                }

                LoadBarkClips();
                yield return new WaitForSeconds(0.5f);
            }
        }

        public void LoadBarkClips()
        {
            List<AudioClip> found = new List<AudioClip>();

            foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (clip == null)
                    continue;

                string clipName = clip.name.ToLowerInvariant().Trim();

                foreach (string allowed in allowedClips)
                {
                    if (clipName != allowed)
                        continue;

                    if (!found.Contains(clip))
                        found.Add(clip);

                    break;
                }
            }

            if (found.Count > 0)
            {
                barkClips = found.ToArray();
                initialized = true;
            }
        }

        /// <summary>
        /// Utility method to scan and log all AudioClips loaded in memory.
        /// Call this via UnityExplorer or BepInEx logs to find Aino's native sound names.
        /// </summary>
        public static void LogAllAvailableClips()
        {
            Plugin.Log.LogInfo("=== [Ai-Chan Audio Scan] Listing AudioClips in memory ===");
            int count = 0;
            foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (clip != null)
                {
                    string name = clip.name.ToLowerInvariant();
                    if (name.Contains("aino") || name.Contains("elsa") || name.Contains("bark") || name.Contains("vo_"))
                    {
                        Plugin.Log.LogInfo("AudioClip [" + count + "]: " + clip.name + " (Length: " + clip.length + "s)");
                        count++;
                    }
                }
            }
            Plugin.Log.LogInfo("=== [Ai-Chan Audio Scan] Total clips found: " + count + " ===");
        }

        public void PlayBark()
        {
            if (!initialized ||
                barkClips == null ||
                barkClips.Length == 0)
            {
                LoadBarkClips();

                if (!initialized ||
                    barkClips == null ||
                    barkClips.Length == 0)
                {
                    return;
                }
            }

            if (Time.time < nextBarkTime)
                return;

            nextBarkTime = Time.time + barkCooldown;

            float configuredPercent =
                PetSettings.Volume != null
                    ? PetSettings.Volume.Value
                    : 50f;

            // 100% configured = 50% max output limit.
            float volume =
                Mathf.Clamp01(configuredPercent / 100f) * 0.5f;

            AudioClip clip =
                barkClips[Random.Range(0, barkClips.Length)];

            source.pitch = Random.Range(0.96f, 1.04f);
            source.PlayOneShot(clip, volume);
        }
    }
}