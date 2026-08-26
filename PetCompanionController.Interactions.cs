using System.Reflection;
using Photon.Pun;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;
using UnityEngine;

namespace ElsaPetMod
{
    public partial class PetCompanionController
    {
        public ParticleSystem heartParticles;
        public float petDuration = 2.6f;

        private MonoBehaviour nativePetVfxComponent;
        private MethodInfo nativePetVfxMethod;
        private ParticleSystem[] fallbackHeartParticleSystems;
        private bool heartVfxCached;
        private float petEndsAt;
        private float stunEndsAt;
        private PetState stateBeforePetting;

        private void TickPetting()
        {
            // ABORTO DE EMERGÊNCIA: Se a explosão ou dano mudou o estado dela, encerra o carinho silenciosamente!
            if (state != PetState.Petting) return;

            StopMoving();

            if (Time.time < petEndsAt)
                return;

            // Só devolve ao estado normal se ela sobreviveu ilesa aos 2.6 segundos de carinho
            if (state == PetState.Petting)
            {
                state = stateBeforePetting == PetState.Dead || stateBeforePetting == PetState.Grabbed
                    ? PetState.FollowOwner
                    : stateBeforePetting;
            }
        }

        private void CacheHeartVfx()
        {
            if (heartVfxCached)
                return;

            heartVfxCached = true;

            if (visualRoot == null)
                return;

            MonoBehaviour[] components =
                visualRoot.GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];

                if (component == null ||
                    component.GetType().Name != "EnemyElsaAnim")
                {
                    continue;
                }

                MethodInfo method = component.GetType().GetMethod(
                    "VFXPetParticles",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic
                );

                if (method == null)
                    continue;

                nativePetVfxComponent = component;
                nativePetVfxMethod = method;
                return;
            }

            fallbackHeartParticleSystems =
                visualRoot.GetComponentsInChildren<ParticleSystem>(true);
        }

        public void Pet()
        {
            // PROTEÇÃO: Impede acúmulo de carinhos ou interrupção de morte/explosão
            if (state == PetState.Dead || state == PetState.Grabbed || state == PetState.Stunned || state == PetState.Petting)
                return;

            stateBeforePetting = state;
            state = PetState.Petting;
            petEndsAt = Time.time + petDuration;
            StopMoving();

            if (aiAudio != null)
                aiAudio.PlayBark();

            if (animator != null)
            {
                animator.ResetTrigger(PetTriggerHash);
                animator.SetTrigger(PetTriggerHash);
            }

            TriggerHeartParticles();

            if (PhotonNetwork.InRoom)
            {
                var packet = new PetSyncPettingPacket { PetViewID = GetComponent<PhotonView>().ViewID };
                RepoSteamNetwork.SendPacket(packet, NetworkDestination.EveryoneExcludingSender);
            }
        }

        public void PetFromNetwork()
        {
            if (state == PetState.Dead || state == PetState.Grabbed || state == PetState.Stunned || state == PetState.Petting)
                return;

            stateBeforePetting = state;
            state = PetState.Petting;
            petEndsAt = Time.time + petDuration;
            StopMoving();

            if (aiAudio != null)
                aiAudio.PlayBark();

            if (animator != null)
            {
                animator.ResetTrigger(PetTriggerHash);
                animator.SetTrigger(PetTriggerHash);
            }

            TriggerHeartParticles();
        }

        private void TriggerHeartParticles()
        {
            CacheHeartVfx();

            if (nativePetVfxComponent != null &&
                nativePetVfxMethod != null)
            {
                try
                {
                    nativePetVfxMethod.Invoke(nativePetVfxComponent, null);

                    Plugin.LogDebug(
                        Plugin.LogCategory.AiInteract,
                        "Native VFXPetParticles executed."
                    );

                    return;
                }
                catch (System.Exception exception)
                {
                    Plugin.Log.LogWarning(
                        "[Ai-Chan] Fail to execute VFXPetParticles: " +
                        exception.Message
                    );

                    // Evita repetir uma reflexão que já falhou.
                    nativePetVfxComponent = null;
                    nativePetVfxMethod = null;
                }
            }

            if (fallbackHeartParticleSystems == null)
                return;

            for (int i = 0; i < fallbackHeartParticleSystems.Length; i++)
            {
                ParticleSystem particles = fallbackHeartParticleSystems[i];

                if (particles == null)
                    continue;

                if (!particles.gameObject.activeSelf)
                    particles.gameObject.SetActive(true);

                particles.Play(true);
            }
        }

        public void TakeDamage(float damage, Vector3 forceDirection, float pushForce = 5f, float stunDuration = 2f)
        {
            if (state == PetState.Dead)
                return;

            currentHealth -= damage;
            DropItemAtFeet();

            Plugin.LogDebug(Plugin.LogCategory.AiInteract, $"Damage received: {damage} | Health: {currentHealth}/{maxHealth}");

            if (currentHealth <= 0f)
            {
                MarkDead();
                return;
            }

            if (myRigidbody != null)
            {
                myRigidbody.isKinematic = false;
                myRigidbody.useGravity = true;
                myRigidbody.AddForce(forceDirection * pushForce, ForceMode.Impulse);
            }

            state = PetState.Stunned;
            stunEndsAt = Time.time + stunDuration;
            StopMoving();
        }

        private void TickStun()
        {
            StopMoving();

            if (Time.time < stunEndsAt)
                return;

            isPlayingDead = false;

            if (myRigidbody != null)
            {
                myRigidbody.isKinematic = true;
                myRigidbody.useGravity = false;
                myRigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }

            if (SnapToNavMesh())
                state = PetState.FollowOwner;
        }

        public void MarkDead()
        {
            DropItemAtFeet();
            StopMoving();
            state = PetState.Dead;

            if (myRigidbody != null)
            {
                myRigidbody.isKinematic = false;
                myRigidbody.useGravity = true;
            }
        }
    }
}