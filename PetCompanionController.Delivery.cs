using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;
using UnityEngine;
using UnityEngine.AI;

namespace ElsaPetMod
{
    public partial class PetCompanionController
    {
        // O C# agora vai ler essas variáveis direto do painel de Config em tempo real!
        public float cartApproachDistance => PetSettings.CartApproachDistance != null ? PetSettings.CartApproachDistance.Value : 1.60f;
        public float cartDropDistance => PetSettings.CartDropDistance != null ? PetSettings.CartDropDistance.Value : 1.80f;
        public float shopDropDistance => PetSettings.ShopDropDistance != null ? PetSettings.ShopDropDistance.Value : 1.20f;
        public Transform holdPoint;
            
        private Vector3? lastShopDeliveryPosition = null;
        private const float ShopDeliverySpreadRadius = 0.35f;
        private const float ShopDeliveryMinDistance = 0.25f;
        private const float MinItemScaleCap = 0.01f;
        private Vector3? lockedApproachPoint = null;
        private Vector3 lockedApproachCartCenter = Vector3.zero;



        private PhysGrabObject carriedItem;
        private Vector3 carriedOriginalScale = Vector3.one;
        private bool carriedInheritScale = false; // Controle de escala salvo para o drop

        private PhysGrabCart targetCart;
        private ExtractionPoint targetExtractionPoint;
        private Rigidbody carriedRigidbody;
        private Collider[] carriedColliders;
        private bool[] carriedOriginalTriggers;
        private int[] carriedOriginalLayers;
        private bool carriedWasKinematic;

        private PlayerAvatar carriedPlayerAvatar;
        private PlayerTumble carriedTumble;
        private bool isDeliveryLocked;

        private Vector3 lastDeliveryDestination = Vector3.zero;
        private bool hasLastDeliveryDestination = false;

        private const float DeliveryDestinationHysteresis = 1.0f;

        private PlayerTumble GetPlayerTumble(PlayerAvatar player)
        {
            if (player == null) return null;
            return AccessTools.Field(typeof(PlayerAvatar), "tumble")?.GetValue(player) as PlayerTumble
                ?? player.GetComponentInChildren<PlayerTumble>();
        }

        private Rigidbody GetTumbleRigidbody(PlayerTumble tumble)
        {
            if (tumble == null) return null;
            return AccessTools.Field(typeof(PlayerTumble), "rb")?.GetValue(tumble) as Rigidbody
                ?? tumble.GetComponent<Rigidbody>();
        }

        private bool IsTumbling(PlayerTumble tumble)
        {
            if (tumble == null) return false;
            return AccessTools.Field(typeof(PlayerTumble), "isTumbling")?.GetValue(tumble) is bool b && b;
        }

        private bool IsPlayerDisabled(PlayerAvatar player)
        {
            if (player == null) return true;
            return AccessTools.Field(typeof(PlayerAvatar), "isDisabled")?.GetValue(player) is bool b && b;
        }

        private void DisablePetImpactProcessing()
        {
            PhysGrabObjectImpactDetector detector = GetComponent<PhysGrabObjectImpactDetector>();
            if (detector != null)
            {
                detector.enabled = false;
                detector.destroyDisable = true;
                detector.playerHurtDisable = true;
            }
        }

        private void SnapToFloorSafely()
        {
            if (agent != null && (!agent.enabled || !agent.isOnNavMesh))
            {
                if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit hitFloor, 1.5f, SemiFunc.LayerMaskGetVisionObstruct()))
                {
                    Vector3 newPos = transform.position;
                    newPos.y = hitFloor.point.y;
                    transform.position = newPos;
                }
                else if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 1.5f, NavMesh.AllAreas))
                {
                    if (!IsNetworkClientOnly) agent.enabled = true;
                    agent.Warp(navHit.position);
                }
            }
        }

        private Vector3 GetStableDeliveryDestination(Vector3 destination)
        {
            if (!hasLastDeliveryDestination ||
                Vector3.SqrMagnitude(destination - lastDeliveryDestination) >
                DeliveryDestinationHysteresis * DeliveryDestinationHysteresis)
            {
                lastDeliveryDestination = destination;
                hasLastDeliveryDestination = true;
            }

            return lastDeliveryDestination;
        }

        private void ResetDeliveryDestination()
        {
            lastDeliveryDestination = Vector3.zero;
            hasLastDeliveryDestination = false;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        public bool TryCarryPlayer(PlayerAvatar targetPlayer)
        {
            if (state == PetState.Dead || state == PetState.Grabbed || state == PetState.Stunned) return false;

            if (isDeliveryLocked || state == PetState.CarryItemToCart || carriedItem != null || carriedPlayerAvatar != null) return false;
            lockedApproachPoint = null;
            lockedApproachCartCenter = Vector3.zero;

            PlayerTumble tumble = GetPlayerTumble(targetPlayer);
            if (targetPlayer == null || tumble == null || !IsTumbling(tumble)) return false;

            if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
            {
                PhotonView targetPV = targetPlayer.photonView;
                if (targetPV != null)
                {
                    isDeliveryLocked = true;
                    var packet = new PetCarryPlayerPacket
                    {
                        PetViewID = GetComponent<PhotonView>().ViewID,
                        PlayerViewID = targetPV.ViewID
                    };
                    RepoSteamNetwork.SendPacket(packet, NetworkDestination.HostOnly);
                    Invoke(nameof(UnlockDelivery), 1.0f);
                    return true;
                }
                return false;
            }

            CreateHoldPoint();

            carriedPlayerAvatar = targetPlayer;
            carriedTumble = tumble;
            carriedRigidbody = GetTumbleRigidbody(carriedTumble);

            SnapToFloorSafely();

            Collider myCollider = GetComponent<Collider>();
            carriedColliders = carriedTumble.GetComponentsInChildren<Collider>(true);
            carriedOriginalTriggers = new bool[carriedColliders.Length];
            carriedOriginalLayers = new int[carriedColliders.Length];

            for (int i = 0; i < carriedColliders.Length; i++)
            {
                Collider col = carriedColliders[i];
                if (col == null) continue;

                carriedOriginalTriggers[i] = col.isTrigger;
                carriedOriginalLayers[i] = col.gameObject.layer;
                col.gameObject.layer = 2; // Ignore Raycast
                if (!(col is MeshCollider mc && !mc.convex)) col.isTrigger = true;
                if (myCollider != null) Physics.IgnoreCollision(myCollider, col, true);
            }

            if (carriedRigidbody != null)
            {
                carriedRigidbody.velocity = Vector3.zero;
                carriedRigidbody.angularVelocity = Vector3.zero;
                carriedRigidbody.isKinematic = true;
                carriedRigidbody.useGravity = false;
            }

            bool isShop = PetSpawner.IsShopContext();
            if (isShop) targetExtractionPoint = FindUsableExtractionPoint(true);
            else
            {
                targetCart = FindNearestValidCart();
                if (targetCart == null) targetExtractionPoint = FindUsableExtractionPoint(false);
            }

            noGrabUntilTime = Time.time + 2.0f;
            state = PetState.CarryItemToCart;
            isDeliveryLocked = true;

            ResetDeliveryDestination();

            if (aiAudio != null) aiAudio.PlayBark();

            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                PhotonView targetPV = targetPlayer.photonView;
                if (targetPV != null)
                {
                    var syncPacket = new PetSyncCarryPacket
                    {
                        PetViewID = GetComponent<PhotonView>().ViewID,
                        TargetViewID = targetPV.ViewID,
                        IsPlayer = true,
                        IsPickingUp = true,
                        InheritScale = false
                    };
                    RepoSteamNetwork.SendPacket(syncPacket, NetworkDestination.EveryoneExcludingSender);
                }
            }

            return true;
        }

        private void ReleaseCarriedPlayer()
        {
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && carriedPlayerAvatar != null && carriedPlayerAvatar.photonView != null)
            {
                var syncPacket = new PetSyncCarryPacket
                {
                    PetViewID = GetComponent<PhotonView>().ViewID,
                    TargetViewID = carriedPlayerAvatar.photonView.ViewID,
                    IsPlayer = true,
                    IsPickingUp = false,
                    InheritScale = false
                };
                RepoSteamNetwork.SendPacket(syncPacket, NetworkDestination.EveryoneExcludingSender);
            }

            if (carriedTumble != null)
            {
                Collider myCollider = GetComponent<Collider>();
                if (carriedColliders != null)
                {
                    for (int i = 0; i < carriedColliders.Length; i++)
                    {
                        if (carriedColliders[i] == null) continue;
                        carriedColliders[i].isTrigger = (carriedOriginalTriggers != null && i < carriedOriginalTriggers.Length) ? carriedOriginalTriggers[i] : false;
                        if (carriedOriginalLayers != null && i < carriedOriginalLayers.Length) carriedColliders[i].gameObject.layer = carriedOriginalLayers[i];
                        if (myCollider != null) Physics.IgnoreCollision(myCollider, carriedColliders[i], false);
                    }
                }

                Rigidbody tumbleRb = carriedRigidbody != null ? carriedRigidbody : GetTumbleRigidbody(carriedTumble);
                if (tumbleRb != null)
                {
                    tumbleRb.isKinematic = false;
                    tumbleRb.useGravity = true;
                }

                AccessTools.Method(typeof(PlayerTumble), "OverrideEnemyHurt")?.Invoke(carriedTumble, new object[] { 1.0f });
            }

            ClearCarryState();
        }

        private Vector3 GetShopDeliveryPosition(Vector3 centerPoint, ExtractionPoint point)
        {
            if (point != null)
            {
                foreach (Transform child in point.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name != "In Cart") continue;
                    Collider collider = child.GetComponent<Collider>();
                    if (collider == null || !collider.enabled) continue;

                    Physics.SyncTransforms();
                    Bounds bounds = collider.bounds;

                    float randomX = UnityEngine.Random.Range(-ShopDeliverySpreadRadius, ShopDeliverySpreadRadius);
                    float randomZ = UnityEngine.Random.Range(-ShopDeliverySpreadRadius, ShopDeliverySpreadRadius);

                    Vector3 newPoint = new Vector3(
                        centerPoint.x + randomX,
                        bounds.min.y + 0.40f,
                        centerPoint.z + randomZ
                    );

                    if (bounds.Contains(newPoint))
                    {
                        if (lastShopDeliveryPosition.HasValue)
                        {
                            Vector3 flatNew = new Vector3(newPoint.x, 0, newPoint.z);
                            Vector3 flatLast = new Vector3(lastShopDeliveryPosition.Value.x, 0, lastShopDeliveryPosition.Value.z);

                            if (Vector3.Distance(flatNew, flatLast) < ShopDeliveryMinDistance)
                            {
                                Vector3 direction = (flatNew - flatLast).normalized;
                                newPoint = lastShopDeliveryPosition.Value + direction * ShopDeliveryMinDistance;
                                newPoint.y = bounds.min.y + 0.40f;
                            }
                        }

                        lastShopDeliveryPosition = newPoint;
                        return newPoint;
                    }
                }
            }

            Vector3 fallback = centerPoint;
            fallback.y = centerPoint.y + 0.40f;
            lastShopDeliveryPosition = fallback;
            return fallback;
        }

        private void SetCartObstacleEnabled(PhysGrabCart cart, bool state)
        {
            if (cart == null) return;
            NavMeshObstacle obstacle = cart.GetComponent<NavMeshObstacle>() ?? cart.GetComponentInChildren<NavMeshObstacle>(true);
            if (obstacle != null)
            {
                obstacle.enabled = state;
            }
        }

        private void SetCartCarving(PhysGrabCart cart, bool carvingState)
        {
            if (cart == null) return;
            NavMeshObstacle obstacle = cart.GetComponent<NavMeshObstacle>() ?? cart.GetComponentInChildren<NavMeshObstacle>(true);
            if (obstacle != null)
            {
                obstacle.carving = carvingState;
            }
        }

        private string DebugPrivateField(string fieldName)
        {
            System.Reflection.FieldInfo field = GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public
            );

            if (field == null)
                return fieldName + "=<missing>";

            object value = field.GetValue(this);

            if (value == null)
                return fieldName + "=null";

            return fieldName + "=" + value.ToString();
        }
        private void TickCarryItemToCart()
        {
            // Executa o rastreamento apenas se a opção estiver ativada na config
            if (PetSettings.EnableCarryJitterLogs != null && PetSettings.EnableCarryJitterLogs.Value)
            {
                debugJitterTimer += Time.deltaTime;

                if (!debugJitterInitialized)
                {
                    debugLastLogicPos = transform.position;

                    Transform initialVisual =
                        animator != null ? animator.transform : transform;

                    debugLastVisualPos = initialVisual.position;
                    debugLastSampleTime = Time.time;
                    debugJitterInitialized = true;
                }

                if (debugJitterTimer >= 0.10f)
                {
                    Vector3 currentLogicPos = transform.position;

                    Transform visualTransform =
                        animator != null ? animator.transform : transform;

                    Vector3 currentVisualPos = visualTransform.position;

                    Vector3 logicDelta =
                        currentLogicPos - debugLastLogicPos;

                    Vector3 visualDelta =
                        currentVisualPos - debugLastVisualPos;

                    float sampleInterval =
                        Time.time - debugLastSampleTime;

                    float measuredSpeed = 0f;

                    if (sampleInterval > 0.001f)
                    {
                        measuredSpeed =
                            logicDelta.magnitude / sampleInterval;
                    }

                    float navVelocity = 0f;
                    float desiredSpeed = 0f;
                    Vector3 desiredVelocity = Vector3.zero;
                    Vector3 navDestination = Vector3.zero;
                    Vector3 navNextPosition = Vector3.zero;
                    Vector3 steeringTarget = Vector3.zero;

                    bool navEnabled = false;
                    bool navOnMesh = false;
                    bool navStopped = false;
                    bool navHasPath = false;

                    float agentTransformGap = 0f;
                    float remainingDistance = -1f;

                    if (agent != null)
                    {
                        navEnabled = agent.enabled;

                        if (agent.enabled)
                        {
                            navVelocity = agent.velocity.magnitude;
                            desiredVelocity = agent.desiredVelocity;
                            desiredSpeed = desiredVelocity.magnitude;
                            navDestination = agent.destination;
                            navNextPosition = agent.nextPosition;
                            steeringTarget = agent.steeringTarget;
                            navStopped = agent.isStopped;
                            navHasPath = agent.hasPath;
                            agentTransformGap =
                                Vector3.Distance(transform.position, agent.nextPosition);

                            navOnMesh = agent.isOnNavMesh;

                            if (agent.isOnNavMesh)
                            {
                                remainingDistance = agent.remainingDistance;
                            }
                        }
                    }

                    Vector3 measuredVelocity = Vector3.zero;

                    if (sampleInterval > 0.001f)
                    {
                        measuredVelocity =
                            logicDelta / sampleInterval;
                    }

                    float targetDistance = -1f;
                    float cartDistance = -1f;

                    if (agent != null && agent.enabled)
                    {
                        Vector3 flatPosition = transform.position;
                        Vector3 flatDestination = agent.destination;

                        flatPosition.y = 0f;
                        flatDestination.y = 0f;

                        targetDistance =
                            Vector3.Distance(flatPosition, flatDestination);
                    }

                    if (targetCart != null)
                    {
                        Vector3 flatPosition = transform.position;
                        Vector3 flatCartPosition = targetCart.transform.position;

                        flatPosition.y = 0f;
                        flatCartPosition.y = 0f;

                        cartDistance =
                            Vector3.Distance(flatPosition, flatCartPosition);
                    }

                    Rigidbody carriedRb = carriedRigidbody;

                    Collider petCollider = GetComponent<Collider>();

                    int carriedColliderCount = 0;
                    int carriedTriggerCount = 0;
                    int carriedIgnoredCount = 0;

                    if (carriedColliders != null)
                    {
                        carriedColliderCount = carriedColliders.Length;

                        for (int i = 0; i < carriedColliders.Length; i++)
                        {
                            Collider carriedCollider = carriedColliders[i];

                            if (carriedCollider == null)
                                continue;

                            if (carriedCollider.isTrigger)
                                carriedTriggerCount++;

                            if (petCollider != null &&
                                Physics.GetIgnoreCollision(petCollider, carriedCollider))
                            {
                                carriedIgnoredCount++;
                            }
                        }
                    }

                    string carriedPhysicsInfo =
                        " CarriedRB=null" +
                        " CarriedColliders=" + carriedColliderCount +
                        " CarriedTriggers=" + carriedTriggerCount +
                        " CarriedIgnored=" + carriedIgnoredCount;

                    if (carriedRb != null)
                    {
                        carriedPhysicsInfo =
                            " CarriedRBKinematic=" + carriedRb.isKinematic +
                            " CarriedRBVelocity=" + carriedRb.velocity +
                            " CarriedRBAngularVelocity=" + carriedRb.angularVelocity +
                            " CarriedColliders=" + carriedColliderCount +
                            " CarriedTriggers=" + carriedTriggerCount +
                            " CarriedIgnored=" + carriedIgnoredCount;
                    }

                    float forwardSpeed = 0f;
                    float desiredForwardSpeed = 0f;

                    if (agent != null && agent.enabled)
                    {
                        forwardSpeed = Vector3.Dot(measuredVelocity, agent.desiredVelocity.normalized);
                        desiredForwardSpeed = Vector3.Dot(agent.velocity, agent.desiredVelocity.normalized);
                    }

                    string logMessage =
                        "[JitterLog] " +
                        "Time=" + Time.time.ToString("F3") + " " +
                        "SampleDt=" + sampleInterval.ToString("F3") + " " +
                        "LogicPos=" + currentLogicPos + " " +
                        "LogicDelta=" + logicDelta.magnitude.ToString("F3") + " " +
                        "MeasuredSpeed=" + measuredSpeed.ToString("F3") + " " +
                        "VisualPos=" + currentVisualPos + " " +
                        "VisualDelta=" + visualDelta.magnitude.ToString("F3") + " " +
                        "NavVel=" + navVelocity.ToString("F3") + " " +
                        "DesiredVel=" + desiredVelocity + " " +
                        "DesiredSpeed=" + desiredSpeed.ToString("F3") + " " +
                        "NavDest=" + navDestination + " " +
                        "NavNext=" + navNextPosition + " " +
                        "SteeringTarget=" + steeringTarget + " " +
                        "Enabled=" + navEnabled + " " +
                        "OnMesh=" + navOnMesh + " " +
                        "Stopped=" + navStopped + " " +
                        "HasPath=" + navHasPath + " " +
                        "AgentGap=" + agentTransformGap.ToString("F3") + " " +
                        "Remaining=" + remainingDistance.ToString("F3") + " " +
                        "TargetDist=" + targetDistance.ToString("F3") + " " +
                        "CartDist=" + cartDistance.ToString("F3") +
                        " MeasuredVelocity=" + measuredVelocity +
                        " MeasuredSpeed=" + measuredVelocity.magnitude.ToString("F3") +
                        " ForwardSpeed=" + forwardSpeed.ToString("F3") +
                        " DesiredForwardSpeed=" + desiredForwardSpeed.ToString("F3");

                    if (agent != null)
                    {
                        logMessage +=
                            " StopDist=" + agent.stoppingDistance.ToString("F3") +
                            " Accel=" + agent.acceleration.ToString("F3") +
                            " AngularSpeed=" + agent.angularSpeed.ToString("F3");
                    }

                    logMessage +=
                        " ApproachDist=" + cartApproachDistance.ToString("F3") +
                        " DropDist=" + cartDropDistance.ToString("F3") +
                        " " + DebugPrivateField("hasManualMoveTarget") +
                        " " + DebugPrivateField("autoMovementSuspendedUntil") +
                        " " + DebugPrivateField("lockedApproachPoint") +
                        " " + DebugPrivateField("lockedApproachCartCenter") +
                        " " + DebugPrivateField("lastDeliveryDestination") +
                        " " + DebugPrivateField("hasLastDeliveryDestination") +
                        " " + DebugPrivateField("isDeliveryLocked") +
                        carriedPhysicsInfo;

                    Plugin.Log.LogInfo(logMessage);

                    debugLastLogicPos = currentLogicPos;
                    debugLastVisualPos = currentVisualPos;
                    debugLastSampleTime = Time.time;
                    debugJitterTimer = 0f;
                }
            }
            else
            {
                debugJitterInitialized = false;
            }

            if (hasManualMoveTarget || Time.time < autoMovementSuspendedUntil)
            {
                if (TickManualMove())
                    return;
            }

            // --- 1. SE CARREGANDO JOGADOR ---
            // --- 1. SE CARREGANDO JOGADOR ---
            if (carriedPlayerAvatar != null)
            {
                if (carriedTumble == null || !IsTumbling(carriedTumble) || IsPlayerDisabled(carriedPlayerAvatar))
                {
                    ReleaseCarriedPlayer();
                    state = PetState.FollowOwner;
                    return;
                }

                // Matemática prematura de posição foi movida totalmente para o LateUpdate!

                if (carriedRigidbody != null)
                {
                    if (!carriedRigidbody.isKinematic)
                    {
                        carriedRigidbody.velocity = Vector3.zero;
                        carriedRigidbody.angularVelocity = Vector3.zero;
                    }
                }

                Vector3 destination = transform.position;

                if (targetExtractionPoint != null && IsExtractionPointUsable(targetExtractionPoint, PetSpawner.IsShopContext()))
                {
                    destination = GetUnlockedExtractionCenter(targetExtractionPoint);
                }
                else if (targetCart != null && targetCart.gameObject.activeInHierarchy)
                {
                    // A CORREÇÃO DE OURO: Desliga o NavMeshObstacle do carrinho também ao carregar o jogador!
                    SetCartObstacleEnabled(targetCart, false);
                    SetCartCarving(targetCart, false);

                    BoxCollider area = GetCartDepositArea(targetCart);
                    if (area != null) destination = new Vector3(area.bounds.center.x, area.bounds.min.y + 0.15f, area.bounds.center.z);
                }
                else
                {
                    ReleaseCarriedPlayer();
                    state = PetState.FollowOwner;
                    return;
                }

                Vector3 stableDestination = GetStableDeliveryDestination(destination);
                Vector3 approach = GetCartApproachPoint(stableDestination);

                MoveTo(approach, 1.2f); // Aumentado para 1.2f

                float distToCart = HorizontalDistance(transform.position, destination);
                float distToApproach = HorizontalDistance(transform.position, approach);

                bool stuckOnForcefield = agent.enabled && agent.pathStatus == NavMeshPathStatus.PathPartial && distToCart <= cartDropDistance + 1.0f;

                // Margens de tolerância aumentadas (1.2f e + 1.0f)
                if (distToApproach <= 1.2f || distToCart <= cartDropDistance + 1.0f || stuckOnForcefield)
                {
                    StopMoving();
                    ReleaseCarriedPlayer();
                    state = PetState.FollowOwner;
                }
                return;
            }
            // ... O restante da função (SE CARREGANDO ITEM) continua normal a partir daqui ...

            // ... O restante da função (SE CARREGANDO ITEM) continua normal a partir daqui ...

            bool beingGrabbedByPlayer = (carriedItem != null && carriedItem.playerGrabbing != null && carriedItem.playerGrabbing.Count > 0);
            if (Time.time < noGrabUntilTime) beingGrabbedByPlayer = false;

            if (carriedItem == null || carriedItem.dead || beingGrabbedByPlayer)
            {
                DropItemAtFeetSafe();
                state = PetState.FollowOwner;
                return;
            }

            bool isShop = PetSpawner.IsShopContext();

            if (isShop)
            {
                if (targetExtractionPoint != null && IsExtractionPointUsable(targetExtractionPoint, true))
                {
                    Vector3 extractionCenter = GetUnlockedExtractionCenter(targetExtractionPoint);
                    Vector3 stableDestination = GetStableDeliveryDestination(extractionCenter);
                    Vector3 approach = GetCartApproachPoint(stableDestination);

                    MoveTo(approach, 0.6f);

                    if (HorizontalDistance(transform.position, approach) <= shopDropDistance + 0.3f)
                    {
                        Vector3 deliveryPos = GetShopDeliveryPosition(extractionCenter, targetExtractionPoint);
                        ReleaseCarriedItem(deliveryPos, false);
                        state = PetState.FollowOwner;
                    }
                    return;
                }
            }
            else
            {
                if (targetCart == null || !targetCart.gameObject.activeInHierarchy)
                    targetCart = FindNearestValidCart();

                if (targetCart != null && targetCart.gameObject.activeInHierarchy)
                {
                    SetCartObstacleEnabled(targetCart, false); // Desliga o obstáculo para rota limpa
                    BoxCollider area = GetCartDepositArea(targetCart);
                    if (area != null)
                    {
                        Vector3 cartFloorCenter = new Vector3(area.bounds.center.x, area.bounds.min.y + 0.15f, area.bounds.center.z);
                        Vector3 stableDestination = GetStableDeliveryDestination(cartFloorCenter);
                        Vector3 approach = GetCartApproachPoint(stableDestination);

                        MoveTo(approach, 1.2f); // Aumentado para 1.2f

                        float distToCart = HorizontalDistance(transform.position, cartFloorCenter);
                        float distToApproach = HorizontalDistance(transform.position, approach);

                        bool stuckOnForcefield = agent.enabled && agent.pathStatus == NavMeshPathStatus.PathPartial && distToCart <= cartDropDistance + 1.0f;

                        // Margens de tolerância aumentadas
                        if (distToApproach <= 1.2f || distToCart <= cartDropDistance + 1.0f || stuckOnForcefield)
                        {
                            StopMoving();
                            DropItemInBounds(area.bounds);
                            state = PetState.FollowOwner;
                        }
                        return;
                    }
                }

                if (targetExtractionPoint == null || !IsExtractionPointUsable(targetExtractionPoint, false))
                    targetExtractionPoint = FindUsableExtractionPoint(false);

                if (targetExtractionPoint != null && IsExtractionPointUsable(targetExtractionPoint, false))
                {
                    Vector3 extractionCenter = GetUnlockedExtractionCenter(targetExtractionPoint);
                    Vector3 stableDestination = GetStableDeliveryDestination(extractionCenter);
                    Vector3 approach = GetCartApproachPoint(stableDestination);

                    MoveTo(approach, 0.6f);

                    if (HorizontalDistance(transform.position, approach) <= cartDropDistance + 0.5f)
                    {
                        StopMoving();
                        ReleaseCarriedItem(extractionCenter, false);
                        state = PetState.FollowOwner;
                    }
                    return;
                }
            }

            DisablePetImpactProcessing();
            DropItemAtFeetSafe();
            state = PetState.FollowOwner;
        }

        private bool IsValidCarryableItem(PhysGrabObject item)
        {
            if (item == null || item == myGrabObject) return false;
            if (item.GetComponent<PhysGrabCart>() != null || item.GetComponentInParent<PhysGrabCart>() != null) return false;
            if (item.GetComponent<ValuableObject>() != null) return true;

            ItemAttributes itemAttr = item.GetComponent<ItemAttributes>();
            if (itemAttr != null)
            {
                FieldInfo shopItemField = AccessTools.Field(typeof(ItemAttributes), "shopItem");
                if (shopItemField != null && shopItemField.GetValue(itemAttr) is bool isShopItem && isShopItem) return true;
                return true;
            }

            if (item.GetComponent("ItemAttributes") != null) return true;
            return false;
        }

        public bool TryGiveItem(PhysGrabObject item)
        {
            if (state == PetState.Dead || state == PetState.Grabbed || state == PetState.Stunned) return false;

            if (isDeliveryLocked || state == PetState.CarryItemToCart || carriedItem != null || carriedPlayerAvatar != null) return false;
            lockedApproachPoint = null;
            lockedApproachCartCenter = Vector3.zero;

            if (item == null) return false;

            float maxDistance = PetSettings.GiveItemDistance != null ? PetSettings.GiveItemDistance.Value : 4.5f;
            if (Vector3.Distance(transform.position, item.transform.position) > maxDistance) return false;

            if (!IsValidCarryableItem(item)) return false;

            float limit = PetSettings.MaxMass != null ? PetSettings.MaxMass.Value : 3f;
            if (item.massOriginal > limit) return false;

            bool isShop = PetSpawner.IsShopContext();
            PhysGrabCart potentialCart = isShop ? null : FindNearestValidCart();
            ExtractionPoint potentialPoint = isShop ? FindUsableExtractionPoint(true) : FindUsableExtractionPoint(false);

            if (potentialCart == null && potentialPoint == null) return false;

            if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
            {
                PhotonView itemPV = item.GetComponent<PhotonView>();
                if (itemPV != null && itemPV.ViewID > 0)
                {
                    isDeliveryLocked = true;
                    var packet = new PetGiveItemPacket
                    {
                        PetViewID = GetComponent<PhotonView>().ViewID,
                        ItemViewID = itemPV.ViewID
                    };
                    RepoSteamNetwork.SendPacket(packet, NetworkDestination.HostOnly);
                    Invoke(nameof(UnlockDelivery), 1.0f);
                    return true;
                }
                return false;
            }

            if (!PickUpItem(item)) return false;

            PhotonView pv = item.GetComponent<PhotonView>();
            if (pv != null && !pv.IsMine) pv.RequestOwnership();

            noGrabUntilTime = Time.time + 2.0f;
            targetCart = potentialCart;
            targetExtractionPoint = potentialPoint;
            state = PetState.CarryItemToCart;
            isDeliveryLocked = true;
            ResetDeliveryDestination();

            if (aiAudio != null) aiAudio.PlayBark();
            return true;
        }

        private void UnlockDelivery()
        {
            isDeliveryLocked = false;
        }

        private static bool ReadBoolField(FieldInfo field, object source, bool fallback)
        {
            if (field == null || source == null) return fallback;
            try { return field.GetValue(source) is bool value ? value : fallback; } catch { return fallback; }
        }

        private bool IsExtractionPointUsable(ExtractionPoint point, bool isShop)
        {
            if (point == null || !point.gameObject.activeInHierarchy || !point.isActiveAndEnabled) return false;

            if (isShop)
            {
                Transform inCartShop = null;
                foreach (Transform child in point.GetComponentsInChildren<Transform>(true)) { if (child.name == "In Cart") { inCartShop = child; break; } }
                if (inCartShop == null || !inCartShop.gameObject.activeInHierarchy) return false;
                Collider colliderShop = inCartShop.GetComponent<Collider>();
                return colliderShop != null && colliderShop.enabled;
            }

            FieldInfo lockedField = AccessTools.Field(typeof(ExtractionPoint), "isLocked");
            FieldInfo tubeHitField = AccessTools.Field(typeof(ExtractionPoint), "tubeHit");
            FieldInfo cancelExtractionField = AccessTools.Field(typeof(ExtractionPoint), "cancelExtraction");
            FieldInfo completedRightAwayField = AccessTools.Field(typeof(ExtractionPoint), "isCompletedRightAway");
            FieldInfo surplusCompletedField = AccessTools.Field(typeof(ExtractionPoint), "extractionSurplusCompleted");

            if (ReadBoolField(lockedField, point, true) || ReadBoolField(tubeHitField, point, true) || ReadBoolField(cancelExtractionField, point, true) || ReadBoolField(completedRightAwayField, point, true) || ReadBoolField(surplusCompletedField, point, true)) return false;

            Transform inCart = null;
            foreach (Transform child in point.GetComponentsInChildren<Transform>(true)) { if (child.name == "In Cart") { inCart = child; break; } }
            if (inCart == null || !inCart.gameObject.activeInHierarchy) return false;

            Collider collider = inCart.GetComponent<Collider>();
            return collider != null && collider.enabled;
        }

        private ExtractionPoint FindUsableExtractionPoint(bool isShopMode)
        {
            PlayerAvatar player = SemiFunc.PlayerAvatarLocal();
            Vector3 origin = player != null ? player.transform.position : transform.position;

            ExtractionPoint[] points = FindObjectsOfType<ExtractionPoint>(true);
            FieldInfo isShopField = AccessTools.Field(typeof(ExtractionPoint), "isShop");

            ExtractionPoint bestPoint = null;
            float bestDistance = float.MaxValue;

            foreach (ExtractionPoint point in points)
            {
                if (!IsExtractionPointUsable(point, isShopMode)) continue;
                if (isShopField != null && isShopField.GetValue(point) is bool isShop && isShop != isShopMode) continue;

                Transform inCart = null;
                foreach (Transform child in point.GetComponentsInChildren<Transform>(true)) { if (child.name == "In Cart") { inCart = child; break; } }
                if (inCart == null) continue;

                Collider collider = inCart.GetComponent<Collider>();
                if (collider == null || !collider.enabled) continue;

                Physics.SyncTransforms();
                float distance = Vector3.SqrMagnitude(origin - collider.bounds.center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPoint = point;
                }
            }
            return bestPoint;
        }

        private Vector3 GetUnlockedExtractionCenter(ExtractionPoint point)
        {
            if (point == null) return transform.position;
            foreach (Transform child in point.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != "In Cart") continue;
                Collider collider = child.GetComponent<Collider>();
                if (collider == null || !collider.enabled) continue;

                Physics.SyncTransforms();
                Vector3 center = collider.bounds.center;
                center.y = collider.bounds.min.y + 0.40f;
                return center;
            }
            return point.transform.position;
        }

        private void CreateHoldPoint()
        {
            if (holdPoint != null) return;

            GameObject holdObject = new GameObject("AiChanHoldPoint");
            holdPoint = holdObject.transform;
            holdPoint.SetParent(transform, false);

            holdPoint.localPosition = new Vector3(0f, 1.45f, 0.10f);
            holdPoint.localRotation = Quaternion.identity;
            holdPoint.localScale = Vector3.one;
        }

        private PhysGrabCart FindNearestValidCart()
        {
            PhysGrabCart best = null;
            float bestDistance = float.MaxValue;

            foreach (PhysGrabCart cart in FindObjectsOfType<PhysGrabCart>(true))
            {
                if (cart == null || !cart.gameObject.activeInHierarchy) continue;
                BoxCollider area = GetCartDepositArea(cart);
                if (area == null) continue;

                float distance = Vector3.SqrMagnitude(area.bounds.center - transform.position);
                if (distance < bestDistance)
                {
                    best = cart;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private BoxCollider GetCartDepositArea(PhysGrabCart cart)
        {
            if (cart == null) return null;
            Transform inCart = cart.transform.Find("In Cart");
            return inCart == null ? null : inCart.GetComponent<BoxCollider>();
        }

        private bool PickUpItem(PhysGrabObject item)
        {
            if (carriedItem != null || carriedPlayerAvatar != null) return false;
            if (item == null) return false;

            if (item.playerGrabbing != null && item.playerGrabbing.Count > 0)
            {
                List<PhysGrabber> grabbers = new List<PhysGrabber>(item.playerGrabbing);
                foreach (PhysGrabber grabber in grabbers)
                {
                    if (grabber != null) grabber.ReleaseObjectRPC(false, 1f, -1);
                }
            }

            CreateHoldPoint();
            SnapToFloorSafely();

            carriedItem = item;
            carriedOriginalScale = item.transform.localScale;
            carriedInheritScale = PetSettings.InheritPetScaleOnCarry != null && PetSettings.InheritPetScaleOnCarry.Value;

            carriedRigidbody = item.GetComponent<Rigidbody>();
            carriedColliders = item.GetComponentsInChildren<Collider>(true);
            carriedOriginalTriggers = new bool[carriedColliders.Length];
            carriedOriginalLayers = new int[carriedColliders.Length];
            carriedWasKinematic = carriedRigidbody != null && carriedRigidbody.isKinematic;

            Collider myCollider = GetComponent<Collider>();

            for (int i = 0; i < carriedColliders.Length; i++)
            {
                Collider collider = carriedColliders[i];
                if (collider == null) continue;

                carriedOriginalTriggers[i] = collider.isTrigger;
                carriedOriginalLayers[i] = collider.gameObject.layer;
                collider.gameObject.layer = 2; // Ignore Raycast

                if (!(collider is MeshCollider mc && !mc.convex)) collider.isTrigger = true;
                if (myCollider != null) Physics.IgnoreCollision(myCollider, collider, true);
            }

            if (carriedRigidbody != null)
            {
                if (!carriedRigidbody.isKinematic)
                {
                    carriedRigidbody.velocity = Vector3.zero;
                    carriedRigidbody.angularVelocity = Vector3.zero;
                }
                carriedRigidbody.isKinematic = true;
                carriedRigidbody.useGravity = false;
            }

            // PASSO 4: Desliga o script do jogo que briga com a física
            if (item != null) item.enabled = false;

            item.transform.SetParent(holdPoint, false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;

            ApplyCarriedItemScale(item, carriedInheritScale);

            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                PhotonView pv = item.GetComponent<PhotonView>();
                if (pv != null)
                {
                    var syncPacket = new PetSyncCarryPacket
                    {
                        PetViewID = GetComponent<PhotonView>().ViewID,
                        TargetViewID = pv.ViewID,
                        IsPlayer = false,
                        IsPickingUp = true,
                        InheritScale = carriedInheritScale
                    };
                    RepoSteamNetwork.SendPacket(syncPacket, NetworkDestination.EveryoneExcludingSender);
                }
            }

            return true;
        }

        private void ApplyCarriedItemScale(PhysGrabObject item, bool inheritScale)
        {
            if (item == null) return;

            if (inheritScale)
            {
                Vector3 worldScale = Vector3.Scale(carriedOriginalScale, transform.localScale);
                worldScale.x = Mathf.Max(worldScale.x, MinItemScaleCap);
                worldScale.y = Mathf.Max(worldScale.y, MinItemScaleCap);
                worldScale.z = Mathf.Max(worldScale.z, MinItemScaleCap);

                Vector3 petLossy = transform.lossyScale;
                item.transform.localScale = new Vector3(
                    petLossy.x > 0.0001f ? worldScale.x / petLossy.x : carriedOriginalScale.x,
                    petLossy.y > 0.0001f ? worldScale.y / petLossy.y : carriedOriginalScale.y,
                    petLossy.z > 0.0001f ? worldScale.z / petLossy.z : carriedOriginalScale.z
                );
            }
            else
            {
                Vector3 petLossy = transform.lossyScale;
                item.transform.localScale = new Vector3(
                    petLossy.x > 0.0001f ? carriedOriginalScale.x / petLossy.x : carriedOriginalScale.x,
                    petLossy.y > 0.0001f ? carriedOriginalScale.y / petLossy.y : carriedOriginalScale.y,
                    petLossy.z > 0.0001f ? carriedOriginalScale.z / petLossy.z : carriedOriginalScale.z
                );
            }
        }

        // SUBSTITUIR POR:
        private Vector3 GetCartApproachPoint(Vector3 centerPoint)
        {
            if (lockedApproachPoint.HasValue)
            {
                if (Vector3.Distance(centerPoint, lockedApproachCartCenter) < 1.0f)
                {
                    return lockedApproachPoint.Value;
                }
            }

            Vector3 direction = transform.position - centerPoint;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = -transform.forward;

            // Expandimos levemente a busca bruta para garantir captura
            Vector3 rawApproach = centerPoint + direction.normalized * (cartApproachDistance + 0.50f);

            if (NavMesh.SamplePosition(rawApproach, out NavMeshHit hit, 3.5f, NavMesh.AllAreas))
            {
                // O recuo radial de segurança: Retrai o ponto em direção à Ai-Chan, libertando
                // a zona afiada da fenda gerada pelo NavMeshObstacle do carrinho.
                Vector3 safeApproach = hit.position + direction.normalized * 0.35f;

                if (NavMesh.SamplePosition(safeApproach, out NavMeshHit safeHit, 1.5f, NavMesh.AllAreas))
                {
                    lockedApproachPoint = safeHit.position;
                }
                else
                {
                    lockedApproachPoint = hit.position;
                }
                lockedApproachCartCenter = centerPoint;
                return lockedApproachPoint.Value;
            }

            // Fallback: se o raio falhar perto do carrinho, busca a NavMesh mais próxima da própria pet em direção ao carrinho
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit petHit, 3.5f, NavMesh.AllAreas))
            {
                lockedApproachPoint = petHit.position;
                lockedApproachCartCenter = centerPoint;
                return petHit.position;
            }

            lockedApproachPoint = centerPoint;
            lockedApproachCartCenter = centerPoint;
            return centerPoint;
        }

        private void DropItemInBounds(Bounds bounds)
        {
            Vector3 dropPoint = new Vector3(bounds.center.x, bounds.max.y + 0.05f, bounds.center.z);
            ReleaseCarriedItem(dropPoint, false);
        }

        private void DropItemAtFeetSafe()
        {
            DisablePetImpactProcessing();
            if (carriedItem == null && carriedPlayerAvatar == null) { ClearCarryState(); return; }

            try { DropItemAtFeet(); }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning("[Ai-Chan] Falha ao soltar item: " + exception.Message);
                ClearCarryState();
            }
        }

        private void DropItemAtFeet()
        {
            if (carriedPlayerAvatar != null) { ReleaseCarriedPlayer(); return; }
            if (carriedItem == null) return;
            ReleaseCarriedItem(transform.position + transform.forward * 0.5f + Vector3.up * 0.2f, false);
        }

        private void ReleaseCarriedItem(Vector3 point, bool impulse)
        {
            bool wasInheritingScale = carriedInheritScale;

            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && carriedItem != null)
            {
                PhotonView ipv = carriedItem.GetComponent<PhotonView>();
                if (ipv != null)
                {
                    var syncPacket = new PetSyncCarryPacket
                    {
                        PetViewID = GetComponent<PhotonView>().ViewID,
                        TargetViewID = ipv.ViewID,
                        IsPlayer = false,
                        IsPickingUp = false,
                        InheritScale = wasInheritingScale
                    };
                    RepoSteamNetwork.SendPacket(syncPacket, NetworkDestination.EveryoneExcludingSender);
                }
            }

            PhysGrabObject item = carriedItem;
            Vector3 originalScale = carriedOriginalScale;
            Rigidbody body = carriedRigidbody;
            Collider[] colliders = carriedColliders;
            bool[] originalTriggers = carriedOriginalTriggers;
            int[] originalLayers = carriedOriginalLayers;
            bool wasKinematic = carriedWasKinematic;

            Collider myCollider = GetComponent<Collider>();
            ClearCarryState();

            if (item == null) return;

            item.enabled = true; // RELIGA script nativo do item ao soltar

            PhysGrabObjectImpactDetector detector = item.GetComponent<PhysGrabObjectImpactDetector>();
            if (detector != null) detector.playerHurtDisable = true;

            item.transform.SetParent(null, true);
            item.transform.position = point;
            item.transform.rotation = Quaternion.identity;

            // PASSO 4 (Reverso): Devolve o controle ao R.E.P.O
            item.enabled = true;

            // Se o item não estava herdando a escala (proporção), voltamos ele pro original
            if (!wasInheritingScale)
            {
                Vector3 restoredScale = originalScale;
                restoredScale.x = Mathf.Max(restoredScale.x, MinItemScaleCap);
                restoredScale.y = Mathf.Max(restoredScale.y, MinItemScaleCap);
                restoredScale.z = Mathf.Max(restoredScale.z, MinItemScaleCap);
                item.transform.localScale = restoredScale;
            }
            // Se wasInheritingScale for true, nada acontece com a escala (ele preserva o tamanho modificado visual no mundo).

            if (body != null)
            {
                body.position = point;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = wasKinematic;
                body.useGravity = !wasKinematic;
            }

            if (colliders != null)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] == null) continue;
                    colliders[i].isTrigger = originalTriggers != null && i < originalTriggers.Length ? originalTriggers[i] : false;
                    if (originalLayers != null && i < originalLayers.Length) colliders[i].gameObject.layer = originalLayers[i];
                    if (myCollider != null) Physics.IgnoreCollision(myCollider, colliders[i], false);
                }
            }

            Physics.SyncTransforms();
            if (impulse && body != null && !body.isKinematic) body.AddForce(Vector3.down * 1.5f, ForceMode.Impulse);
        }

        private void ClearCarryState()
        {
            // Restaura o NavMeshObstacle do carrinho ao finalizar a entrega
            if (targetCart != null)
            {
                SetCartObstacleEnabled(targetCart, true);
            }

            isDeliveryLocked = false;
            carriedItem = null;
            carriedOriginalScale = Vector3.one;
            carriedInheritScale = false;
            carriedPlayerAvatar = null;
            carriedTumble = null;
            carriedRigidbody = null;
            carriedColliders = null;
            carriedOriginalTriggers = null;
            carriedOriginalLayers = null;
            carriedWasKinematic = false;
            targetCart = null;
            targetExtractionPoint = null;
            lockedApproachPoint = null;
        }

        public void NetworkSyncCarry(int targetViewID, bool isPlayer, bool isPickingUp, bool inheritScale)
        {
            if (PhotonNetwork.IsMasterClient) return;

            PhotonView targetPV = PhotonNetwork.GetPhotonView(targetViewID);
            if (targetPV == null) return;

            if (isPickingUp)
            {
                CreateHoldPoint();
                if (isPlayer)
                {
                    PlayerAvatar p = targetPV.GetComponent<PlayerAvatar>();
                    if (p != null) ClientSidePickUpPlayer(p);
                }
                else
                {
                    PhysGrabObject item = targetPV.GetComponent<PhysGrabObject>();
                    if (item != null) ClientSidePickUpItem(item, inheritScale);
                }
            }
            else
            {
                ClientSideDrop(inheritScale);
            }
        }

        private void ClientSidePickUpItem(PhysGrabObject item, bool inheritScale)
        {
            CreateHoldPoint();
            carriedItem = item;
            carriedOriginalScale = item.transform.localScale;
            carriedInheritScale = inheritScale; // O Client segue e obedece a config do Master

            carriedRigidbody = item.GetComponent<Rigidbody>();
            if (carriedRigidbody != null)
            {
                if (!carriedRigidbody.isKinematic)
                {
                    carriedRigidbody.velocity = Vector3.zero;
                    carriedRigidbody.angularVelocity = Vector3.zero;
                }
                carriedRigidbody.isKinematic = true;
                carriedRigidbody.useGravity = false;
            }

            item.enabled = false; // DESATIVA script nativo do item

            item.transform.SetParent(holdPoint, false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;

            ApplyCarriedItemScale(item, carriedInheritScale);

            Collider myCollider = GetComponent<Collider>();
            carriedColliders = item.GetComponentsInChildren<Collider>(true);
            carriedOriginalTriggers = new bool[carriedColliders.Length];
            carriedOriginalLayers = new int[carriedColliders.Length];

            for (int i = 0; i < carriedColliders.Length; i++)
            {
                if (carriedColliders[i] == null) continue;
                carriedOriginalTriggers[i] = carriedColliders[i].isTrigger;
                carriedOriginalLayers[i] = carriedColliders[i].gameObject.layer;
                carriedColliders[i].gameObject.layer = 2;
                if (!(carriedColliders[i] is MeshCollider mc && !mc.convex)) carriedColliders[i].isTrigger = true;
                if (myCollider != null) Physics.IgnoreCollision(myCollider, carriedColliders[i], true);
            }
        }

        public void ForceDropItem()
        {
            // Checa se ela realmente está segurando algo
            if (state == PetState.CarryItemToCart || carriedItem != null || carriedPlayerAvatar != null)
            {
                // Usa a sua própria função segura para jogar o item no chão,
                // restaurar a escala e avisar a rede (Photon)
                DropItemAtFeetSafe();

                // Volta a focar no dono
                state = PetState.FollowOwner;

                // Dá um latido/som de confirmação
                if (aiAudio != null) aiAudio.PlayBark();

                Plugin.Log.LogInfo("[Ai-Chan] Item largado à força por comando do Dono!");
            }
        }

        private void ClientSidePickUpPlayer(PlayerAvatar targetPlayer)
        {
            CreateHoldPoint();
            carriedPlayerAvatar = targetPlayer;
            carriedTumble = GetPlayerTumble(targetPlayer);
            carriedRigidbody = GetTumbleRigidbody(carriedTumble);

            if (targetPlayer == SemiFunc.PlayerAvatarLocal() && carriedRigidbody != null)
            {
                carriedWasKinematic = carriedRigidbody.isKinematic;
                carriedRigidbody.isKinematic = true;
                carriedRigidbody.useGravity = false;
            }

            Collider myCollider = GetComponent<Collider>();
            if (carriedTumble != null)
            {
                carriedColliders = carriedTumble.GetComponentsInChildren<Collider>(true);
                carriedOriginalTriggers = new bool[carriedColliders.Length];
                carriedOriginalLayers = new int[carriedColliders.Length];
                for (int i = 0; i < carriedColliders.Length; i++)
                {
                    if (carriedColliders[i] == null) continue;
                    carriedOriginalTriggers[i] = carriedColliders[i].isTrigger;
                    carriedOriginalLayers[i] = carriedColliders[i].gameObject.layer;
                    carriedColliders[i].gameObject.layer = 2;
                    if (!(carriedColliders[i] is MeshCollider mc && !mc.convex)) carriedColliders[i].isTrigger = true;
                    if (myCollider != null) Physics.IgnoreCollision(myCollider, carriedColliders[i], true);
                }
            }
        }

        private void ClientSideDrop(bool inheritScale)
        {
            Collider myCollider = GetComponent<Collider>();
            if (carriedColliders != null)
            {
                for (int i = 0; i < carriedColliders.Length; i++)
                {
                    if (carriedColliders[i] == null) continue;
                    carriedColliders[i].isTrigger = (carriedOriginalTriggers != null && i < carriedOriginalTriggers.Length) ? carriedOriginalTriggers[i] : false;
                    if (carriedOriginalLayers != null && i < carriedOriginalLayers.Length) carriedColliders[i].gameObject.layer = carriedOriginalLayers[i];
                    if (myCollider != null) Physics.IgnoreCollision(myCollider, carriedColliders[i], false);
                }
            }

            if (carriedPlayerAvatar == SemiFunc.PlayerAvatarLocal() && carriedRigidbody != null)
            {
                carriedRigidbody.isKinematic = carriedWasKinematic;
                carriedRigidbody.useGravity = !carriedWasKinematic;
            }
            else if (carriedItem != null && carriedRigidbody != null)
            {
                carriedItem.enabled = true;
                carriedItem.transform.SetParent(null, true);

                if (!inheritScale)
                {
                    Vector3 restoredScale = carriedOriginalScale;
                    restoredScale.x = Mathf.Max(restoredScale.x, MinItemScaleCap);
                    restoredScale.y = Mathf.Max(restoredScale.y, MinItemScaleCap);
                    restoredScale.z = Mathf.Max(restoredScale.z, MinItemScaleCap);
                    carriedItem.transform.localScale = restoredScale;
                }
            }

            ClearCarryState();
        }
    }
}