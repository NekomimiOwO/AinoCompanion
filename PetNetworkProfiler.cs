using System;
using System.Text;
using UnityEngine;

namespace ElsaPetMod
{
    public class PetNetworkProfiler : MonoBehaviour
    {
        public static PetNetworkProfiler Instance;

        private static long totalBytesSent = 0;
        private static long totalBytesReceived = 0;
        private static float startTime = 0f;
        private static bool isInitialized = false;

        private int sentThisSecond = 0;
        private int receivedThisSecond = 0;

        private int lastSentBps = 0;
        private int lastReceivedBps = 0;
        private float timer = 0f;

        public const int EstimatedStatePacketSize = 60;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (!isInitialized)
            {
                startTime = Time.unscaledTime;
                isInitialized = true;
                Plugin.Log.LogInfo("[Ai-Chan Profiler] Network statistics profiler initialized.");
            }
        }

        // A CURA DO FANTASMA: Se o mapa recarregar, esvazia a instância!
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            timer += Time.unscaledDeltaTime;
            if (timer >= 1f)
            {
                lastSentBps = sentThisSecond;
                lastReceivedBps = receivedThisSecond;
                sentThisSecond = 0;
                receivedThisSecond = 0;
                timer -= 1f;
            }
        }

        public void PrintStats(string trigger)
        {
            try
            {
                float uptimeSeconds = Mathf.Max(0f, Time.unscaledTime - startTime);
                TimeSpan t = TimeSpan.FromSeconds((double)uptimeSeconds);

                // A CURA DO TRAVAMENTO (StringBuilder):
                // Constrói o texto gigante na memória e envia uma única vez ao console!
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(); // Pula uma linha no console para ficar organizado
                sb.AppendLine($"=== [Ai-Chan Network : {trigger}] ===");
                sb.AppendLine($"Total Uptime: {t.Hours:D2}h:{t.Minutes:D2}m:{t.Seconds:D2}s");
                sb.AppendLine("-------------------------------------------------");
                sb.AppendLine("[ STEAM NETWORKING (MOD DATA) ]");
                sb.AppendLine($"Current Upload: {lastSentBps} B/s | Current Download: {lastReceivedBps} B/s");
                sb.AppendLine($"Total Upload: {totalBytesSent / 1024f:F2} KB | Total Download: {totalBytesReceived / 1024f:F2} KB");
                sb.Append("=================================================");

                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[AiNet] Falha ao imprimir o Profiler: {ex.Message}");
            }
        }

        public static void RecordSend(int bytes)
        {
            totalBytesSent += bytes;
            if (Instance != null) Instance.sentThisSecond += bytes;
        }

        public static void RecordReceive(int bytes)
        {
            totalBytesReceived += bytes;
            if (Instance != null) Instance.receivedThisSecond += bytes;
        }
    }
}