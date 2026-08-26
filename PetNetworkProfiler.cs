using System;
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
                Plugin.Log.LogInfo("[Ai-Chan Profiler] Global network tracker initialized.");
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
            float uptimeSeconds = Time.unscaledTime - startTime;
            TimeSpan t = TimeSpan.FromSeconds(uptimeSeconds);
            string uptimeString = string.Format("{0:D2}h:{1:D2}m:{2:D2}s", t.Hours, t.Minutes, t.Seconds);

            Plugin.Log.LogInfo($"=== [Ai-Chan Network : {trigger}] ===");
            Plugin.Log.LogInfo($"Total Uptime: {uptimeString}");
            Plugin.Log.LogInfo("-------------------------------------------------");
            Plugin.Log.LogInfo("[ STEAM NETWORKING (MOD DATA) ]");
            Plugin.Log.LogInfo($"Current Upload: {lastSentBps} B/s | Current Download: {lastReceivedBps} B/s");
            Plugin.Log.LogInfo($"Total Upload: {totalBytesSent / 1024f:F2} KB | Total Download: {totalBytesReceived / 1024f:F2} KB");
            Plugin.Log.LogInfo("=================================================");
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