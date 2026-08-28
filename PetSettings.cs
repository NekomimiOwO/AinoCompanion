using BepInEx.Configuration;
using UnityEngine;

namespace ElsaPetMod
{
    public static class PetSettings
    {
        public static ConfigEntry<bool> EnableDebugLogs;
        public static ConfigEntry<bool> EnableStateTransitionLogs;
        public static ConfigEntry<bool> EnableNavMeshLogs;
        public static ConfigEntry<bool> EnableCarryJitterLogs;
        public static ConfigEntry<bool> EnableDoorOpening;
        public static ConfigEntry<float> CartApproachDistance;
        public static ConfigEntry<float> CartDropDistance;
        public static ConfigEntry<float> ShopDropDistance;
        public static ConfigEntry<float> CarrySpeed;

        public static ConfigEntry<float> Speed;
        public static ConfigEntry<float> MaxMass;
        public static ConfigEntry<float> PetMass;
        public static ConfigEntry<float> StandUpDelay;
        public static ConfigEntry<float> AutoJumpDelay;
        public static ConfigEntry<float> AngularDrag;
        public static ConfigEntry<float> Volume;
        public static ConfigEntry<float> PlayerSwitchInterval;
        public static ConfigEntry<float> FollowDistance;

        public static ConfigEntry<int> ExplosionPlayerDamage;
        public static ConfigEntry<int> ExplosionEnemyDamage;
        public static ConfigEntry<float> ExplosionRadius;
        public static ConfigEntry<float> ExplosionForce;
        public static ConfigEntry<float> FollowStoppingDistance;
        public static ConfigEntry<string> InteractKey;
        public static ConfigEntry<string> PetKey;
        public static ConfigEntry<string> SwitchOwnerKey;
        public static ConfigEntry<float> GiveItemDistance;
        public static ConfigEntry<bool> InheritPetScaleOnCarry;

        // Novas configurações de Performance e Pathfinding
        public static ConfigEntry<bool> EnableGhostProbing;

        public static ConfigEntry<float> MinJumpObstacleHeight;

        public static ConfigEntry<bool> EnableExplodeCommand;
        public static ConfigEntry<int> GhostProbeRays;
        public static ConfigEntry<bool> EnableDebugRays; // <- NOVO: Opção para ver os fantasmas
        public static ConfigEntry<float> DebugRaysFadeTime; // <- NOVO AQUI

        public static ConfigEntry<float> KnockdownSpeedThreshold;
        public static ConfigEntry<float> MaxJumpObstacleHeight; // <-- ADICIONE ESTA LINHA

        public static ConfigEntry<float> DebugBreadcrumbsFadeTime;

        public static ConfigEntry<float> GhostProbeDistance;
        public static ConfigEntry<float> GhostProbeUpdateInterval;

        public static void Initialize(ConfigFile config)
        {

            CartApproachDistance = config.Bind(
                "Delivery",
                "CartApproachDistance",
                1.60f,
                new ConfigDescription(
                    "Distance for Ai-Chan to approach the cart before stopping.",
                    new AcceptableValueRange<float>(0.5f, 4.0f)));

            CartDropDistance = config.Bind(
                "Delivery",
                "CartDropDistance",
                1.80f,
                new ConfigDescription(
                    "Maximum distance (in meters) for Ai-Chan to throw/drop the item into the cart.",
                    new AcceptableValueRange<float>(0.5f, 5.0f)));

            ShopDropDistance = config.Bind(
                "Delivery",
                "ShopDropDistance",
                1.20f,
                new ConfigDescription(
                    "Maximum distance (in meters) for Ai-Chan to drop the item at the shop counter.",
                    new AcceptableValueRange<float>(0.5f, 4.0f)));

            GiveItemDistance = config.Bind(
                "Delivery",
                "GiveItemDistance",
                4.5f,
                 new ConfigDescription(
                    "Maximum distance (in meters) for Ai-Chan to be able to take an item from the player's hand.",
                    new AcceptableValueRange<float>(2f, 8f)));

            Speed = config.Bind(
                "Movement",
                "Speed",
                3.5f,
                new ConfigDescription(
                    "Movement speed of Ai-Chan.",
                    new AcceptableValueRange<float>(1f, 10f)));

            CarrySpeed = config.Bind(
                "Movement",
                "Carry Speed",
                3.5f,
                new ConfigDescription(
                    "Movement speed of Ai-Chan when carrying an item or player to the cart/extraction.",
                    new AcceptableValueRange<float>(1f, 10f)));

            AutoJumpDelay = config.Bind(
                "Movement",
                "Auto Jump Stuck Delay",
                1f,
                new ConfigDescription(
                    "Seconds spent stuck before Ai-Chan performs an automatic obstacle jump.",
                    new AcceptableValueRange<float>(0.5f, 5f)));

            FollowDistance = config.Bind(
                "Movement",
                "Follow Range (Start)",
                2.0f,
                new ConfigDescription(
                    "Distance at which Ai-Chan begins following the player.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            FollowStoppingDistance = config.Bind(
                "Movement",
                "Stopping Distance(Stop)",
                2f,
                new ConfigDescription(
                    "Distance at which Ai-Chan stops near the player.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            MinJumpObstacleHeight = config.Bind(
            "Movement",
            "Min Jump Obstacle Height",
            0.75f,
            new ConfigDescription(
                "Minimum height(in meters) of an obstacle for Ai - Chan to stop and jump.Values ​​between 0.75m and 0.85m allow her to walk up stairs smoothly without attempting to jump over the steps.",
        new AcceptableValueRange<float>(0.35f, 2.5f)));

            MaxJumpObstacleHeight = config.Bind(
                "Movement",
                "Max Jump Obstacle Height",
                3.5f,
                new ConfigDescription(
                    "Maximum height (in meters) of an obstacle Ai-Chan will attempt to jump onto.",
            new AcceptableValueRange<float>(1.5f, 8.0f)));


            MaxMass = config.Bind(
                "Interaction",
                "Max Carried Mass",
                3f,
                new ConfigDescription(
                    "Maximum item weight Ai-Chan can accept and carry.",
                    new AcceptableValueRange<float>(0.5f, 20f)));

            EnableDoorOpening = config.Bind(
                "Interaction",
                "Enable Door Opening",
                true,
                new ConfigDescription("Permite que a Ai-Chan destrave e empurre as portas do mapa fisicamente."));

            ExplosionPlayerDamage = config.Bind(
                "Explosion",
                "Player Damage",
                10,
                new ConfigDescription("Dano causado aos jogadores no raio da explosão.", new AcceptableValueRange<int>(0, 200)));

            ExplosionEnemyDamage = config.Bind(
                "Explosion",
                "Enemy Damage",
                50,
                new ConfigDescription("Dano causado aos monstros e inimigos.", new AcceptableValueRange<int>(0, 500)));

            ExplosionRadius = config.Bind(
                "Explosion",
                "Explosion Radius",
                1.2f,
                new ConfigDescription("Tamanho e raio da explosão (em metros).", new AcceptableValueRange<float>(0.5f, 10.0f)));

            ExplosionForce = config.Bind(
                "Explosion",
                "Explosion Force Multp. (if too high damage is not applied far away)",
                1.0f,
                new ConfigDescription("Multiplicador da força de impacto físico e arremesso de objetos.", new AcceptableValueRange<float>(0.5f, 10.0f)));

            InheritPetScaleOnCarry = config.Bind(
                "Interaction",
                "Inherit Pet Scale On Carry",
                false,
                new ConfigDescription("When enabled, carried items scale proportionally with the pet's size while held (minimum scale cap of 0.01 to prevent disappearing). When disabled, items preserve their original world scale."));

            EnableExplodeCommand = config.Bind(
                "Commands",
                "Enable Explode Command",
                false,
                new ConfigDescription("Permite que o dono use o comando de chat para explodir a Ai-Chan."));

            PetMass = config.Bind(
                "Physics",
                "Body Mass",
                1.5f,
                new ConfigDescription(
                    "Rigidbody and PhysGrab mass of Ai-Chan's body.",
                    new AcceptableValueRange<float>(0.2f, 10f)));

            // --- BIND DA NOVA CONFIGURAÇÃO AQUI ---
            KnockdownSpeedThreshold = config.Bind(
                "Physics",
                "Knockdown Impact Resistance",
                3.5f,
                new ConfigDescription("Minimum impact speed (m/s) required to knock Ai-Chan down.", 
                new AcceptableValueRange<float>(0.5f, 10.0f)));

            StandUpDelay = config.Bind(
                "Physics",
                "Stand Up Delay",
                2.0f,
                new ConfigDescription(
                    "Delay in seconds before Ai-Chan starts standing up after hitting the ground.",
                    new AcceptableValueRange<float>(0f, 10f)));

            AngularDrag = config.Bind(
                "Physics",
                "Angular Drag",
                0.5f,
                new ConfigDescription(
                    "Free-flight rotation resistance when thrown.",
                    new AcceptableValueRange<float>(0f, 10f)));

            Volume = config.Bind(
                "Audio",
                "Volume",
                50f,
                new ConfigDescription(
                    "Audio volume percentage (50% default = 25% actual output limit).",
                    new AcceptableValueRange<float>(0f, 100f)));

            PlayerSwitchInterval = config.Bind(
                "Multiplayer",
                "Owner Switch Interval (Minutes)",
                3f,
                new ConfigDescription(
                    "Interval in minutes to switch target player in multiplayer (0 disables).",
                    new AcceptableValueRange<float>(2f, 10f)));

            InteractKey = config.Bind(
                "Controls",
                "Give Item Key",
                "R",
                new ConfigDescription("Key code to give the held item to Ai-Chan."));

            PetKey = config.Bind(
                "Controls",
                "Pet Key",
                "E",
                new ConfigDescription("Key code to pet Ai-Chan."));

            SwitchOwnerKey = config.Bind(
                "Controls",
                "Switch Owner Key",
                "F5",
                new ConfigDescription("Key code to manually switch Ai-Chan's owner to another player."));

            EnableDebugLogs = config.Bind(
                "Logs",
                "Enable Debug Logs",
                true,
                new ConfigDescription("Enable or disable Ai-Chan network and debug logs in the console."));

            EnableStateTransitionLogs = config.Bind(
                "Logs",
                "Enable State Transition Logs",
                false,
                new ConfigDescription("Enable or disable verbose state transition logs (useful for deep debugging)."));

            EnableNavMeshLogs = config.Bind(
                "Logs",
                "Enable NavMesh Transition Logs",
                false,
                new ConfigDescription("Enable or disable logs when Ai-Chan enters or leaves the NavMesh."));

            EnableCarryJitterLogs = config.Bind(
            "Logs",
            "Enable Carry Logs(will spam console when carry)",
            false,
            new ConfigDescription("Enable or disable verbose delivery movement and jitter logs in the console."));

            // --- CONFIGURAÇÕES DE DESEMPENHO E PATHFINDING ---
            EnableGhostProbing = config.Bind(
                "Performance",
                "Enable Ghost Probing",
                true,
                new ConfigDescription("Enables multi-ray manual pathfinding to smoothly avoid tables/walls. Disabling reduces CPU usage but lowers obstacle intelligence."));

            // --- BIND DAS NOVAS AQUI ---
            GhostProbeDistance = config.Bind(
                "Performance", 
                "Ghost Probe Distance",
             2.5f, 
             new ConfigDescription("How far (in meters) the ghost probes look ahead to avoid walls. Higher = Detects earlier.", new AcceptableValueRange<float>(1.0f, 5.0f)));
            
            GhostProbeUpdateInterval = config.Bind(
                "Performance", 
                "Ghost Probe Update Interval", 
            0.1f, 
            new ConfigDescription("How often (in seconds) the pathfinding is calculated. 0.1s = 10 times per second. Higher = More performance, slower reaction.", 
            new AcceptableValueRange<float>(0.02f, 0.5f)));

            GhostProbeRays = config.Bind(
                "Performance",
                "Ghost Probe Rays",
                7,
                new ConfigDescription(
                    "Number of projection rays used when dodging obstacles.",
                    new AcceptableValueRange<int>(1, 12)));

            EnableDebugRays = config.Bind(
                "Performance",
                "Enable Debug Rays",
                false,
                new ConfigDescription("Visualizes the ghost simulation paths in-game. Cyan = Free future path, Red = Hit wall, Green = Best chosen path."));

            DebugRaysFadeTime = config.Bind(
                "Performance",
                "Debug Rays Fade Time",
                0.25f, // Valor padrão de 0.25 segundos
                new ConfigDescription(
                    "How long (in seconds) the debug rays remain visible on screen.",
                    new AcceptableValueRange<float>(0.05f, 5.0f)));

            DebugBreadcrumbsFadeTime = config.Bind(
                "Performance",
                "Debug Breadcrumbs Fade Time",
                0.15f, // Valor padrão um pouco menor, já que são muitas linhas
                new ConfigDescription(
                    "How long (in seconds) the breadcrumb trail rays remain visible on screen.",
                    new AcceptableValueRange<float>(0.05f, 5.0f)));
        }

        public static KeyCode GetInteractKeyCode()
        {
            if (System.Enum.TryParse(InteractKey != null ? InteractKey.Value : "R", true, out KeyCode key)) return key;
            return KeyCode.R;
        }

        public static KeyCode GetPetKeyCode()
        {
            if (System.Enum.TryParse(PetKey != null ? PetKey.Value : "E", true, out KeyCode key)) return key;
            return KeyCode.E;
        }

        public static KeyCode GetSwitchOwnerKeyCode()
        {
            if (System.Enum.TryParse(SwitchOwnerKey != null ? SwitchOwnerKey.Value : "F5", true, out KeyCode key)) return key;
            return KeyCode.F5;
        }
    }
}