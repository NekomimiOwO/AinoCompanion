using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;
using UnityEngine;

namespace ElsaPetMod
{
    public class PetInteraction : MonoBehaviour
    {
        public PetCompanionController pet;

        public float petDistance = 3.0f;
        public float itemDistance = 3.5f;
        public float petCooldown = 1.0f;

        private float nextPetTime;

        private static readonly FieldInfo GrabbedPhysGrabObjectField =
            AccessTools.Field(
                typeof(PhysGrabber),
                "grabbedPhysGrabObject"
            );

        private static readonly FieldInfo PlayerTumbleField =
            AccessTools.Field(
                typeof(PlayerAvatar),
                "tumble"
            );

        private static readonly FieldInfo PlayerIsTumblingField =
            AccessTools.Field(
                typeof(PlayerAvatar),
                "isTumbling"
            );

        private static readonly FieldInfo TumbleIsTumblingField =
            AccessTools.Field(
                typeof(PlayerTumble),
                "isTumbling"
            );

        private static readonly FieldInfo TumblePlayerAvatarField =
            AccessTools.Field(
                typeof(PlayerTumble),
                "playerAvatar"
            );

        private static readonly FieldInfo PlayerNameField =
            AccessTools.Field(
                typeof(PlayerAvatar),
                "playerName"
            );

        private void Start()
        {
            if (pet == null)
                pet = GetComponent<PetCompanionController>();
        }

        private bool IsLookingAtPet()
        {
            PlayerAvatar player = SemiFunc.PlayerAvatarLocal();

            if (player == null || player.localCamera == null)
                return false;

            Transform cameraTransform = player.localCamera.GetOverrideTransform();

            if (cameraTransform == null)
                return false;

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

            // Ignora o seu próprio corpo e os volumes da sala para o raio não ser bloqueado pelo seu nariz
            int ignoreMask = ~LayerMask.GetMask("Player", "PlayerOnlyCollision", "RoomVolume");

            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    petDistance,
                    ignoreMask,
                    QueryTriggerInteraction.Collide))
            {
                return false;
            }

            if (hit.collider == null)
                return false;

            PetCompanionController hitPet = hit.collider.GetComponentInParent<PetCompanionController>();

            return hitPet != null && hitPet == pet;
        }

        private PhysGrabObject GetPlayerHeldItem(PlayerAvatar player)
        {
            if (player == null || player.physGrabber == null)
                return null;

            return GrabbedPhysGrabObjectField?.GetValue(
                player.physGrabber
            ) as PhysGrabObject;
        }

        private void ReleasePlayerItem(PlayerAvatar player)
        {
            if (player == null || player.physGrabber == null)
                return;

            player.physGrabber.ReleaseObjectRPC(true, 1f, -1);
        }

        private PlayerTumble GetPlayerTumble(PlayerAvatar player)
        {
            if (player == null)
                return null;

            return PlayerTumbleField?.GetValue(player) as PlayerTumble
                ?? player.GetComponentInChildren<PlayerTumble>();
        }

        private bool IsPlayerTumbling(PlayerAvatar player)
        {
            if (player == null)
                return false;

            if (PlayerIsTumblingField?.GetValue(player) is bool avatarTumbling &&
                avatarTumbling)
            {
                return true;
            }

            PlayerTumble tumble = GetPlayerTumble(player);

            return tumble != null &&
                   TumbleIsTumblingField?.GetValue(tumble) is bool tumbleTumbling &&
                   tumbleTumbling;
        }

        private void Update()
        {
            if (pet == null)
                return;

            PlayerAvatar player = SemiFunc.PlayerAvatarLocal();

            if (player == null)
                return;

            KeyCode petKey = PetSettings.GetPetKeyCode();
            KeyCode itemKey = PetSettings.GetInteractKeyCode();
            KeyCode switchKey = PetSettings.GetSwitchOwnerKeyCode();

            bool switchPressed = Input.GetKeyDown(switchKey);
            bool petPressed = Input.GetKeyDown(petKey);
            bool itemPressed = Input.GetKeyDown(itemKey);

            if (switchPressed)
            {
                bool isOwner =
                    pet.owner != null &&
                    player.photonView != null &&
                    pet.owner.photonView != null &&
                    pet.owner.photonView.ControllerActorNr ==
                    player.photonView.ControllerActorNr;

                if (isOwner)
                {
                    if (PhotonNetwork.InRoom &&
                        !PhotonNetwork.IsMasterClient)
                    {
                        RepoSteamNetwork.SendPacket(
                            new PetSwitchOwnerPacket(),
                            NetworkDestination.HostOnly
                        );
                    }
                    else
                    {
                        pet.ManualSwitchOwner();
                    }
                }
                else
                {
                    string ownerName = "Ninguém";

                    if (pet.owner != null)
                    {
                        ownerName =
                            PlayerNameField?.GetValue(pet.owner) as string
                            ?? "Player";
                    }

                    Plugin.LogDebug(
                        Plugin.LogCategory.AiInteract,
                        $"Denied: Only the current owner ({ownerName}) " +
                        "can transfer the Pet using F5."
                    );
                }
            }

            if (!petPressed && !itemPressed)
                return;

            Vector3 playerPosition = player.transform.position;
            Vector3 petPosition = transform.position;
            Vector3 difference = playerPosition - petPosition;

            float distanceSqr = difference.sqrMagnitude;

            if (petPressed &&
                distanceSqr <= petDistance * petDistance &&
                Time.time >= nextPetTime)
            {
                if (IsLookingAtPet())
                {
                    nextPetTime = Time.time + petCooldown;

                    Plugin.LogDebug(
                        Plugin.LogCategory.AiInteract,
                        $"Petting triggered via key: {petKey}"
                    );

                    pet.Pet();
                }
            }

            if (!itemPressed ||
                distanceSqr > itemDistance * itemDistance)
            {
                return;
            }

            PhysGrabObject heldItem = GetPlayerHeldItem(player);

            if (heldItem != null)
            {
                PlayerTumble friendTumble =
                    heldItem.GetComponent<PlayerTumble>()
                    ?? heldItem.GetComponentInParent<PlayerTumble>();

                PlayerAvatar friendAvatar = null;

                if (friendTumble != null)
                {
                    friendAvatar =
                        TumblePlayerAvatarField?.GetValue(friendTumble)
                        as PlayerAvatar
                        ?? friendTumble.playerAvatar;
                }

                if (friendAvatar != null)
                {
                    if (pet.TryCarryPlayer(friendAvatar))
                        ReleasePlayerItem(player);
                }
                else
                {
                    // LÓGICA DA CABEÇA MORTA: Bypass de peso

                    bool isDeadHead = heldItem.name.Contains("Player Death Head") || heldItem.GetComponent("PlayerDeathHead") != null;

                    if (isDeadHead)
                    {
                        if (PetSettings.EnableCarryingDeadPlayers != null && !PetSettings.EnableCarryingDeadPlayers.Value)
                            return; // Aborta se a configuração estiver desligada

                        float maxAllowed = PetSettings.MaxMass != null ? PetSettings.MaxMass.Value : 3f;

                        // Engana o limite de peso para a Ai-Chan aceitar o corpo
                        if (heldItem.massOriginal > maxAllowed)
                        {
                            heldItem.massOriginal = maxAllowed;
                            Rigidbody itemRb = heldItem.GetComponent<Rigidbody>();
                            if (itemRb != null) itemRb.mass = maxAllowed;
                        }
                    }

                    if (pet.TryGiveItem(heldItem))
                    {
                        ReleasePlayerItem(player);
                    }
                }
            }
            else if (IsPlayerTumbling(player))
            {
                pet.TryCarryPlayer(player);
            }
        }
    }
}