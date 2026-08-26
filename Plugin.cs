using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RepoSteamNetworking.API;
using RepoSteamNetworking.API.VersionCompat;

namespace ElsaPetMod
{
    [RSNVersionCompatibility(VersionCompatibility.Strict, optional: false)]
    [BepInPlugin(MODGUID, MODNAME, MODVERSION)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string MODGUID = "com.neko3004.aichancompanion";
        public const string MODNAME = "Ai-Chan Companion";
        public const string MODVERSION = "1.0.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static bool IsPetAlive = true;

        private Harmony harmony;

        public enum LogCategory
        {
            SteamNet,
            AiNet,
            AiInteract,
            AiSystem
        }

        public static void LogDebug(LogCategory category, string message)
        {
            if (PetSettings.EnableDebugLogs != null && PetSettings.EnableDebugLogs.Value)
            {
                Log.LogInfo($"[{category}] {message}");
            }
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // CORREÇÃO: Anexando o Profiler diretamente ao Plugin. Assim ele nunca será destruído!
            gameObject.AddComponent<PetNetworkProfiler>();

            PetSettings.Initialize(Config);

            RepoSteamNetwork.RegisterPacket<PetStatePacket>();
            RepoSteamNetwork.RegisterPacket<PetGiveItemPacket>();
            RepoSteamNetwork.RegisterPacket<PetSyncPettingPacket>();
            RepoSteamNetwork.RegisterPacket<PetCarryPlayerPacket>();
            RepoSteamNetwork.RegisterPacket<PetSyncCarryPacket>();
            RepoSteamNetwork.RegisterPacket<PetSwitchOwnerPacket>();
            RepoSteamNetwork.RegisterPacket<PetSpawnPacket>();
            RepoSteamNetwork.RegisterPacket<PetRequestSyncPacket>();
            RepoSteamNetwork.RegisterPacket<PetExplodePacket>();

            harmony = new Harmony(MODGUID);

            try
            {
                harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo($"{MODNAME} v{MODVERSION} loaded successfully (Steam Network enabled).");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"[AiSystem] Failed to apply patches: {ex}");
            }
        }
    }
}