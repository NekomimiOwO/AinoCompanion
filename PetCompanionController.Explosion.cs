using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;
using UnityEngine;

namespace ElsaPetMod
{
    public partial class PetCompanionController
    {
        private Coroutine explodeCountdownCoroutine;
        private TextMesh countdownTextMesh;
        private GameObject countdownTagObject;

        private static AudioClip cachedBeepClip;
        private static bool searchedForBeepClip;
        private AudioSource countdownAudioSource;

        private void EnsureBeepAudioSource()
        {
            if (countdownAudioSource == null)
            {
                GameObject audioObj = new GameObject("AiChan_Countdown_Audio");
                audioObj.transform.SetParent(transform, false);
                audioObj.transform.localPosition = Vector3.zero;

                countdownAudioSource = audioObj.AddComponent<AudioSource>();
                countdownAudioSource.playOnAwake = false;
                countdownAudioSource.loop = false;
                countdownAudioSource.spatialBlend = 1f;
                countdownAudioSource.minDistance = 3f;
                countdownAudioSource.maxDistance = 40f;
                countdownAudioSource.rolloffMode = AudioRolloffMode.Linear;
                countdownAudioSource.dopplerLevel = 0f;
            }

            if (!searchedForBeepClip || cachedBeepClip == null)
            {
                searchedForBeepClip = true;
                AudioClip[] clips = Resources.FindObjectsOfTypeAll<AudioClip>();

                cachedBeepClip = clips.FirstOrDefault(c => c != null && c.name.Equals("item explosive mine warning beeps", StringComparison.OrdinalIgnoreCase))
                    ?? clips.FirstOrDefault(c => c != null && c.name.Equals("grenade countdown", StringComparison.OrdinalIgnoreCase))
                    ?? clips.FirstOrDefault(c => c != null && c.name.Equals("item explosive mine arm beep", StringComparison.OrdinalIgnoreCase))
                    ?? clips.FirstOrDefault(c => c != null && c.name.Equals("snd_dirt_tracker_beep", StringComparison.OrdinalIgnoreCase));
            }

            if (cachedBeepClip != null)
            {
                countdownAudioSource.clip = cachedBeepClip;
            }
        }

        private void PlayCountdownBeep(float pitch = 1.0f)
        {
            EnsureBeepAudioSource();

            if (countdownAudioSource != null && cachedBeepClip != null)
            {
                countdownAudioSource.pitch = pitch;
                countdownAudioSource.PlayOneShot(cachedBeepClip, 0.95f);
            }
        }

        public void StartExplodeCountdown(float delay, bool fromNetwork = false)
        {
            if (state == PetState.Dead) return;

            if (delay < 0f)
            {
                CancelExplodeCountdown(true);
                return;
            }

            if (!fromNetwork && PhotonNetwork.InRoom)
            {
                PhotonView pv = GetComponent<PhotonView>();
                if (pv != null)
                {
                    var packet = new PetExplodePacket { PetViewID = pv.ViewID, Delay = delay };
                    RepoSteamNetwork.SendPacket(packet, NetworkDestination.EveryoneExcludingSender);
                }
            }

            if (delay == 0f)
            {
                Explode(fromNetwork);
                return;
            }

            if (explodeCountdownCoroutine != null)
                StopCoroutine(explodeCountdownCoroutine);

            explodeCountdownCoroutine = StartCoroutine(ExplodeCountdownRoutine(delay));
        }

        public void CancelExplodeCountdown(bool fromNetwork = false)
        {
            if (explodeCountdownCoroutine != null)
            {
                StopCoroutine(explodeCountdownCoroutine);
                explodeCountdownCoroutine = null;
            }

            HideCountdownTag();

            if (countdownAudioSource != null)
                countdownAudioSource.Stop();

            if (!fromNetwork && PhotonNetwork.InRoom)
            {
                PhotonView pv = GetComponent<PhotonView>();
                if (pv != null)
                {
                    var packet = new PetExplodePacket { PetViewID = pv.ViewID, Delay = -1f };
                    RepoSteamNetwork.SendPacket(packet, NetworkDestination.EveryoneExcludingSender);
                }
            }

            Plugin.Log.LogInfo("[Ai-Chan] Explosion countdown cancelled.");
        }

        private IEnumerator ExplodeCountdownRoutine(float totalSeconds)
        {
            EnsureCountdownTagCreated();
            EnsureBeepAudioSource();

            float remaining = totalSeconds;
            float nextBeepTime = 0f;

            while (remaining > 0f)
            {
                if (state == PetState.Dead)
                {
                    HideCountdownTag();
                    yield break;
                }

                if (countdownTextMesh != null)
                {
                    countdownTextMesh.text = $"⚠️ {remaining:F1}s ⚠️";
                }

                if (Time.time >= nextBeepTime)
                {
                    float progress = 1f - (remaining / Mathf.Max(totalSeconds, 0.01f));
                    float interval = Mathf.Lerp(0.85f, 0.18f, progress);
                    float pitch = Mathf.Lerp(1.05f, 1.45f, progress);

                    PlayCountdownBeep(pitch);
                    nextBeepTime = Time.time + interval;
                }

                remaining -= Time.deltaTime;
                yield return null;
            }

            HideCountdownTag();
            explodeCountdownCoroutine = null;
            Explode(true);
        }

        private void EnsureCountdownTagCreated()
        {
            if (countdownTagObject == null)
            {
                countdownTagObject = new GameObject("AiChanCountdownTag");
                countdownTagObject.transform.SetParent(transform, false);
                countdownTagObject.transform.localPosition = new Vector3(0f, 1.95f, 0f);

                countdownTextMesh = countdownTagObject.AddComponent<TextMesh>();
                countdownTextMesh.fontSize = 42;
                countdownTextMesh.characterSize = 0.045f;
                countdownTextMesh.alignment = TextAlignment.Center;
                countdownTextMesh.anchor = TextAnchor.MiddleCenter;
                countdownTextMesh.color = Color.red;

                countdownTagObject.AddComponent<PetNameBillboard>();
            }

            countdownTagObject.SetActive(true);
        }

        private void HideCountdownTag()
        {
            if (countdownTagObject != null)
            {
                countdownTagObject.SetActive(false);
            }
        }

        public void Explode(bool fromNetwork = false)
        {
            if (state == PetState.Dead) return;

            HideCountdownTag();

            if (explodeCountdownCoroutine != null)
            {
                StopCoroutine(explodeCountdownCoroutine);
                explodeCountdownCoroutine = null;
            }

            if (!fromNetwork && PhotonNetwork.InRoom)
            {
                PhotonView pv = GetComponent<PhotonView>();
                if (pv != null)
                {
                    var packet = new PetExplodePacket { PetViewID = pv.ViewID, Delay = 0f };
                    RepoSteamNetwork.SendPacket(packet, NetworkDestination.EveryoneExcludingSender);
                }
            }

            DropItemAtFeet();
            StopMoving();

            float force = PetSettings.ExplosionForce != null ? PetSettings.ExplosionForce.Value : 4.0f;
            float radius = PetSettings.ExplosionRadius != null ? PetSettings.ExplosionRadius.Value : 1.2f;

            SpawnNativeExplosion(radius, force);

            bool isPhysicsAuthority = !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
            if (!isPhysicsAuthority) return;

            if (agent != null && agent.enabled)
                agent.enabled = false;

            ApplyPhysicalExplosionImpulse(transform.position, radius * 2.5f, force);

            if (myRigidbody != null)
            {
                myRigidbody.isKinematic = false;
                myRigidbody.useGravity = true;
                myRigidbody.constraints = RigidbodyConstraints.None;

                float launchPower = 4f * force;
                Vector3 launchDir = (Vector3.up * 1.8f + UnityEngine.Random.insideUnitSphere * 0.4f).normalized;

                myRigidbody.AddForce(launchDir * launchPower, ForceMode.Impulse);
                myRigidbody.AddTorque(UnityEngine.Random.insideUnitSphere * (launchPower * 1.5f), ForceMode.Impulse);
            }

            float baseStandUpDelay = PetSettings.StandUpDelay != null ? PetSettings.StandUpDelay.Value : 2.0f;
            float totalStunTime = baseStandUpDelay + 3.0f;

            state = PetState.Stunned;
            stunEndsAt = Time.time + totalStunTime;

            Plugin.Log.LogInfo($"[Ai-Chan] KABOOM! Explosion executed (Radius: {radius:F1}m, Force: {force:F1}x).");
        }

        private void ApplyPhysicalExplosionImpulse(Vector3 epicenter, float radius, float forceMulti)
        {
            Collider[] colliders = Physics.OverlapSphere(epicenter, radius);
            HashSet<Rigidbody> affectedBodies = new HashSet<Rigidbody>();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;

                Rigidbody rb = col.GetComponentInParent<Rigidbody>() ?? col.attachedRigidbody;
                if (rb != null && !rb.isKinematic && !affectedBodies.Contains(rb))
                {
                    affectedBodies.Add(rb);
                    rb.AddExplosionForce(forceMulti * 60f, epicenter, radius, 1.2f, ForceMode.Impulse);
                }
            }
        }

        private void SpawnNativeExplosion(float radius, float force)
        {
            Vector3 pos = transform.position;

            int playerDmg = PetSettings.ExplosionPlayerDamage != null ? PetSettings.ExplosionPlayerDamage.Value : 75;
            int enemyDmg = PetSettings.ExplosionEnemyDamage != null ? PetSettings.ExplosionEnemyDamage.Value : 160;

            // Fator de conversão métrica para o sistema da prefab do jogo
            float gameScaleFactor = radius * 1.25f;

            ParticleScriptExplosion template = Resources.FindObjectsOfTypeAll<ParticleScriptExplosion>()
                .FirstOrDefault(p => p != null && p.explosionPreset != null);

            if (template == null)
            {
                ItemGrenadeExplosive grenade = Resources.FindObjectsOfTypeAll<ItemGrenadeExplosive>().FirstOrDefault();
                if (grenade != null)
                {
                    template = grenade.GetComponent<ParticleScriptExplosion>();
                }
            }

            ParticlePrefabExplosion ppe = null;

            if (template != null)
            {
                GameObject explosionPrefab = AccessTools.Field(typeof(ParticleScriptExplosion), "explosionPrefab")?.GetValue(template) as GameObject;
                if (explosionPrefab == null)
                {
                    explosionPrefab = Resources.Load<GameObject>("Effects/Part Prefab Explosion");
                    AccessTools.Field(typeof(ParticleScriptExplosion), "explosionPrefab")?.SetValue(template, explosionPrefab);
                }

                if (owner != null)
                {
                    AccessTools.Field(typeof(ParticleScriptExplosion), "playerCausingHurtOverride")?.SetValue(template, owner);
                }
                AccessTools.Field(typeof(ParticleScriptExplosion), "playerHitFullRuckusOverride")?.SetValue(template, true);

                ppe = template.Spawn(pos, gameScaleFactor, playerDmg, enemyDmg, force);
            }
            else
            {
                GameObject prefab = Resources.Load<GameObject>("Effects/Part Prefab Explosion");
                if (prefab != null)
                {
                    GameObject explosionObj = Instantiate(prefab, pos, Quaternion.identity);
                    ppe = explosionObj.GetComponent<ParticlePrefabExplosion>();
                    if (ppe != null)
                    {
                        AccessTools.Field(typeof(ParticlePrefabExplosion), "explosionSize")?.SetValue(ppe, gameScaleFactor);
                        AccessTools.Field(typeof(ParticlePrefabExplosion), "explosionDamage")?.SetValue(ppe, playerDmg);
                        AccessTools.Field(typeof(ParticlePrefabExplosion), "explosionDamageEnemy")?.SetValue(ppe, enemyDmg);
                        ppe.forceMultiplier = force;

                        if (owner != null)
                        {
                            AccessTools.Field(typeof(ParticlePrefabExplosion), "playerCausingHurt")?.SetValue(ppe, owner);
                        }
                        AccessTools.Field(typeof(ParticlePrefabExplosion), "playerHitFullRuckus")?.SetValue(ppe, true);
                    }
                }
            }

            // Garante que o colisor de dano cubra a área e acerte alvos estáticos imediatamente
            if (ppe != null && ppe.HurtCollider != null)
            {
                ppe.HurtCollider.transform.localScale = Vector3.one * gameScaleFactor;
                ppe.HurtCollider.physHitForce = (float)playerDmg * 0.5f * force;
                ppe.HurtCollider.playerHitForce *= force;

                Physics.SyncTransforms();
            }

            if (GameDirector.instance != null)
            {
                float shakeDist = radius * 3f;
                GameDirector.instance.CameraImpact?.ShakeDistance(10f * Mathf.Clamp(force * 0.25f, 0.5f, 3f), shakeDist * 0.5f, shakeDist, pos, 0.2f);
                GameDirector.instance.CameraShake?.ShakeDistance(5f * Mathf.Clamp(force * 0.25f, 0.5f, 3f), shakeDist * 0.5f, shakeDist, pos, 0.5f);
            }
        }
    }
}