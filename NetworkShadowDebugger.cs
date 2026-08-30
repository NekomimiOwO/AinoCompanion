using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ElsaPetMod
{
    public class NetworkShadowDebugger : MonoBehaviour
    {
        [Header("Configurações do Fantasma")]
        public Transform realAiChan;
        public Transform ghostAiChan;
        public Vector3 ghostOffset = new Vector3(1.5f, 0f, 0f);

        [Header("Simulação de Internet Ruim")]
        public float simulatedPingMs = 150f;
        public float simulatedJitterMs = 20f; // Nova variável
        [Range(0f, 100f)]
        public float packetLossPercent = 5f;
        public float networkTickRate = 20f;

        private struct NetworkPacket
        {
            public Vector3 position;
            public Quaternion rotation;
            public float timestamp;
        }

        private Queue<NetworkPacket> packetsInTransit = new Queue<NetworkPacket>();
        private float nextSendTime;

        private Vector3 networkTargetPosition;
        private Quaternion networkTargetRotation;

        // Memória da Interpolação Adaptativa do Fantasma
        private float lastPacketArrivalTime;
        private float averagePacketDelay = 0.05f;

        void Start()
        {
            if (ghostAiChan != null)
            {
                networkTargetPosition = ghostAiChan.position;
                networkTargetRotation = ghostAiChan.rotation;

                foreach (Collider col in ghostAiChan.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
                foreach (Rigidbody rb in ghostAiChan.GetComponentsInChildren<Rigidbody>(true))
                {
                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                }
            }
        }

        // Memória do Jitter Buffer no Fantasma
        private List<StateSnapshot> stateBuffer = new List<StateSnapshot>();
        private struct StateSnapshot { public Vector3 position; public Quaternion rotation; public float localTime; }

        void Update()
        {
            if (realAiChan == null || ghostAiChan == null) return;

            // LÊ EM TEMPO REAL AS CONFIGURAÇÕES (Resolve o bug do BepInEx não atualizar)
            if (PetSettings.ShadowPacketLoss != null) packetLossPercent = PetSettings.ShadowPacketLoss.Value;
            if (PetSettings.ShadowSimulatedPing != null) simulatedPingMs = PetSettings.ShadowSimulatedPing.Value;
            if (PetSettings.ShadowSimulatedJitter != null) simulatedJitterMs = PetSettings.ShadowSimulatedJitter.Value;

            // 1. O HOST ENVIANDO OS DADOS
            if (Time.time >= nextSendTime)
            {
                nextSendTime = Time.time + (1f / networkTickRate);

                if (Random.Range(0f, 100f) >= packetLossPercent)
                {
                    packetsInTransit.Enqueue(new NetworkPacket
                    {
                        position = realAiChan.position,
                        rotation = realAiChan.rotation,
                        timestamp = Time.time
                    });
                }
            }

            // 2. A INTERNET ENTREGANDO O PACOTE
            if (packetsInTransit.Count > 0)
            {
                float jitterDelay = Random.Range(0f, simulatedJitterMs / 1000f);
                float arrivalTime = packetsInTransit.Peek().timestamp + (simulatedPingMs / 1000f) + jitterDelay;

                if (Time.time >= arrivalTime)
                {
                    NetworkPacket arrivedPacket = packetsInTransit.Dequeue();

                    networkTargetPosition = arrivedPacket.position + ghostOffset;
                    networkTargetRotation = arrivedPacket.rotation;

                    // O Jitter Buffer do Fantasma
                    float packetTime = Time.time;
                    if (stateBuffer.Count > 0)
                        packetTime = Mathf.Max(Time.time, stateBuffer[stateBuffer.Count - 1].localTime + 0.05f);

                    stateBuffer.Add(new StateSnapshot { position = networkTargetPosition, rotation = networkTargetRotation, localTime = packetTime });
                    if (stateBuffer.Count > 20) stateBuffer.RemoveAt(0);

                    // Atraso Adaptativo
                    float currentDelay = Time.time - lastPacketArrivalTime;
                    lastPacketArrivalTime = Time.time;
                    if (currentDelay > 1.0f) currentDelay = 0.05f;
                    averagePacketDelay = Mathf.Lerp(averagePacketDelay, currentDelay, 0.05f);
                }
            }

            // 3. O CLIENT RENDERIZANDO
            if (Vector3.Distance(ghostAiChan.position, networkTargetPosition) > 12.0f)
            {
                ghostAiChan.position = networkTargetPosition;
                ghostAiChan.rotation = networkTargetRotation;
                stateBuffer.Clear();
                return;
            }

            bool useSnapshot = PetSettings.EnableSnapshotInterpolation != null && PetSettings.EnableSnapshotInterpolation.Value;

            if (useSnapshot && stateBuffer.Count >= 2)
            {
                float bufferDelay = (PetSettings.SnapshotBufferMs != null ? PetSettings.SnapshotBufferMs.Value : 100f) / 1000f;
                float renderTime = Time.time - bufferDelay;

                int indexA = -1;
                for (int i = stateBuffer.Count - 1; i >= 0; i--) { if (stateBuffer[i].localTime <= renderTime) { indexA = i; break; } }

                if (indexA >= 0 && indexA < stateBuffer.Count - 1)
                {
                    StateSnapshot pA = stateBuffer[indexA];
                    StateSnapshot pB = stateBuffer[indexA + 1];
                    float t = Mathf.InverseLerp(pA.localTime, pB.localTime, renderTime);
                    ghostAiChan.position = Vector3.Lerp(pA.position, pB.position, t);
                    ghostAiChan.rotation = Quaternion.Slerp(pA.rotation, pB.rotation, t);
                    if (indexA > 0) stateBuffer.RemoveRange(0, indexA);
                    return;
                }
                else if (indexA == stateBuffer.Count - 1)
                {
                    ghostAiChan.position = Vector3.Lerp(ghostAiChan.position, stateBuffer[indexA].position, 15f * Time.deltaTime);
                    ghostAiChan.rotation = Quaternion.Slerp(ghostAiChan.rotation, stateBuffer[indexA].rotation, 15f * Time.deltaTime);
                    return;
                }
            }

            // FALLBACK ADAPTATIVO
            float posSpeed = 12f;
            float rotSpeed = 15f;
            if (PetSettings.EnableAdaptiveInterpolation != null && PetSettings.EnableAdaptiveInterpolation.Value)
            {
                float degradation = Mathf.InverseLerp(0.08f, 0.25f, averagePacketDelay);
                posSpeed = Mathf.Lerp(12f, 6f, degradation);
                rotSpeed = Mathf.Lerp(15f, 8f, degradation);
            }

            ghostAiChan.position = Vector3.Lerp(ghostAiChan.position, networkTargetPosition, 1f - Mathf.Exp(-posSpeed * Time.deltaTime));

            bool useAntiFlick = PetSettings.EnableAntiFlickRotation != null && PetSettings.EnableAntiFlickRotation.Value;
            if (useAntiFlick && Quaternion.Angle(ghostAiChan.rotation, networkTargetRotation) > 100f)
                ghostAiChan.rotation = Quaternion.RotateTowards(ghostAiChan.rotation, networkTargetRotation, 800f * Time.deltaTime);
            else
                ghostAiChan.rotation = Quaternion.Slerp(ghostAiChan.rotation, networkTargetRotation, 1f - Mathf.Exp(-rotSpeed * Time.deltaTime));
        }
    }
}