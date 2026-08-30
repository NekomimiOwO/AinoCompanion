using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;
using Steamworks;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ElsaPetMod
{
    public class PetSpawner : MonoBehaviour
    {
        public static bool isSpawning;
        private static PetSpawner instance;
        private static bool eventSpawnInProgress;

        public static void Initialize()
        {
            isSpawning = false;
            eventSpawnInProgress = false;

            if (instance != null) return;

            GameObject spawnerObject = new GameObject("AiChan_PetSpawner_Net");
            Object.DontDestroyOnLoad(spawnerObject);
            instance = spawnerObject.AddComponent<PetSpawner>();

            Plugin.Log.LogInfo("[AiNet] PetSpawner initialized via Steamworks.");
        }

        private static bool IsOnlineMaster()
        {
            return PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && PhotonNetwork.IsMasterClient;
        }

        private void OnEnable()
        {
            RepoSteamNetwork.AddCallback<PetRequestSyncPacket>(OnRequestSync);
            RepoSteamNetwork.AddCallback<PetSpawnPacket>(OnSpawnPacket);
        }

        private void OnDisable()
        {
            RepoSteamNetwork.RemoveCallback<PetRequestSyncPacket>(OnRequestSync);
            RepoSteamNetwork.RemoveCallback<PetSpawnPacket>(OnSpawnPacket);
        }

        private void OnDestroy()
        {
            eventSpawnInProgress = false;
            if (instance == this) instance = null;
        }

        private void OnRequestSync(PetRequestSyncPacket packet)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            PetCompanionController pet = Object.FindObjectOfType<PetCompanionController>(true);
            if (pet != null)
            {
                PhotonView pv = pet.GetComponent<PhotonView>();
                if (pv != null)
                {
                    var spawnPacket = new PetSpawnPacket
                    {
                        Position = pet.transform.position,
                        ContextName = "Sync",
                        AllocatedViewID = pv.ViewID
                    };

                    spawnPacket.Header.Target = packet.Header.Sender;
                    RepoSteamNetwork.SendPacket(spawnPacket, NetworkDestination.PacketTarget);
                }
            }
        }

        private void OnSpawnPacket(PetSpawnPacket packet)
        {
            if (Object.FindObjectOfType<PetCompanionController>(true) != null) return;
            if (PhotonNetwork.GetPhotonView(packet.AllocatedViewID) != null) return;

            Plugin.Log.LogInfo($"[AiNet] Spawn event received from the Steam network. Context: {packet.ContextName} | ViewID: {packet.AllocatedViewID}");

            if (eventSpawnInProgress) return;
            eventSpawnInProgress = true;
            StartCoroutine(BuildPetWhenLevelIsReady(packet.Position, packet.ContextName, packet.AllocatedViewID));
        }

        private IEnumerator BuildPetWhenLevelIsReady(Vector3 position, string contextName, int allocatedViewID)
        {
            try
            {
                float timeout = Time.time + 15f;
                bool isShopSpawn = !string.IsNullOrEmpty(contextName) && contextName.IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0;

                while (Time.time < timeout)
                {
                    if (isShopSpawn)
                    {
                        if (IsShopContext() && SemiFunc.PlayerAvatarLocal() != null && NavMesh.SamplePosition(SemiFunc.PlayerAvatarLocal().transform.position, out _, 15f, NavMesh.AllAreas)) break;
                    }
                    else
                    {
                        if (LevelGenerator.Instance != null && LevelGenerator.Instance.Generated && SemiFunc.RunIsLevel()) break;
                    }
                    yield return new WaitForSeconds(0.25f);
                }

                if (Object.FindObjectOfType<PetCompanionController>(true) == null)
                {
                    try
                    {
                        BuildPetGameObject(position, contextName, allocatedViewID);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError("[AiNet] Late spawn error: " + ex);
                    }
                }
            }
            finally
            {
                eventSpawnInProgress = false;
            }
        }

        public static bool IsShopContext()
        {
            string activeScene = SceneManager.GetActiveScene().name.ToLowerInvariant();
            if (activeScene.Contains("shop")) return true;

            ExtractionPoint[] points = Object.FindObjectsOfType<ExtractionPoint>(true);
            FieldInfo field = AccessTools.Field(typeof(ExtractionPoint), "isShop");
            if (field == null) return false;

            foreach (ExtractionPoint point in points)
            {
                if (point != null && point.gameObject.activeInHierarchy && field.GetValue(point) is bool isShop && isShop) return true;
            }
            return false;
        }

        public static IEnumerator SpawnWhenShopIsReady(ExtractionPoint shopPoint)
        {
            try
            {
                if (shopPoint == null) yield break;
                Initialize();

                // CORREÇÃO: Aumentado para 30s de paciência pra ela esperar carregar e confirmar
                float timeout = Time.time + 30f;
                PlayerAvatar player = null;
                Vector3 validSpawnPoint = Vector3.zero;
                bool canSpawn = false;

                while (Time.time < timeout)
                {
                    player = SemiFunc.PlayerAvatarLocal();
                    if (player != null && player.gameObject.activeInHierarchy)
                    {
                        // Exige rigidamente um NavMesh bem debaixo/atrás do jogador
                        if (NavMesh.SamplePosition(player.transform.position - player.transform.forward * 1.5f, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                        {
                            validSpawnPoint = hit.position;
                            canSpawn = true;
                            break;
                        }
                    }
                    yield return new WaitForSeconds(0.5f);
                }

                if (canSpawn && Object.FindObjectOfType<PetCompanionController>(true) == null)
                {
                    TryCreatePet(player, "Shop", validSpawnPoint);
                }
            }
            finally { isSpawning = false; }
        }

        public static bool TryCreatePet(PlayerAvatar player, string contextName, Vector3? forcedPosition = null)
        {
            if (player == null || !player.gameObject.activeInHierarchy || Object.FindObjectOfType<PetCompanionController>(true) != null) return false;

            Vector3 spawnPoint;

            // Usa a posição forçada rígida, senão pega um backup do pé do cara
            if (forcedPosition.HasValue && forcedPosition.Value != Vector3.zero)
            {
                spawnPoint = forcedPosition.Value;
            }
            else
            {
                Vector3 candidate = player.transform.position - player.transform.forward * 1.5f;
                spawnPoint = NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.0f, NavMesh.AllAreas)
                    ? hit.position
                    : player.transform.position;
            }

            // Fixa a altura colada no chão antes de dar Instantiate para ela não spawnar voando
            if (Physics.Raycast(spawnPoint + Vector3.up * 1.0f, Vector3.down, out RaycastHit groundHit, 3.0f, ~LayerMask.GetMask("Player", "PlayerOnlyCollision", "Ignore Raycast")))
            {
                spawnPoint.y = groundHit.point.y;
            }

            bool isOnlineRoom = PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;

            if (!isOnlineRoom)
            {
                return BuildPetGameObject(spawnPoint, contextName, 0) != null;
            }

            if (!IsOnlineMaster() || eventSpawnInProgress) return false;

            GameObject pet = BuildPetGameObject(spawnPoint, contextName, 0);
            if (pet == null) return false;

            PhotonView view = pet.GetComponent<PhotonView>();
            if (view == null || view.ViewID <= 0)
            {
                Object.Destroy(pet);
                return false;
            }

            var packet = new PetSpawnPacket
            {
                Position = spawnPoint,
                ContextName = contextName ?? "Network",
                AllocatedViewID = view.ViewID
            };
            RepoSteamNetwork.SendPacket(packet, NetworkDestination.EveryoneExcludingSender);

            return true;
        }

        private static GameObject BuildPetGameObject(Vector3 position, string contextName, int allocatedViewID = 0)
        {
            if (Object.FindObjectOfType<PetCompanionController>(true) != null) return null;

            int physGrabLayer = LayerMask.NameToLayer("PhysGrabObject");
            if (physGrabLayer == -1) return null;

            GameObject petRoot = new GameObject("Ai-Chan Companion");
            petRoot.SetActive(false);
            petRoot.tag = "Phys Grab Object";
            petRoot.layer = physGrabLayer;
            petRoot.transform.position = position;

            PhotonView view = petRoot.AddComponent<PhotonView>();
            PhotonTransformView transformView = petRoot.AddComponent<PhotonTransformView>();
            view.ObservedComponents = new List<Component> { transformView };

            // A CURA DO HOST CEGO: Permite que o Client assuma a física nativa ao pegar
            view.OwnershipTransfer = OwnershipOption.Takeover;

            bool isMaster = !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;

            if (allocatedViewID > 0)
            {
                PhotonView existing = PhotonNetwork.GetPhotonView(allocatedViewID);
                if (existing != null && existing != view) { Object.Destroy(petRoot); return null; }

                view.ViewID = allocatedViewID;
                if (existing == null) PhotonNetwork.RegisterPhotonView(view);
                Plugin.Log.LogInfo($"[AiNet] Ai-Chan spawned in the Client! Context: {contextName} | ViewID: {view.ViewID}");
            }
            else if (PhotonNetwork.InRoom && isMaster)
            {
                if (!PhotonNetwork.AllocateViewID(view) || view.ViewID <= 0) { Object.Destroy(petRoot); return null; }
                Plugin.Log.LogInfo($"[AiNet] Ai-Chan spawned on Master! Context: {contextName} | ViewID: {view.ViewID}");
            }
            else
            {
                Plugin.Log.LogInfo($"[AiNet] Ai-Chan spawned in Singleplayer! Context: {contextName}");
            }

            float bodyMass = PetSettings.PetMass != null ? PetSettings.PetMass.Value : 1.5f;
            float angularDragValue = PetSettings.AngularDrag != null ? PetSettings.AngularDrag.Value : 0.5f;

            Rigidbody body = petRoot.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.mass = bodyMass;
            body.drag = 1f;
            body.angularDrag = angularDragValue;
            body.constraints = RigidbodyConstraints.None;
            body.interpolation = RigidbodyInterpolation.None;

            body.excludeLayers = LayerMask.GetMask("Player", "PlayerOnlyCollision");

            CapsuleCollider capsule = petRoot.AddComponent<CapsuleCollider>();
            capsule.radius = 0.30f;
            capsule.height = 1.35f;
            capsule.center = new Vector3(0f, 0.675f, 0f);

            PhysGrabObject grabObject = petRoot.AddComponent<PhysGrabObject>();
            grabObject.ignoreGrabPointCentering = true;
            grabObject.massOriginal = bodyMass;
            AccessTools.Field(typeof(PhysGrabObject), "physRidingDisabled")?.SetValue(grabObject, true);

            petRoot.AddComponent<PhysGrabObjectCollider>();

            PhysGrabObjectImpactDetector impactDetector = petRoot.AddComponent<PhysGrabObjectImpactDetector>();
            impactDetector.enabled = false;
            impactDetector.destroyDisable = true;
            impactDetector.playerHurtDisable = true;
            AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "isIndestructible")?.SetValue(impactDetector, true);

            impactDetector.onAllImpacts = new UnityEvent();
            impactDetector.onImpactLight = new UnityEvent();
            impactDetector.onImpactMedium = new UnityEvent();
            impactDetector.onImpactHeavy = new UnityEvent();
            impactDetector.onAllBreaks = new UnityEvent();
            impactDetector.onBreakLight = new UnityEvent();
            impactDetector.onBreakMedium = new UnityEvent();
            impactDetector.onBreakHeavy = new UnityEvent();
            impactDetector.onDestroy = new UnityEvent();
            impactDetector.onHurtColliderHit = new UnityEvent();

            PetPlayerCollisionController collision = petRoot.AddComponent<PetPlayerCollisionController>();
            collision.Initialize(capsule);

            NavMeshAgent agent = petRoot.AddComponent<NavMeshAgent>();
            agent.radius = 0.15f; // Estreitamento da caixa algorítmica previne boundary hugging severo.
            agent.height = 1.35f;
            agent.speed = PetSettings.Speed != null ? PetSettings.Speed.Value : 3.5f;
            agent.acceleration = 28f;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = 0.15f;
            agent.autoBraking = true;
            agent.autoRepath = true;
            agent.updateRotation = true;

            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.enabled = isMaster;

            if (isMaster)
            {
                if (NavMesh.SamplePosition(position, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                {
                    agent.Warp(navHit.position);
                    petRoot.transform.position = navHit.position;
                }
            }

            // Marcador no minimapa
            petRoot.AddComponent<PetMapTracker>();

            PetCompanionController controller = petRoot.AddComponent<PetCompanionController>();
            petRoot.AddComponent<PetInteraction>();
            petRoot.AddComponent<PetNetworkBridge>();

            GameObject visual = CreateAinoVisual(petRoot.transform);
            if (visual == null) { Object.Destroy(petRoot); return null; }

            controller.visualRoot = visual.transform;
            controller.animator = visual.GetComponentInChildren<Animator>(true);

            CloneNativeHeartParticles(visual.transform);
            CreateNameTag(petRoot.transform, "Ai-Chan");

            petRoot.SetActive(true);

            if (isMaster && agent.enabled)
            {
                if (NavMesh.SamplePosition(position, out NavMeshHit navHit, 2f, NavMesh.AllAreas)) agent.Warp(navHit.position);
                else agent.Warp(position);
            }
            // --- INJEÇÃO DO NETWORK SHADOW DEBUGGER ---
            // --- INJEÇÃO DO NETWORK SHADOW DEBUGGER ---
            if (PetSettings.EnableNetworkShadow != null && PetSettings.EnableNetworkShadow.Value)
            {
                if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)
                {
                    GameObject ghostVisual = CreateAinoVisual(null);
                    if (ghostVisual != null)
                    {
                        ghostVisual.name = "Ai-Chan Ghost (Network Shadow)";

                        NetworkShadowDebugger shadow = petRoot.AddComponent<NetworkShadowDebugger>();
                        shadow.realAiChan = petRoot.transform;
                        shadow.ghostAiChan = ghostVisual.transform;

                        if (PetSettings.ShadowSimulatedPing != null)
                            shadow.simulatedPingMs = PetSettings.ShadowSimulatedPing.Value;
                        if (PetSettings.ShadowPacketLoss != null)
                            shadow.packetLossPercent = PetSettings.ShadowPacketLoss.Value;
                        if (PetSettings.ShadowSimulatedJitter != null)
                            shadow.simulatedJitterMs = PetSettings.ShadowSimulatedJitter.Value; // Mapeamento novo

                        Plugin.Log.LogInfo("[AiNet] Network Shadow Debugger Injetado! O Fantasma vai te seguir.");
                    }
                }
            }

            return petRoot;
        }


        public class PetMapTracker : MonoBehaviour
        {
            private GameObject mapIconRoot;
            private GameObject spriteChild;
            private SpriteRenderer spriteRenderer;
            private static Sprite circleSprite;
            private int dirtFinderMapLayer = -1;

            private void Start()
            {
                dirtFinderMapLayer = LayerMask.NameToLayer("DirtFinderMap");
                if (dirtFinderMapLayer == -1) dirtFinderMapLayer = gameObject.layer;

                CreateCircleSprite();
            }

            private void CreateCircleSprite()
            {
                if (circleSprite != null) return;

                int size = 64;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Vector2 center = new Vector2(size / 2f, size / 2f);
                float radius = size / 2f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                        if (dist <= radius - 2f)
                        {
                            texture.SetPixel(x, y, Color.white);
                        }
                        else if (dist <= radius)
                        {
                            float alpha = Mathf.Clamp01(radius - dist);
                            texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                        }
                        else
                        {
                            texture.SetPixel(x, y, Color.clear);
                        }
                    }
                }
                texture.Apply();
                circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }

            private void LateUpdate()
            {
                // Só funciona estritamente em fases jogáveis (ignora em lojas, lobby ou se o minimapa não existir)
                if (!SemiFunc.RunIsLevel() || PetSpawner.IsShopContext() || Map.Instance == null || Map.Instance.OverLayerParent == null)
                {
                    if (mapIconRoot != null && mapIconRoot.activeSelf)
                    {
                        mapIconRoot.SetActive(false);
                    }
                    return;
                }

                // Cria a estrutura idêntica à do minimapa
                if (mapIconRoot == null)
                {
                    mapIconRoot = new GameObject("AiChan_MinimapEntity");
                    mapIconRoot.layer = dirtFinderMapLayer;
                    mapIconRoot.transform.SetParent(Map.Instance.OverLayerParent, false);

                    spriteChild = new GameObject("Sprite");
                    spriteChild.layer = dirtFinderMapLayer;
                    spriteChild.transform.SetParent(mapIconRoot.transform, false);
                    spriteChild.transform.localPosition = Vector3.zero;
                    spriteChild.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                    spriteChild.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);

                    spriteRenderer = spriteChild.AddComponent<SpriteRenderer>();
                    spriteRenderer.sprite = circleSprite;
                    spriteRenderer.color = new Color(1f, 0.2f, 0.8f, 1f); // Rosa / Magenta
                    spriteRenderer.sortingOrder = 100;
                }

                if (!mapIconRoot.activeSelf) mapIconRoot.SetActive(true);
                if (!spriteChild.activeSelf) spriteChild.SetActive(true);

                Vector3 worldPos = transform.position;
                Vector3 flatPos = new Vector3(worldPos.x, 0f, worldPos.z);
                mapIconRoot.transform.position = flatPos * Map.Instance.Scale + Map.Instance.OverLayerParent.position;
            }

            private void OnDestroy()
            {
                if (mapIconRoot != null)
                {
                    Destroy(mapIconRoot);
                }
            }
        }

        private static GameObject CreateAinoVisual(Transform parent)
        {
            GameObject prefab = GenshinImpactOverhaulRepo.GenshinImpactOverhaul.AinoIneffaPrefab;
            if (prefab == null) return null;

            GameObject visual = Object.Instantiate(prefab, parent, false);
            visual.name = "Ai-Chan Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            Transform aino = visual.transform.Find("Aino");
            Animator animator = visual.GetComponent<Animator>();

            if (aino != null) aino.gameObject.SetActive(true);
            if (visual.transform.Find("Ineffa") != null) visual.transform.Find("Ineffa").gameObject.SetActive(false);

            if (animator != null)
            {
                Animator ainoAnimator = aino == null ? null : aino.GetComponent<Animator>();
                if (ainoAnimator != null && ainoAnimator.avatar != null) animator.avatar = ainoAnimator.avatar;
                animator.enabled = true;
                animator.speed = 1f;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false; // Desativa a briga entre Animação e NavMesh
            }

            foreach (MonoBehaviour component in visual.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;
                string name = component.GetType().Name;
                if (name == "EnemyElsaAnim") { component.enabled = false; continue; }
                if (name.Contains("Enemy") || name.Contains("Elsa") || name.Contains("Aggro") || name.Contains("EventHandler")) Object.Destroy(component);
            }

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.isTrigger = true;
            foreach (Rigidbody rigidbody in visual.GetComponentsInChildren<Rigidbody>(true)) { rigidbody.isKinematic = true; rigidbody.useGravity = false; }

            return visual;
        }

        private static ParticleSystem CloneNativeHeartParticles(Transform parent)
        {
            foreach (EnemyElsaAnim elsa in Object.FindObjectsOfType<EnemyElsaAnim>(true))
            {
                if (elsa == null) continue;
                foreach (ParticleSystem source in elsa.transform.root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (source == null || source.name != "Particle Hearts") continue;
                    GameObject clone = Object.Instantiate(source.gameObject, parent, false);
                    clone.name = "AiChanNativeHearts";
                    clone.transform.localPosition = new Vector3(0f, 1.45f, 0f);
                    clone.transform.localRotation = Quaternion.identity;
                    clone.transform.localScale = Vector3.one;

                    foreach (Component component in clone.GetComponentsInChildren<Component>(true))
                    {
                        if (component != null && component.GetType().Name == "ParentConstraint") Object.Destroy(component);
                    }

                    foreach (ParticleSystem particle in clone.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        particle.gameObject.SetActive(true);
                        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    return clone.GetComponentInChildren<ParticleSystem>(true);
                }
            }
            return null;
        }

        private static void CreateNameTag(Transform parent, string text)
        {
            GameObject tag = new GameObject("AiChanNameTag");
            tag.transform.SetParent(parent, false);
            tag.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            TextMesh mesh = tag.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 36;
            mesh.characterSize = 0.04f;
            mesh.alignment = TextAlignment.Center;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.color = Color.cyan;
            tag.AddComponent<PetNameBillboard>();
        }
    }

    [HarmonyPatch(typeof(LevelGenerator), "Start")]
    internal static class PatchLevelGeneratorSpawnAiChan
    {
        private static void Postfix(LevelGenerator __instance)
        {
            if (__instance == null || !Plugin.IsPetAlive) return;

            string sceneName = SceneManager.GetActiveScene().name.ToLowerInvariant();
            if (sceneName.Contains("menu") || sceneName.Contains("title") || sceneName.Contains("start") || sceneName.Contains("lobby")) return;

            bool onlineRoom = PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;

            if (onlineRoom && !PhotonNetwork.IsMasterClient)
            {
                PetSpawner.Initialize();
                RepoSteamNetwork.SendPacket(new PetRequestSyncPacket(), NetworkDestination.HostOnly);
                return;
            }

            PetSpawner.Initialize();
            if (PetSpawner.isSpawning) return;

            PetSpawner.isSpawning = true;
            __instance.StartCoroutine(SpawnWhenLevelIsReady());
        }

        private static IEnumerator SpawnWhenLevelIsReady()
        {
            try
            {
                float timeout = Time.time + 30f;
                while (Time.time < timeout)
                {
                    if (!SemiFunc.RunIsLevel() || LevelGenerator.Instance == null || !LevelGenerator.Instance.Generated)
                    {
                        yield return new WaitForSeconds(0.25f);
                        continue;
                    }

                    PlayerAvatar player = SemiFunc.PlayerAvatarLocal();
                    if (player != null && player.gameObject.activeInHierarchy)
                    {
                        // Exige rigidamente um NavMesh do lado do jogador nas fases também
                        if (NavMesh.SamplePosition(player.transform.position - player.transform.forward * 1.0f, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                        {
                            yield return new WaitForSeconds(0.5f);
                            PetSpawner.TryCreatePet(player, "Level", hit.position);
                            yield break;
                        }
                    }
                    yield return new WaitForSeconds(0.5f);
                }
            }
            finally { PetSpawner.isSpawning = false; }
        }
    }

    [HarmonyPatch(typeof(ExtractionPoint), "Start")]
    internal static class PatchShopSpawnAiChan
    {
        private static void Postfix(ExtractionPoint __instance)
        {
            if (__instance == null || !Plugin.IsPetAlive) return;

            string sceneName = SceneManager.GetActiveScene().name.ToLowerInvariant();
            if (sceneName.Contains("menu") || sceneName.Contains("title") || sceneName.Contains("start") || sceneName.Contains("lobby")) return;

            FieldInfo field = AccessTools.Field(typeof(ExtractionPoint), "isShop");
            bool isShop = sceneName.Contains("shop") || (field != null && field.GetValue(__instance) is bool value && value);
            if (!isShop) return;

            bool onlineRoom = PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;

            if (onlineRoom && !PhotonNetwork.IsMasterClient)
            {
                PetSpawner.Initialize();
                RepoSteamNetwork.SendPacket(new PetRequestSyncPacket(), NetworkDestination.HostOnly);
                return;
            }

            PetSpawner.Initialize();
            if (PetSpawner.isSpawning) return;

            PetSpawner.isSpawning = true;
            __instance.StartCoroutine(PetSpawner.SpawnWhenShopIsReady(__instance));
        }
    }

    internal sealed class PetPlayerCollisionController : MonoBehaviour
    {
        private Collider[] petColliders;

        public void Initialize(CapsuleCollider collider)
        {
            petColliders = GetComponentsInChildren<Collider>(true);
        }

        private void FixedUpdate()
        {
            if (petColliders == null || petColliders.Length == 0) return;

            List<PlayerAvatar> players = SemiFunc.PlayerGetAll();
            if (players == null) return;

            foreach (PlayerAvatar avatar in players)
            {
                if (avatar == null || avatar.gameObject == null) continue;

                foreach (Collider playerCol in avatar.transform.root.GetComponentsInChildren<Collider>(true))
                {
                    if (playerCol == null || playerCol.isTrigger) continue;

                    for (int i = 0; i < petColliders.Length; i++)
                    {
                        if (petColliders[i] != null && petColliders[i] != playerCol)
                        {
                            Physics.IgnoreCollision(petColliders[i], playerCol, true);
                        }
                    }
                }
            }
        }
    }

    // SUBSTITUIR POR:
    // SUBSTITUIR POR:
    internal sealed class PetNameBillboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            Vector3 lookDir = transform.position - camera.transform.position;
            Vector3 flatDir = new Vector3(lookDir.x, 0f, lookDir.z);
            if (flatDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
            }
        }
    }
}