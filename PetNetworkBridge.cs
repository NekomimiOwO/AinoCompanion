using System;
using Photon.Pun;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;
using UnityEngine;

namespace ElsaPetMod
{
    public sealed class PetNetworkBridge : MonoBehaviourPunCallbacks
    {
        private const float RemoteMovingTimeout = 0.08f;
        private const float SnapDistance = 3.0f;
        private const float PositionThresholdSqr = 0.000025f;

        private bool receivedFirstState;

        private PetCompanionController controller;
        private PhotonTransformView ptv;
        private Rigidbody rb;

        private int sendSequence;

        private Vector3 networkPosition;
        private Quaternion networkRotation;
        private int lastReceivedSequence = -1;

        private Vector3 lastPos;
        private float remoteMoveTimer;

        public bool IsRemoteMoving { get; private set; }

        private void Awake()
        {
            controller = GetComponent<PetCompanionController>();
            ptv = GetComponent<PhotonTransformView>();
            rb = GetComponent<Rigidbody>();

            lastPos = transform.position;
            networkPosition = transform.position;
            networkRotation = transform.rotation;
        }

        public override void OnEnable()
        {
            base.OnEnable();

            RepoSteamNetwork.AddCallback<PetStatePacket>(OnStatePacketReceived);
            RepoSteamNetwork.AddCallback<PetGiveItemPacket>(OnGiveItemPacketReceived);
            RepoSteamNetwork.AddCallback<PetCarryPlayerPacket>(OnCarryPlayerPacketReceived);
            RepoSteamNetwork.AddCallback<PetSyncCarryPacket>(OnSyncCarryPacketReceived);
            RepoSteamNetwork.AddCallback<PetSyncPettingPacket>(OnSyncPettingPacketReceived);
            RepoSteamNetwork.AddCallback<PetSwitchOwnerPacket>(OnSwitchOwnerPacketReceived);
            RepoSteamNetwork.AddCallback<PetExplodePacket>(OnExplodePacketReceived);
        }

        public override void OnDisable()
        {
            RepoSteamNetwork.RemoveCallback<PetStatePacket>(OnStatePacketReceived);
            RepoSteamNetwork.RemoveCallback<PetGiveItemPacket>(OnGiveItemPacketReceived);
            RepoSteamNetwork.RemoveCallback<PetCarryPlayerPacket>(OnCarryPlayerPacketReceived);
            RepoSteamNetwork.RemoveCallback<PetSyncCarryPacket>(OnSyncCarryPacketReceived);
            RepoSteamNetwork.RemoveCallback<PetSyncPettingPacket>(OnSyncPettingPacketReceived);
            RepoSteamNetwork.RemoveCallback<PetSwitchOwnerPacket>(OnSwitchOwnerPacketReceived);
            RepoSteamNetwork.RemoveCallback<PetExplodePacket>(OnExplodePacketReceived);

            base.OnDisable();
        }

        private bool IsPacketForThisPet(int petViewId)
        {
            return photonView != null &&
                   photonView.ViewID > 0 &&
                   photonView.ViewID == petViewId;
        }

        private void OnSwitchOwnerPacketReceived(PetSwitchOwnerPacket packet)
        {
            if (!PhotonNetwork.IsMasterClient || controller == null)
                return;

            controller.ManualSwitchOwner();
        }

        private void OnGiveItemPacketReceived(PetGiveItemPacket packet)
        {
            if (!PhotonNetwork.IsMasterClient ||
                controller == null ||
                !IsPacketForThisPet(packet.PetViewID))
            {
                return;
            }

            PhotonView itemView = PhotonView.Find(packet.ItemViewID);
            PhysGrabObject item = itemView != null
                ? itemView.GetComponent<PhysGrabObject>()
                : null;

            if (item != null)
                controller.TryGiveItem(item);
        }

        private void OnCarryPlayerPacketReceived(PetCarryPlayerPacket packet)
        {
            if (!PhotonNetwork.IsMasterClient ||
                controller == null ||
                !IsPacketForThisPet(packet.PetViewID))
            {
                return;
            }

            PhotonView playerView = PhotonView.Find(packet.PlayerViewID);
            PlayerAvatar targetPlayer = playerView != null
                ? playerView.GetComponent<PlayerAvatar>()
                : null;

            if (targetPlayer != null)
                controller.TryCarryPlayer(targetPlayer);
        }

        private void OnSyncCarryPacketReceived(PetSyncCarryPacket packet)
        {
            if (controller == null || !IsPacketForThisPet(packet.PetViewID))
                return;

            controller.NetworkSyncCarry(
                packet.TargetViewID,
                packet.IsPlayer,
                packet.IsPickingUp,
                packet.InheritScale);
        }

        private void OnSyncPettingPacketReceived(PetSyncPettingPacket packet)
        {
            if (controller == null || !IsPacketForThisPet(packet.PetViewID))
                return;

            controller.PetFromNetwork();
        }

        private void OnExplodePacketReceived(PetExplodePacket packet)
        {
            if (controller == null || !IsPacketForThisPet(packet.PetViewID))
                return;

            controller.StartExplodeCountdown(packet.Delay, true);
        }


        private void OnStatePacketReceived(PetStatePacket packet)
        {
            if (!IsPacketForThisPet(packet.PetViewID))
                return;

            if (!PhotonNetwork.IsMasterClient)
            {
                PetNetworkProfiler.RecordReceive(PetNetworkProfiler.EstimatedStatePacketSize);
            }

            if (PhotonNetwork.IsMasterClient)
                return;

            if (lastReceivedSequence >= 0 && !IsSequenceNewer(packet.Sequence, lastReceivedSequence))
                return;

            lastReceivedSequence = packet.Sequence;
            networkPosition = packet.Position;
            networkRotation = packet.Rotation;

            if (controller == null)
                return;

            if (Enum.IsDefined(typeof(PetCompanionController.PetState), packet.StateIndex) && !controller.IsRecovering)
            {
                controller.state = (PetCompanionController.PetState)packet.StateIndex;
            }

            // O cliente pode ter spawnado no ponto do chão/local NavMesh.
            // O primeiro estado do host precisa prevalecer imediatamente, inclusive no eixo Y.
            if (!receivedFirstState)
            {
                receivedFirstState = true;
                controller.SnapNetworkInterpolation(
                    packet.Position,
                    packet.Rotation
                );
            }
            else
            {
                controller.NetworkSyncTransform(
                    packet.Position,
                    packet.Rotation
                );
            }
        }

        private static bool IsSequenceNewer(int sequence, int previous)
        {
            return sequence != previous &&
                   (sequence - previous > 0);
        }

        private bool IsRemotePet()
        {
            return PhotonNetwork.InRoom &&
                   !PhotonNetwork.OfflineMode &&
                   !PhotonNetwork.IsMasterClient;
        }
        private void Update()
        {
            if (IsRemotePet())
            {
                // PhotonTransformView continua amordaçado para não brigar com a Steam
                if (ptv != null && ptv.enabled)
                {
                    ptv.enabled = false;
                }

                if (controller != null && controller.agent != null && controller.agent.enabled)
                {
                    controller.agent.enabled = false;
                }

                if (rb != null && !rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }

                // A INTERPOLAÇÃO NUNCA PARA:
                // Mesmo quando a sua mão pegar a pet, é o Host quem simula ela subindo para a mão.
                // O Client só recebe os pacotes da Steam no ar e exibe fluidamente.
                if (controller != null)
                {
                    controller.UpdateRemoteNetworkInterpolation();
                }
            }

            float movementSqr = (transform.position - lastPos).sqrMagnitude;
            lastPos = transform.position;

            if (movementSqr > PositionThresholdSqr)
                remoteMoveTimer = RemoteMovingTimeout;
            else
                remoteMoveTimer -= Time.deltaTime;

            IsRemoteMoving = remoteMoveTimer > 0f;

            if (!PhotonNetwork.InRoom ||
                !PhotonNetwork.IsConnectedAndReady ||
                !PhotonNetwork.IsMasterClient)
            {
                return;
            }

            if (controller != null && controller.ShouldSendNetworkTransform())
            {
                SendStateSnapshot();
            }
        }

        private void SendStateSnapshot()
        {
            if (photonView == null ||
                photonView.ViewID <= 0 ||
                controller == null)
            {
                return;
            }

            sendSequence++;

            int ownerId = controller.owner != null &&
                          controller.owner.photonView != null
                ? controller.owner.photonView.ViewID
                : 0;

            PetStatePacket packet = new PetStatePacket
            {
                PetViewID = photonView.ViewID,
                OwnerViewID = ownerId,
                StateIndex = (int)controller.state,
                Sequence = sendSequence,
                Position = transform.position,
                Rotation = transform.rotation
            };

            RepoSteamNetwork.SendPacket(
                packet,
                NetworkDestination.EveryoneExcludingSender);

            PetNetworkProfiler.RecordSend(PetNetworkProfiler.EstimatedStatePacketSize);
        }
    }
}