using Photon.Pun;
using UnityEngine;
using System.Reflection;
using HarmonyLib;

namespace ElsaPetMod
{
    public partial class PetCompanionController
    {
        private float lastPacketArrivalTime;
        private float averagePacketDelay = 0.05f; 
        public const float NetworkMovingInterval = 0.05f;
        public const float NetworkStoppedInterval = 1.5f;

        private const float RemoteSnapDistance = 12.0f;

        private float lastNetworkSentTime;
        private Vector3 lastNetworkSentPosition;
        private Quaternion lastNetworkSentRotation;
        private bool hasNetworkSentTransform;

        private Vector3 networkTargetPosition;
        private Quaternion networkTargetRotation;
        private bool hasNetworkTarget;

        private float lastMoveTime;

        private bool IsNetworkClientOnly
        {
            get
            {
                return PhotonNetwork.InRoom &&
                       !PhotonNetwork.OfflineMode &&
                       !PhotonNetwork.IsMasterClient;
            }
        }

        private struct StateSnapshot
        {
            public Vector3 position;
            public Quaternion rotation;
            public float localTime;
        }
        private System.Collections.Generic.List<StateSnapshot> stateBuffer = new System.Collections.Generic.List<StateSnapshot>();

        public bool IsGrabbedByAnyPlayer()
        {
            if (myGrabObject == null)
                myGrabObject = GetComponent<PhysGrabObject>();

            return myGrabObject != null &&
                   myGrabObject.playerGrabbing != null &&
                   myGrabObject.playerGrabbing.Count > 0;
        }

        private bool IsCarryingForNetwork()
        {
            return state == PetState.CarryItemToCart &&
                   (carriedItem != null || carriedPlayerAvatar != null);
        }

        private bool IsNetworkMoving()
        {
            if (isJumping || state == PetState.Dead || IsGrabbedByAnyPlayer())
                return true;

            if (agent != null && agent.enabled &&
                agent.velocity.sqrMagnitude > 0.001f)
            {
                return true;
            }

            if (myRigidbody != null &&
                !myRigidbody.isKinematic &&
                myRigidbody.velocity.sqrMagnitude > 0.001f)
            {
                return true;
            }

            return (transform.position - lastNetworkSentPosition).sqrMagnitude > 0.0001f;
        }

        private void InitializeNetworkInterpolation()
        {
            networkTargetPosition = transform.position;
            networkTargetRotation = transform.rotation;
            hasNetworkTarget = true;
        }

        public void UpdateRemoteNetworkInterpolation()
        {
            if (!IsNetworkClientOnly || !hasNetworkTarget) return;

            if (Vector3.Distance(transform.position, networkTargetPosition) > RemoteSnapDistance)
            {
                transform.position = networkTargetPosition;
                transform.rotation = networkTargetRotation;
                stateBuffer.Clear(); // Limpa o buffer se teleportou
                return;
            }

            bool useSnapshot = PetSettings.EnableSnapshotInterpolation != null && PetSettings.EnableSnapshotInterpolation.Value;

            // ========================================================
            // TÉCNICA 1: SNAPSHOT INTERPOLATION (PADRÃO AAA)
            // ========================================================
            if (useSnapshot && stateBuffer.Count >= 2)
            {
                float bufferDelay = (PetSettings.SnapshotBufferMs != null ? PetSettings.SnapshotBufferMs.Value : 100f) / 1000f;
                float renderTime = Time.time - bufferDelay; // Vivemos no passado!

                // Busca as duas coordenadas exatas no histórico
                int indexA = -1;
                for (int i = stateBuffer.Count - 1; i >= 0; i--)
                {
                    if (stateBuffer[i].localTime <= renderTime)
                    {
                        indexA = i;
                        break;
                    }
                }

                if (indexA >= 0 && indexA < stateBuffer.Count - 1)
                {
                    // Interpolação perfeita entre dois pontos já recebidos
                    StateSnapshot pA = stateBuffer[indexA];
                    StateSnapshot pB = stateBuffer[indexA + 1];

                    float t = Mathf.InverseLerp(pA.localTime, pB.localTime, renderTime);
                    transform.position = Vector3.Lerp(pA.position, pB.position, t);
                    transform.rotation = Quaternion.Slerp(pA.rotation, pB.rotation, t);

                    // Limpa o lixo que já foi ultrapassado
                    if (indexA > 0) stateBuffer.RemoveRange(0, indexA);
                    return;
                }
                else if (indexA == stateBuffer.Count - 1)
                {
                    // Se o buffer secou (A internet travou e não recebemos nada novo)
                    // Fica agarrado no último pacote com suavização curta
                    transform.position = Vector3.Lerp(transform.position, stateBuffer[indexA].position, 15f * Time.deltaTime);
                    transform.rotation = Quaternion.Slerp(transform.rotation, stateBuffer[indexA].rotation, 15f * Time.deltaTime);
                    return;
                }
            }

            // ========================================================
            // TÉCNICA 2: FALLBACK (INTERPOLAÇÃO ADAPTATIVA)
            // ========================================================
            float posSpeed = 12f;
            float rotSpeed = 15f;

            if (PetSettings.EnableAdaptiveInterpolation != null && PetSettings.EnableAdaptiveInterpolation.Value)
            {
                float degradation = Mathf.InverseLerp(0.08f, 0.25f, averagePacketDelay);
                posSpeed = Mathf.Lerp(12f, 6f, degradation);
                rotSpeed = Mathf.Lerp(15f, 8f, degradation);
            }

            float posFactor = 1f - Mathf.Exp(-posSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, networkTargetPosition, posFactor);

            bool useAntiFlick = PetSettings.EnableAntiFlickRotation != null && PetSettings.EnableAntiFlickRotation.Value;
            if (useAntiFlick && Quaternion.Angle(transform.rotation, networkTargetRotation) > 100f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, networkTargetRotation, 800f * Time.deltaTime);
            else
                transform.rotation = Quaternion.Slerp(transform.rotation, networkTargetRotation, 1f - Mathf.Exp(-rotSpeed * Time.deltaTime));
        }

        public bool ShouldSendNetworkTransform()
        {
            if (!PhotonNetwork.InRoom ||
                PhotonNetwork.OfflineMode ||
                !PhotonNetwork.IsMasterClient)
            {
                return false;
            }

            bool moving =
                IsNetworkMoving() ||
                IsCarryingForNetwork() ||
                isJumping ||
                IsGrabbedByAnyPlayer();

            if (moving)
                lastMoveTime = Time.time;

            bool recentMovement = Time.time - lastMoveTime <= 1.5f;

            float interval = recentMovement
                ? NetworkMovingInterval
                : NetworkStoppedInterval;

            if (Time.time - lastNetworkSentTime < interval)
                return false;

            bool movedEnough =
                (transform.position - lastNetworkSentPosition).sqrMagnitude > 0.0001f;

            bool rotatedEnough =
                Quaternion.Angle(transform.rotation, lastNetworkSentRotation) > 0.5f;

            if (!moving &&
                !recentMovement &&
                !movedEnough &&
                !rotatedEnough &&
                hasNetworkSentTransform)
            {
                return false;
            }

            lastNetworkSentTime = Time.time;
            lastNetworkSentPosition = transform.position;
            lastNetworkSentRotation = transform.rotation;
            hasNetworkSentTransform = true;

            return true;
        }

        public void SnapNetworkInterpolation(Vector3 newPosition, Quaternion newRotation)
        {
            networkTargetPosition = newPosition;
            networkTargetRotation = newRotation;
            hasNetworkTarget = true;

            transform.position = newPosition;
            transform.rotation = newRotation;
        }

        public void NetworkSyncTransform(Vector3 position, Quaternion rotation)
        {
            if (!IsNetworkClientOnly) return;

            // --- NOVO: JITTER BUFFER (A Mágica do Tempo Local) ---
            float packetTime = Time.time;
            if (stateBuffer.Count > 0)
            {
                float lastPTime = stateBuffer[stateBuffer.Count - 1].localTime;
                // Se a internet engasgar e vomitar vários pacotes juntos, espaçamos eles em 50ms!
                // Isso recria a linha do tempo do Host sem precisar sincronizar relógios.
                packetTime = Mathf.Max(Time.time, lastPTime + NetworkMovingInterval);
            }

            stateBuffer.Add(new StateSnapshot { position = position, rotation = rotation, localTime = packetTime });

            // Proteção contra vazamento de memória (guarda no máximo 1 segundo de histórico)
            if (stateBuffer.Count > 20) stateBuffer.RemoveAt(0);
            // -----------------------------------------------------

            // Fallback (Adaptativo antigo)
            float currentDelay = Time.time - lastPacketArrivalTime;
            lastPacketArrivalTime = Time.time;
            if (currentDelay > 1.0f) currentDelay = 0.05f;
            averagePacketDelay = Mathf.Lerp(averagePacketDelay, currentDelay, 0.05f);

            if (!hasNetworkTarget) InitializeNetworkInterpolation();
            networkTargetPosition = position;
            networkTargetRotation = rotation;
        }
    }
}