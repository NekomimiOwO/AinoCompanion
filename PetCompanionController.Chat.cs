using System;
using System.Collections;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ElsaPetMod
{
    public partial class PetCompanionController
    {
        public bool isCalledByOwner;
        public float awayTimer;
        public bool isPlayingDead;

        public void CallPetToOwner()
        {
            if (state == PetState.Dead || state == PetState.Stunned || state == PetState.Grabbed)
                return;

            isCalledByOwner = true;
            awayTimer = 0f;
            if (aiAudio != null) aiAudio.PlayBark();
        }

        public bool TryGetLookAheadGroundPoint(float defaultDistance, out Vector3 point)
        {
            point = default;

            Camera cam = Camera.main;
            if (cam == null)
                return false;

            int mask = GetGroundMask();
            Vector3 targetPoint;

            // 1. Tenta fazer o Raycast exato para onde a câmera está apontando
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 40f, mask, QueryTriggerInteraction.Ignore))
            {
                targetPoint = hit.point;
            }
            else
            {
                // 2. Fallback caso olhe para o céu ou além do alcance
                Vector3 flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                if (flatForward.sqrMagnitude < 0.001f)
                    return false;

                targetPoint = cam.transform.position + flatForward.normalized * defaultDistance;
            }

            // 3. Encontra a NavMesh válida mais próxima do ponto
            if (!UnityEngine.AI.NavMesh.SamplePosition(targetPoint, out UnityEngine.AI.NavMeshHit navHit, 4.0f, UnityEngine.AI.NavMesh.AllAreas))
            {
                Plugin.Log.LogWarning("[Ai-Chan] Command go: area outside the map or blocked.");
                return false;
            }

            point = navHit.position;
            return true;
        }

        public void CommandJump()
        {
            if (state == PetState.Dead || state == PetState.Stunned || state == PetState.Grabbed || isJumping)
                return;

            StartCoroutine(PerformAutoJump(transform.position + transform.forward * 1.5f + Vector3.up * 0.5f));
            if (aiAudio != null) aiAudio.PlayBark();
        }

        public Vector3 awayTargetPos;

        public void CommandAway()
        {
            if (state == PetState.Dead || state == PetState.Stunned || state == PetState.Grabbed)
                return;

            if (owner == null) return;

            isCalledByOwner = false;

            Vector3 diff = transform.position - owner.transform.position;
            diff.y = 0f;

            Vector3 awayDir = diff.sqrMagnitude < 0.01f ? -transform.forward : diff.normalized;
            awayTargetPos = transform.position + awayDir * 5.0f;

            if (UnityEngine.AI.NavMesh.SamplePosition(awayTargetPos, out UnityEngine.AI.NavMeshHit hit, 4f, UnityEngine.AI.NavMesh.AllAreas))
            {
                awayTargetPos = hit.position;
            }

            awayTimer = 6.0f;

            if (aiAudio != null) aiAudio.PlayBark();
        }

        public void CommandPlayDead()
        {
            if (state == PetState.Dead || state == PetState.Grabbed)
                return;

            isPlayingDead = true;
            stunEndsAt = Time.time + 6.0f;
            state = PetState.Stunned;

            if (agent != null && agent.enabled)
            {
                agent.isStopped = true;
                agent.enabled = false;
            }

            if (myRigidbody != null)
            {
                myRigidbody.isKinematic = false;
                myRigidbody.useGravity = true;
                myRigidbody.constraints = RigidbodyConstraints.None;

                myRigidbody.AddForce(Vector3.down * 1.5f + transform.right * 2f, ForceMode.Impulse);
                myRigidbody.AddTorque(transform.forward * 5f, ForceMode.Impulse);
            }

            if (aiAudio != null) aiAudio.PlayBark();
        }

        public void ShowHelpInfo()
        {
            if (aiAudio != null) aiAudio.PlayBark();

            StopCoroutine(nameof(DisplayHelpRoutine));
            StartCoroutine(nameof(DisplayHelpRoutine));
        }

        private IEnumerator DisplayHelpRoutine()
        {
            float duration = 6.0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (ItemInfoExtraUI.instance != null)
                {
                    ItemInfoExtraUI.instance.ItemInfoText("Aino: come, jump, away, dead, small, big, normal, switch, net, go, explode", new Color(0.2f, 0.9f, 1f));
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private void ApplyScale(float multiplier)
        {
            if (visualRoot != null)
            {
                visualRoot.localScale = Vector3.one * multiplier;
            }
        }
    }

    [HarmonyPatch]
    internal static class PatchPlayerChatCommand
    {
        // 1. Hook para MULTIPLAYER (Disparado para todos via RPC da rede)
        [HarmonyPatch(typeof(PlayerAvatar), "ChatMessageSendRPC")]
        [HarmonyPostfix]
        private static void PostfixMultiplayer(PlayerAvatar __instance, string _message)
        {
            bool isOnlineRoom = PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;
            if (!isOnlineRoom) return;

            HandleChatCommand(__instance, _message);
        }

        // 2. Hook para SINGLEPLAYER (Compatível com SoloChat e modo offline)
        [HarmonyPatch(typeof(PlayerAvatar), "ChatMessageSpeak")]
        [HarmonyPostfix]
        private static void PostfixSingleplayer(PlayerAvatar __instance, string _message)
        {
            bool isOnlineRoom = PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;
            if (isOnlineRoom) return;

            HandleChatCommand(__instance, _message);
        }

        private static void HandleChatCommand(PlayerAvatar sender, string rawMessage)
        {
            if (sender == null || string.IsNullOrWhiteSpace(rawMessage))
                return;

            string msg = rawMessage.ToLowerInvariant().Trim();

            // Garante que a mensagem contém o nome da pet para acionar um comando
            if (!msg.Contains("aino") && !msg.Contains("ai-chan") && !msg.Contains("aichan") && !msg.Contains("pet"))
                return;

            PetCompanionController pet = UnityEngine.Object.FindObjectOfType<PetCompanionController>();
            if (pet == null) return;

            bool isSingleplayer = !PhotonNetwork.IsConnectedAndReady || !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode;
            bool isLocalPlayer = isSingleplayer || (sender == SemiFunc.PlayerAvatarLocal());

            bool isOwner = isSingleplayer || (pet.owner == null) || (pet.owner == sender) ||
                           (pet.owner.photonView != null && sender.photonView != null && pet.owner.photonView.ViewID == sender.photonView.ViewID);

            // 1. COMANDOS LOCAIS: Executam apenas no PC de quem digitou (UI e Profiler)
            if (isLocalPlayer)
            {
                if (msg.Contains("help") || msg.Contains("commands") || msg.Contains("ajuda"))
                {
                    Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Displaying help message on local player screen.");
                    pet.ShowHelpInfo();
                    return;
                }

                if (msg.Contains("net") || msg.Contains("rede"))
                {
                    Plugin.Log.LogInfo("Command (Local): Network Stats requested via chat.");
                    PetNetworkProfiler.Instance?.PrintStats("COMANDO CHAT");
                    return;
                }
            }

            // 2. COMANDOS DA AI (Executados no MasterClient no multiplayer ou direto no singleplayer)
            bool isMaster = isSingleplayer || PhotonNetwork.IsMasterClient;

            if (isMaster)
            {
                // Comandos LIBERADOS para todos os jogadores:
                if (msg.Contains("jump") || msg.Contains("pula") || msg.Contains("pule"))
                {
                    Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (General): Jump");
                    pet.CommandJump();
                    return;
                }

                // Comandos RESTRITOS ao dono atual:
                if (isOwner)
                {
                    if (msg.Contains("switch") || msg.Contains("pass") || msg.Contains("leave") || msg.Contains("troca"))
                    {
                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (Owner): Switch Owner");
                        pet.ManualSwitchOwner();
                    }
                    else if (msg.Contains("come") || msg.Contains("here") || msg.Contains("vem") || msg.Contains("aqui"))
                    {
                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (Owner): Come Here");
                        pet.CallPetToOwner();
                    }
                    else if (msg.Contains("away") || msg.Contains("sai") || msg.Contains("longe"))
                    {
                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (Owner): Away");
                        pet.CommandAway();
                    }
                    else if (msg.Contains("play dead") || msg.Contains("dead") || msg.Contains("morta") || msg.Contains("deita"))
                    {
                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (Owner): Play Dead");
                        pet.CommandPlayDead();
                    }
                    else if (msg.Contains("small") || msg.Contains("pequena") || msg.Contains("mini"))
                    {
                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (Owner): Small");
                        pet.SetScaleMultiplier(0.5f);
                    }
                    else if (msg.Contains("big") || msg.Contains("grande") || msg.Contains("gigante"))
                    {
                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (Owner): Big");
                        pet.SetScaleMultiplier(1.8f);
                    }
                    else if (msg.Contains("normal") || msg.Contains("padrao"))
                    {
                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (Owner): Normal");
                        pet.SetScaleMultiplier(1.0f);
                    }
                    else if (msg.Contains("explode") || msg.Contains("exploda") || msg.Contains("kaboom"))
                    {
                        if (PetSettings.EnableExplodeCommand != null && !PetSettings.EnableExplodeCommand.Value)
                        {
                            Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Explosion command disabled in settings.");
                            return;
                        }

                        float delay = 0f;
                        string[] words = msg.Split(' ');
                        foreach (string word in words)
                        {
                            if (float.TryParse(
                                    word.Replace(',', '.'),
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out float parsedDelay))
                            {
                                delay = Mathf.Clamp(parsedDelay, 1f, 30f);
                                break;
                            }
                        }

                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, $"Command (Owner): Explode (Countdown: {delay}s)");
                        pet.StartExplodeCountdown(delay);
                    }
                    else if (msg.Contains("drop") || msg.Contains("solta") || msg.Contains("release") || msg.Contains("larga"))
                    {
                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (Owner): Force Drop Item");
                        pet.ForceDropItem();
                    }
                    else if (msg.Contains("vai") || msg.Contains("walk") || msg.Contains("go") || msg.Contains("anda"))
                    {
                        float distance = 4f;

                        string[] words = msg.Split(' ');
                        foreach (string word in words)
                        {
                            if (float.TryParse(
                                    word.Replace(',', '.'),
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out float parsedDistance))
                            {
                                distance = Mathf.Clamp(parsedDistance, 0.5f, 20f);
                                break;
                            }
                        }

                        if (pet.TryGetLookAheadGroundPoint(distance, out Vector3 target))
                            pet.CommandMoveTo(target, 0f);

                        return;
                    }
                    else if (msg.Contains("para") || msg.Contains("stop") || msg.Contains("fica"))
                    {
                        Plugin.LogDebug(Plugin.LogCategory.AiInteract, "Command (Owner): Stop / Cancel Countdown");
                        pet.CancelManualMove();
                        pet.CancelExplodeCountdown();
                        return;
                    }
                    else if (msg.Contains("size") || msg.Contains("tamanho"))
                    {
                        string delimiter = msg.Contains("size") ? "size" : "tamanho";
                        string[] parts = msg.Split(new string[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1)
                        {
                            string numStr = parts[1].Trim().Split(' ')[0].Replace(',', '.');

                            if (float.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float newSize))
                            {
                                Plugin.LogDebug(Plugin.LogCategory.AiInteract, $"Command (Owner): Size {newSize}");
                                pet.SetScaleMultiplier(newSize);
                            }
                        }
                    }
                }
                else
                {
                    string playerName = AccessTools.Field(typeof(PlayerAvatar), "playerName")?.GetValue(sender) as string ?? "Player";
                    Plugin.LogDebug(Plugin.LogCategory.AiInteract, $"Player {playerName} tried to execute an owner-restricted command.");
                }
            }
        }
    }
}