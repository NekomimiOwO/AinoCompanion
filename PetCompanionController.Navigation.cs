using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;

namespace ElsaPetMod
{
    public partial class PetCompanionController
    {
        public float followDistance = 2.2f;
        public float followStoppingDistance = 1.65f;

        private PhysGrabHinge[] cachedDoors;
        private float nextDoorCacheTime;

        private float nextPlayerSwitchTime;
        private bool isFollowingOwner;
        private Vector3 lastDestination = Vector3.positiveInfinity;
        private float lastSetDestinationTime;

        private float nextProbeTime;
        private Vector3 cachedBestDir = Vector3.forward;

        // --- VARIÁVEIS DAS NOVAS MELHORIAS ---
        private float panicTimer;
        private Vector3 panicDirection;
        private Vector3 lastWallHitNormal;
        private Vector3[] navMeshCorners = new Vector3[16]; // Cache para não gerar Garbage Collection

        // --- ADICIONE ESTAS 3 AQUI (Fim do bug do pulo) ---
        private bool isActuallyMoving;
        private Vector3 lastPosCheck;
        private float posCheckTimer;
        // --- ADICIONE ESTAS DUAS AQUI ---
        private float nextNavMeshCheckTime;

        // --- ADICIONE ESTAS DUAS (Fim da briga do pulo) ---
        private float jumpTimer;
        private bool isPreparingToJump;

        private const float NavDestinationUpdateDistance = 0.5f;
        private const float NavDestinationUpdateInterval = 0.15f;
        private float stuckTimer;
        private float nextJumpTime;
        private float nextJumpBarkTime;
        private float stuckHighTimer;
        private float navMeshCooldown;
        private float offNavMeshStuckTimer;

        private float escapeTimer;
        private Vector3 escapeDirection = Vector3.zero;

        // Controle de amortecimento e projeção fantasma fora da NavMesh
        private Vector3 smoothedManualDir = Vector3.forward;
        private Vector3 lastStuckCheckPos;
        private float stuckPositionCheckTimer;
        private float lastPinDropTime;
        // --- SISTEMA DE ABERTURA NATIVA DE PORTAS (MIMIC LOGIC) ---
        private static readonly MethodInfo DoorOpenMethod = typeof(PhysGrabHinge).GetMethod("OpenImpulse", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DoorClosedField = typeof(PhysGrabHinge).GetField("closed", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Dictionary<int, float> doorOpenCooldowns = new Dictionary<int, float>();
        private float nextDoorCheckTime;

        public bool hasManualMoveTarget;
        public Vector3 manualMoveTarget;
        public float manualMoveEndsAt;
        public float autoMovementSuspendedUntil; // Para a regra dos 30s

        private const float ManualMoveDuration = 12f;
        private const float ManualMoveStoppingDistance = 0.30f;

        private class DeadEndMemory
        {
            public Vector3 Position;
            public float Timer;
        }
        private readonly List<DeadEndMemory> deadEndMemories = new List<DeadEndMemory>();

        private readonly List<Vector3> ownerBreadcrumbs = new List<Vector3>();
        private readonly RaycastHit[] surfaceHitsBuffer = new RaycastHit[15];
        private readonly Collider[] overlapCollidersBuffer = new Collider[16];
        private readonly List<PlayerAvatar> chosenOwnersThisLevel = new List<PlayerAvatar>();

        private void LogNavMeshTransition(bool toNavMesh, string reason)
        {
            if (PetSettings.EnableDebugLogs != null && PetSettings.EnableDebugLogs.Value)
            {
                Plugin.Log.LogInfo($"[Ai-Chan] [NavMesh] {(toNavMesh ? "ENTERED NavMesh" : "EXIT NavMesh (Manual Control)")} | Motivo: {reason}");
            }
        }

        private bool IsDoor(Collider col)
        {
            if (col == null) return false;

            // A CURA DAS PRATELEIRAS: 
            // Usa a sua descoberta da imagem! Apenas portas reais do mapa possuem o marcador DirtFinderMapDoor.
            Transform current = col.transform;
            while (current != null)
            {
                if (current.GetComponent("DirtFinderMapDoor") != null) return true;
                current = current.parent;
            }

            return false;
        }

        private bool IsColliderPlayerOrItem(Collider col)
        {
            if (col == null || col.isTrigger) return true;
            if (col.transform.IsChildOf(transform)) return true;

            int layer = col.gameObject.layer;
            if (layer == LayerMask.NameToLayer("Player") || layer == LayerMask.NameToLayer("PlayerOnlyCollision") || layer == 14) return true;

            if (col.GetComponentInParent<PlayerAvatar>() != null) return true;
            if (col.GetComponentInParent<PhysGrabObject>() != null) return true;

            // O SEGREDO DA TREPIDAÇÃO: Faz a Ai-Chan ignorar o Carrinho e o Extrator como "Paredes de pulo".
            // Isso impede ela de brecar o agente infinitamente tentando pular em cima do carrinho!
            if (col.GetComponentInParent<PhysGrabCart>() != null) return true;
            if (col.GetComponentInParent<ExtractionPoint>() != null) return true;

            if (IsDoor(col)) return true;

            return false;
        }

        private bool IsDoorClosed(PhysGrabHinge hinge)
        {
            if (hinge == null || DoorClosedField == null) return false;
            try
            {
                return Convert.ToBoolean(DoorClosedField.GetValue(hinge));
            }
            catch
            {
                return false;
            }
        }

        private void TryOpenNearbyDoors()
        {
            if (PetSettings.EnableDoorOpening != null && !PetSettings.EnableDoorOpening.Value)
                return;
                
            if (DoorOpenMethod == null || Time.time < nextDoorCheckTime) return;
            nextDoorCheckTime = Time.time + 0.35f;

            // SISTEMA DE CACHE: Só faz a busca pesada no mapa a cada 15 segundos
            if (cachedDoors == null || Time.time >= nextDoorCacheTime)
            {
                cachedDoors = UnityEngine.Object.FindObjectsOfType<PhysGrabHinge>();
                nextDoorCacheTime = Time.time + 15.0f;
            }

            if (cachedDoors.Length == 0) return;

            PhysGrabHinge nearestDoor = null;
            float minDistance = 4.0f; // Nova tolerância ajustada

            // --- VISUAL DEBUGGING (RADAR DE PORTAS) ---
            bool drawDebugRays = PetSettings.EnableDebugRays != null && PetSettings.EnableDebugRays.Value;
            if (drawDebugRays)
            {
                Vector3 center = transform.position + Vector3.up * 0.5f;
                // Desenha a cruz roxa baseada na direção que a Ai-Chan está olhando (transform.forward e transform.right)
                PetRuntimeDrawer.DrawLine(center - transform.forward * 4.0f, center + transform.forward * 4.0f, new Color(0.6f, 0f, 1f), 0.35f);
                PetRuntimeDrawer.DrawLine(center - transform.right * 4.0f, center + transform.right * 4.0f, new Color(0.6f, 0f, 1f), 0.35f);
            }

            // Loop de busca otimizado
            // Loop de busca otimizado
            for (int i = 0; i < cachedDoors.Length; i++)
            {
                PhysGrabHinge door = cachedDoors[i];

                if (door == null || !IsDoorClosed(door)) continue;

                // --- A CURA DAS PRATELEIRAS ---
                // Ignora imediatamente qualquer dobradiça que não tenha o marcador do minimapa.
                // Isso salva os armários, caixas e prateleiras de serem arrombados!
                if (door.GetComponent("DirtFinderMapDoor") == null) continue;

                float dist;
                Collider doorCol = door.GetComponentInChildren<Collider>();

                if (doorCol != null)
                {
                    Vector3 closestPoint = doorCol.ClosestPoint(transform.position);
                    dist = Vector3.Distance(transform.position, closestPoint);

                    // DEBUG: Desenha uma linha fina roxa se a porta entrar no raio de detecção
                    if (drawDebugRays && dist < 4.0f)
                    {
                        PetRuntimeDrawer.DrawLine(transform.position + Vector3.up * 0.5f, closestPoint, new Color(0.6f, 0f, 1f, 0.5f), 0.35f);
                    }
                }
                else
                {
                    dist = Vector3.Distance(transform.position, door.transform.position);
                }

                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearestDoor = door;
                }
            }

            if (nearestDoor == null) return;

            // DEBUG: Se achou o alvo final, desenha um laser rosa na porta escolhida por 2 segundos!
            if (drawDebugRays)
            {
                PetRuntimeDrawer.DrawLine(transform.position + Vector3.up * 0.5f, nearestDoor.transform.position, Color.magenta, 2.0f);
            }

            int instanceID = nearestDoor.GetInstanceID();
            if (doorOpenCooldowns.TryGetValue(instanceID, out float cd) && Time.time < cd) return;

            // Cooldown de 2 segundos para não dar spam de chamadas
            doorOpenCooldowns[instanceID] = Time.time + 2.0f;

            try
            {
                PhysGrabObject grabObj = nearestDoor.GetComponentInParent<PhysGrabObject>();
                if (grabObj != null)
                {
                    grabObj.EnemyInteractTimeSet();
                    // Deixa a porta bem leve temporariamente (1kg) para abrir fácil
                    grabObj.OverrideMass(1f, 1.5f);
                }

                // Destrava a fechadura nativa do jogo
                DoorOpenMethod.Invoke(nearestDoor, null);

                // --- A CURA DAS PORTAS DUPLAS (Empurrão Físico Orgânico) ---
                Rigidbody rb = nearestDoor.GetComponent<Rigidbody>() ?? nearestDoor.GetComponentInParent<Rigidbody>();

                if (rb != null && !rb.isKinematic)
                {
                    rb.WakeUp();

                    // Usa a exata direção horizontal em que a Ai-Chan está andando/olhando
                    Vector3 pushDir = transform.forward;
                    pushDir.y = 0f;

                    // Dá um "chute" (Impulse) de 5kg na porta!
                    // Ao aplicar a força no centro de massa, a Unity calcula o arco da dobradiça
                    // automaticamente, forçando ambas as portas a abrirem para a frente, fugindo dela.
                    rb.AddForce(pushDir.normalized * 15f, ForceMode.Impulse);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Ai-Chan] Error opening door: " + ex.Message);
            }
        }

        private void ResolvePenetrations()
        {
            if (state == PetState.CarryItemToCart) return;
            // PASSO 6: Pula a resolução de atrito durante a entrega

            if (myCapsule == null)
                myCapsule = GetComponent<CapsuleCollider>();

            if (myCapsule == null || !myCapsule.enabled)
                return;

            // CORREÇÃO FÍSICA: Usando TransformPoint e a escala real para criar o collider virtual
            Vector3 scaledCenter = Vector3.Scale(myCapsule.center, transform.lossyScale);
            Vector3 center = transform.TransformPoint(scaledCenter);
            float radius = myCapsule.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
            float height = myCapsule.height * Mathf.Abs(transform.lossyScale.y);
            float halfSegment = Mathf.Max(0f, height * 0.5f - radius);

            Vector3 p1 = center - transform.up * halfSegment;
            Vector3 p2 = center + transform.up * halfSegment;
            int mask = GetGroundMask();

            int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, overlapCollidersBuffer, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = overlapCollidersBuffer[i];
                if (other == null) continue;
                if (IsColliderPlayerOrItem(other)) continue;

                if (Physics.ComputePenetration(
                    myCapsule, transform.position, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 dir, out float dist))
                {
                    transform.position += dir * dist;
                }
            }
        }

        public void CommandMoveTo(Vector3 worldPoint, float suspendDuration = 0f)
        {
            if (state == PetState.Dead || state == PetState.Grabbed || state == PetState.Stunned)
                return;

            if (!UnityEngine.AI.NavMesh.SamplePosition(worldPoint, out UnityEngine.AI.NavMeshHit navHit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
            {
                Plugin.Log.LogWarning($"[Ai-Chan] Manual destination without NavMesh: {worldPoint}");
                return;
            }

            manualMoveTarget = navHit.position;
            hasManualMoveTarget = true;
            manualMoveEndsAt = Time.time + ManualMoveDuration;

            if (suspendDuration > 0f)
            {
                autoMovementSuspendedUntil = Time.time + suspendDuration;
            }

            isCalledByOwner = false;
            awayTimer = 0f;

            // Evita reaproveitar approach point do carrinho anterior.
            lockedApproachPoint = null;

            Plugin.Log.LogInfo($"[Ai-Chan] Manual target: {manualMoveTarget}");

            if (aiAudio != null)
                aiAudio.PlayBark();
        }

        public void CancelManualMove()
        {
            hasManualMoveTarget = false;
            manualMoveEndsAt = 0f;
            StopMoving();
        }

        private bool TickManualMove()
        {
            bool isSuspended = Time.time < autoMovementSuspendedUntil;

            if (!hasManualMoveTarget)
            {
                if (isSuspended)
                {
                    StopMoving();
                    return true; // Bloqueia o automático e fica parada
                }
                return false;
            }

            if (Time.time >= manualMoveEndsAt)
            {
                CancelManualMove();
                return isSuspended; // Se o tempo de andar acabou mas a suspensão não, continua bloqueando auto
            }

            MoveTo(manualMoveTarget, ManualMoveStoppingDistance);

            Vector3 flatDelta = manualMoveTarget - transform.position;
            flatDelta.y = 0f;

            if (flatDelta.sqrMagnitude <= ManualMoveStoppingDistance * ManualMoveStoppingDistance)
            {
                hasManualMoveTarget = false;
                StopMoving();
            }

            return true;
        }

        private bool CheckPathClear(Vector3 p1, Vector3 p2, float radius, Vector3 dir, float dist, int mask, out RaycastHit safeHit)
        {
            safeHit = default;
            int hitCount = Physics.CapsuleCastNonAlloc(p1, p2, radius, dir, surfaceHitsBuffer, dist, mask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float closest = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = surfaceHitsBuffer[i];
                if (hit.collider == null) continue;
                if (IsColliderPlayerOrItem(hit.collider)) continue;

                float relativeTop = hit.collider.bounds.max.y - transform.position.y;
                if (relativeTop < 0.18f * transform.localScale.y) continue;

                if (Mathf.Abs(hit.normal.y) > 0.55f) continue;

                if (hit.distance < closest)
                {
                    closest = hit.distance;
                    safeHit = hit;
                    found = true;
                }
            }
            return found;
        }

        private bool HasLineOfSightSafe(Vector3 start, Vector3 end, int mask)
        {
            Vector3 dir = end - start;
            float dist = dir.magnitude;
            if (dist < 0.01f) return true;

            int hitCount = Physics.RaycastNonAlloc(start, dir.normalized, surfaceHitsBuffer, dist, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                if (!IsColliderPlayerOrItem(surfaceHitsBuffer[i].collider))
                {
                    return false;
                }
            }
            return true;
        }

        private float GetSafeRaycastHeight(Vector3 position, float desiredHeight)
        {
            int mask = GetGroundMask();
            int hitCount = Physics.RaycastNonAlloc(position + Vector3.up * 0.05f, Vector3.up, surfaceHitsBuffer, desiredHeight, mask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = surfaceHitsBuffer[i];
                if (IsColliderPlayerOrItem(hit.collider)) continue;

                return Mathf.Max(0.5f, hit.distance - 0.05f);
            }

            return desiredHeight;
        }

        private bool FindSupportSurface(Vector3 origin, float distance, out float surfaceY)
        {
            surfaceY = -9999f;
            int mask = GetGroundMask();

            Vector3[] footOffsets = {
                Vector3.zero,
                Vector3.forward * 0.18f,
                Vector3.back * 0.18f,
                Vector3.left * 0.18f,
                Vector3.right * 0.18f
            };

            float[] hitHeights = new float[5];
            bool[] hasHit = new bool[5];
            int validHitCount = 0;

            for (int j = 0; j < footOffsets.Length; j++)
            {
                Vector3 checkOrigin = origin + footOffsets[j];
                hasHit[j] = false;
                hitHeights[j] = -9999f;

                int hitCount = Physics.RaycastNonAlloc(checkOrigin, Vector3.down, surfaceHitsBuffer, distance, mask, QueryTriggerInteraction.Ignore);
                float highestValidY = -9999f;
                bool foundPoint = false;

                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = surfaceHitsBuffer[i];
                    if (IsColliderPlayerOrItem(hit.collider)) continue;
                    if (hit.normal.y < 0.30f) continue;
                    if (hit.point.y > origin.y + 0.1f) continue;

                    if (hit.point.y > highestValidY)
                    {
                        highestValidY = hit.point.y;
                        foundPoint = true;
                    }
                }

                if (!foundPoint)
                {

                    hitCount = Physics.SphereCastNonAlloc(checkOrigin, 0.25f, Vector3.down, surfaceHitsBuffer, distance, mask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < hitCount; i++)
                    {
                        RaycastHit hit = surfaceHitsBuffer[i];
                        if (IsColliderPlayerOrItem(hit.collider)) continue;
                        if (hit.normal.y < 0.30f) continue;
                        if (hit.point.y > origin.y + 0.1f) continue;

                        if (hit.point.y > highestValidY)
                        {
                            highestValidY = hit.point.y;
                            foundPoint = true;
                        }
                    }
                }

                if (foundPoint)
                {
                    hitHeights[j] = highestValidY;
                    hasHit[j] = true;
                    validHitCount++;
                }
            }

            if (validHitCount == 0) return false;

            if (hasHit[0])
            {
                float centerY = hitHeights[0];
                int higherCount = 0;
                float maxHighY = centerY;

                for (int j = 1; j < 5; j++)
                {
                    if (hasHit[j] && hitHeights[j] > centerY + 0.08f)
                    {
                        higherCount++;
                        if (hitHeights[j] > maxHighY) maxHighY = hitHeights[j];
                    }
                }

                if (higherCount >= 3) surfaceY = maxHighY;
                else surfaceY = centerY;

                return true;
            }
            else
            {
                float maxY = -9999f;
                for (int j = 1; j < 5; j++)
                {
                    if (hasHit[j] && hitHeights[j] > maxY) maxY = hitHeights[j];
                }

                if (maxY != -9999f)
                {
                    surfaceY = maxY;
                    return true;
                }
            }

            return false;
        }

        private void UpdateContinuousSurfaceOffset()
        {
            // PASSO 6: Evita oscilações verticais com a carga
            if (state == PetState.CarryItemToCart) return;

            if (agent == null || state == PetState.Grabbed || state == PetState.Stunned || isJumping || IsRecovering)
                return;

            if (!agent.enabled) return;

            float safeHeight = GetSafeRaycastHeight(transform.position, 0.65f);
            Vector3 origin = transform.position + Vector3.up * safeHeight;

            if (FindSupportSurface(origin, safeHeight + 1.2f, out float surfaceY))
            {
                float agentFloorY = transform.position.y - agent.baseOffset;
                float heightDiff = surfaceY - agentFloorY;

                float targetOffset = Mathf.Clamp(heightDiff, 0f, 0.65f);
                agent.baseOffset = Mathf.Lerp(agent.baseOffset, targetOffset, Time.deltaTime * 10f);
                return;
            }

            agent.baseOffset = Mathf.Lerp(agent.baseOffset, 0f, Time.deltaTime * 5f);
        }

        private void TickMultiplayerOwnerSwitch()
        {
            float interval = PetSettings.PlayerSwitchInterval != null ? PetSettings.PlayerSwitchInterval.Value : 3f;

            if (interval <= 0f || Time.time < nextPlayerSwitchTime) return;

            nextPlayerSwitchTime = Time.time + interval * 60f;
            SelectNextOwner(false);
        }

        public void ManualSwitchOwner()
        {
            // A CURA DO F5 NO CLIENT:
            // Se quem apertou a tecla F5 for o Client, ele não tenta mudar sozinho.
            // Ele manda um pacote silencioso pela Steam ordenando que o Host faça a troca!
            if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            {
                RepoSteamNetwork.SendPacket(new PetSwitchOwnerPacket(), NetworkDestination.HostOnly);
                return;
            }

            // Se for o Host (ou Singleplayer), faz a troca da IA normalmente
            isCalledByOwner = false;
            SelectNextOwner(true);
        }

        private void SelectNextOwner(bool isManual)
        {
            List<PlayerAvatar> players = SemiFunc.PlayerGetList();
            if (players == null || players.Count <= 1) return;

            List<PlayerAvatar> unchosenPlayers = new List<PlayerAvatar>();
            List<PlayerAvatar> validPlayers = new List<PlayerAvatar>();

            foreach (PlayerAvatar player in players)
            {
                if (player != null && player.gameObject.activeInHierarchy && player != owner)
                {
                    validPlayers.Add(player);
                    if (!chosenOwnersThisLevel.Contains(player))
                    {
                        unchosenPlayers.Add(player);
                    }
                }
            }

            PlayerAvatar nextOwner = null;

            if (unchosenPlayers.Count > 0)
                nextOwner = unchosenPlayers[Random.Range(0, unchosenPlayers.Count)];
            else if (validPlayers.Count > 0)
            {
                chosenOwnersThisLevel.Clear();
                if (owner != null) chosenOwnersThisLevel.Add(owner);
                nextOwner = validPlayers[Random.Range(0, validPlayers.Count)];
            }

            if (nextOwner != null)
            {
                owner = nextOwner;
                chosenOwnersThisLevel.Add(nextOwner);
                ownerBreadcrumbs.Clear();
                deadEndMemories.Clear();
                if (aiAudio != null) aiAudio.PlayBark();
            }
        }

        private void TeleportToOwner()
        {
            if (owner == null) return;

            isJumping = false;
            if (agent != null) agent.enabled = false;

            Vector3 targetPos = owner.transform.position;
            System.Reflection.FieldInfo navField = HarmonyLib.AccessTools.Field(typeof(PlayerAvatar), "LastNavmeshPosition");

            if (navField != null)
            {
                Vector3 lastNav = (Vector3)navField.GetValue(owner);
                if (lastNav != Vector3.zero) targetPos = lastNav;
            }

            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                targetPos = hit.position;

            transform.position = targetPos;

            if (myRigidbody != null)
            {
                // Só zera a velocidade física ANTES de travar o corpo, impedindo o aviso na engine!
                if (!myRigidbody.isKinematic)
                {
                    myRigidbody.velocity = Vector3.zero;
                    myRigidbody.angularVelocity = Vector3.zero;
                }
                myRigidbody.isKinematic = true;
                myRigidbody.useGravity = false;
            }

            if (agent != null)
            {
                agent.baseOffset = 0f;
                agent.enabled = true;
                agent.Warp(targetPos);
            }

            Physics.SyncTransforms();
            ResolvePenetrations();
            stuckTimer = 0f;
            stuckHighTimer = 0f;
            offNavMeshStuckTimer = 0f;
            navMeshCooldown = 0f;
            escapeTimer = 0f;
            deadEndMemories.Clear();
            ownerBreadcrumbs.Clear();
            Plugin.Log.LogInfo("[Ai-Chan] Anti-Stuck activated. Teleported to safety.");
        }

        private void TickFollowOwner()
        {

            if (owner == null) { StopMoving(); isFollowingOwner = false; return; }

            if (TickManualMove())
                return;

            if (owner == null || !owner.gameObject.activeInHierarchy) { owner = FindNearestOwner(); }
            if (owner == null) { StopMoving(); isFollowingOwner = false; return; }

            if (awayTimer > 0f)
            {
                awayTimer -= Time.deltaTime;
                MoveTo(awayTargetPos, 0.5f);
                return;
            }

            if (ownerBreadcrumbs.Count == 0 || Vector3.Distance(ownerBreadcrumbs[ownerBreadcrumbs.Count - 1], owner.transform.position) > 1.0f)
            {
                float safeHeight = GetSafeRaycastHeight(owner.transform.position, 1.0f);
                if (FindSupportSurface(owner.transform.position + Vector3.up * safeHeight, safeHeight + 1f, out float ownerGroundY))
                {
                    Vector3 crumb = owner.transform.position;
                    crumb.y = ownerGroundY;
                    ownerBreadcrumbs.Add(crumb);
                }
                else
                {
                    ownerBreadcrumbs.Add(owner.transform.position);
                }

                if (ownerBreadcrumbs.Count > 50) ownerBreadcrumbs.RemoveAt(0);
            }

            float distance = Vector3.Distance(transform.position, owner.transform.position);

            if (agent != null && !agent.enabled && isFollowingOwner && distance > 4.0f)
            {
                offNavMeshStuckTimer += Time.deltaTime;
                if (offNavMeshStuckTimer >= 15.0f)
                {
                    TeleportToOwner();
                    return;
                }
            }
            else
            {
                offNavMeshStuckTimer = 0f;
            }

            Vector3 flatDist = owner.transform.position - transform.position;
            flatDist.y = 0;

            float petY = transform.position.y;
            float ownerY = owner.transform.position.y;

            bool isStuckAbove = (petY - ownerY >= 3.0f && flatDist.sqrMagnitude <= 25f);
            bool isInVoid = petY < -40f;

            if (isStuckAbove || isInVoid)
            {
                stuckHighTimer += Time.deltaTime;
                if (stuckHighTimer >= 15.0f || isInVoid)
                {
                    TeleportToOwner();
                    return;
                }
            }
            else
            {
                stuckHighTimer = 0f;
            }
            float yDiffToOwner = owner.transform.position.y - transform.position.y;
            bool isOwnerAbove = yDiffToOwner > 0.40f && yDiffToOwner < 4.0f;

            float configuredFollowDistance = isCalledByOwner ? 0f : (PetSettings.FollowDistance != null ? PetSettings.FollowDistance.Value : followDistance);
            // Para a 1.70m da base da mesa para não encostar na quina e abrir espaço para o arco do pulo
            float configuredStoppingDistance = isCalledByOwner ? 0.5f : (isOwnerAbove ? 1.70f : (PetSettings.FollowStoppingDistance != null ? PetSettings.FollowStoppingDistance.Value : followStoppingDistance));
            if (isCalledByOwner && distance <= 0.8f) isCalledByOwner = false;

            if (!isFollowingOwner && distance > configuredFollowDistance) isFollowingOwner = true;
            else if (isFollowingOwner && distance <= configuredStoppingDistance) { isFollowingOwner = false; StopMoving(); }

            if (isFollowingOwner)
            {
                Vector3 chaseTarget = owner.transform.position;
                float currentStoppingDistance = configuredStoppingDistance;

                if (agent != null && !agent.enabled)
                {
                    int obstacleMask = GetGroundMask();
                    bool canSeeOwner = HasLineOfSightSafe(transform.position + Vector3.up * 0.5f, owner.transform.position + Vector3.up * 0.5f, obstacleMask);

                    if (!canSeeOwner && ownerBreadcrumbs.Count > 0)
                    {
                        bool foundNode = false;
                        for (int i = ownerBreadcrumbs.Count - 1; i >= 0; i--)
                        {
                            if (Vector3.Distance(transform.position, ownerBreadcrumbs[i]) > 0.6f)
                            {
                                if (HasLineOfSightSafe(transform.position + Vector3.up * 0.5f, ownerBreadcrumbs[i] + Vector3.up * 0.5f, obstacleMask))
                                {
                                    chaseTarget = ownerBreadcrumbs[i];
                                    foundNode = true;
                                    break;
                                }
                            }
                        }

                        if (!foundNode)
                        {
                            float closestDist = float.MaxValue;
                            Vector3 closestNode = owner.transform.position;
                            for (int i = 0; i < ownerBreadcrumbs.Count; i++)
                            {
                                float d = Vector3.Distance(transform.position, ownerBreadcrumbs[i]);
                                if (d < closestDist && d > 0.5f)
                                {
                                    closestDist = d;
                                    closestNode = ownerBreadcrumbs[i];
                                    foundNode = true;
                                }
                            }
                            chaseTarget = foundNode ? closestNode : owner.transform.position;
                        }

                        if (chaseTarget != owner.transform.position)
                        {
                            currentStoppingDistance = 0.2f;
                        }
                    }
                }

                // --- LIMITAÇÃO DE MIGALHAS ---
                bool drawDebugRays = PetSettings.EnableDebugRays != null && PetSettings.EnableDebugRays.Value;
                if (drawDebugRays && ownerBreadcrumbs.Count > 0)
                {
                    float breadcrumbFadeTime = PetSettings.DebugBreadcrumbsFadeTime != null ? PetSettings.DebugBreadcrumbsFadeTime.Value : 0.15f;

                    // Limita visualização no máximo aos últimos 10 pinos
                    int maxPins = 10;
                    int startIndex = Mathf.Max(0, ownerBreadcrumbs.Count - maxPins);

                    for (int i = startIndex; i < ownerBreadcrumbs.Count - 1; i++)
                    {
                        PetRuntimeDrawer.DrawLine(ownerBreadcrumbs[i] + Vector3.up * 0.1f, ownerBreadcrumbs[i + 1] + Vector3.up * 0.1f, new Color(1f, 0.5f, 0f), breadcrumbFadeTime);
                        PetRuntimeDrawer.DrawLine(ownerBreadcrumbs[i], ownerBreadcrumbs[i] + Vector3.up * 0.4f, new Color(1f, 0.8f, 0f), breadcrumbFadeTime);
                    }

                    if (chaseTarget != owner.transform.position)
                    {
                        PetRuntimeDrawer.DrawLine(transform.position + Vector3.up * 0.5f, chaseTarget + Vector3.up * 0.5f, Color.magenta, breadcrumbFadeTime);
                    }
                }

                MoveTo(chaseTarget, currentStoppingDistance);
            }
            else StopMoving();
        }

        private float lastDebugDrawTime;

        private void TickAutoJump()
        {
            // A CURA DA MESA: Removemos a trava que proibia ela de pular com você nas costas!
            if (isJumping || Time.time < nextJumpTime) return;
            if (agent == null) return;

            // (O restante do código da função TickAutoJump continua normal abaixo...)
            posCheckTimer += Time.deltaTime;
            if (posCheckTimer >= 0.25f)
            {
                float distMoved = Vector3.Distance(transform.position, lastPosCheck);
                lastPosCheck = transform.position;
                posCheckTimer = 0f;
                isActuallyMoving = distMoved > 0.03f;
            }

             
            Vector3 checkDir = transform.forward;
            float horizontalDistToOwner = 0f;
            float yDiff = 0f;

            if (state == PetState.FollowOwner && owner != null)
            {
                Vector3 p1 = transform.position; p1.y = 0;
                Vector3 p2 = owner.transform.position; p2.y = 0;
                horizontalDistToOwner = Vector3.Distance(p1, p2);
                yDiff = owner.transform.position.y - transform.position.y;

                if (horizontalDistToOwner > 0.1f) checkDir = (p2 - p1).normalized;
            }
            else if (agent.enabled && agent.hasPath)
            {
                Vector3 p1 = transform.position; p1.y = 0;
                Vector3 p2 = agent.steeringTarget; p2.y = 0;
                if (Vector3.Distance(p1, p2) > 0.1f) checkDir = (p2 - p1).normalized;
            }

            bool drawDebugRays = PetSettings.EnableDebugRays != null && PetSettings.EnableDebugRays.Value;
            float debugFade = PetSettings.DebugRaysFadeTime != null ? PetSettings.DebugRaysFadeTime.Value : 0.25f;

            bool canDrawNow = drawDebugRays && Time.time > lastDebugDrawTime + 0.25f;
            if (canDrawNow) lastDebugDrawTime = Time.time;

            Vector3 checkOriginBottom = transform.position + Vector3.up * 0.25f;
            Vector3 checkOriginTop = transform.position + Vector3.up * 0.85f;

            bool wallInFront = CheckPathClear(checkOriginBottom, checkOriginTop, 0.25f, checkDir, 1.2f, GetGroundMask(), out _);

            if (canDrawNow) PetRuntimeDrawer.DrawLine(transform.position + Vector3.up * 0.5f, transform.position + Vector3.up * 0.5f + checkDir * 1.2f, wallInFront ? Color.red : Color.blue, debugFade);



            bool validLedgeTarget = false;
            float maxJumpHeight = PetSettings.MaxJumpObstacleHeight != null ? PetSettings.MaxJumpObstacleHeight.Value : 3.5f;

            // A CURA DO LASER VERMELHO (Trava de Parede):
            // Só consideramos o alvo elevado se a distância horizontal for maior que um mínimo (ex: 0.8m)
            // Se você (Dono) subiu em algo muito perto, ela não tenta pular colada na parede infinitamente, 
            // ela vai caminhar pela NavMesh primeiro até pegar espaço!
            bool targetIsElevated = (yDiff > 0.40f && yDiff < 4.0f && horizontalDistToOwner < 5.0f && horizontalDistToOwner > 0.8f);

            // Varre a mesa a uma distância de até 2.2m para travar a posição antes de encostar no colisor
            if (wallInFront || targetIsElevated)
            {
                float[] checkDistances = targetIsElevated ? new float[] { 0.8f, 1.4f, 2.2f } : new float[] { 1.2f };
                for (int i = 0; i < checkDistances.Length; i++)
                {
                    Vector3 probePos = transform.position + checkDir * checkDistances[i];
                    float ledgeSafeHeight = GetSafeRaycastHeight(probePos, maxJumpHeight);
                    Vector3 ledgeCheckOrigin = probePos + Vector3.up * ledgeSafeHeight;

                    if (FindSupportSurface(ledgeCheckOrigin, ledgeSafeHeight + 1.5f, out float ledgeY))
                    {
                        float heightAbove = ledgeY - transform.position.y;
                        float minHeight = PetSettings.MinJumpObstacleHeight != null ? PetSettings.MinJumpObstacleHeight.Value : 0.75f;

                        if (heightAbove >= minHeight && heightAbove <= maxJumpHeight)
                        {
                            validLedgeTarget = true;
                            if (canDrawNow) PetRuntimeDrawer.DrawLine(ledgeCheckOrigin, ledgeCheckOrigin + Vector3.down * (ledgeSafeHeight + 1.5f), Color.magenta, debugFade);
                            break;
                        }
                    }
                }
            }

            // Se o dono estiver no alto e a pet estiver dentro de 2.2m, prepara o salto imediatamente
            bool inJumpZone = targetIsElevated && horizontalDistToOwner <= 2.2f;
            bool stuckAtEdge = !isActuallyMoving && stuckTimer > 0.25f && targetIsElevated;
            isPreparingToJump = validLedgeTarget || inJumpZone || stuckAtEdge;
            if (isPreparingToJump)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
                jumpTimer += Time.deltaTime;

                // Projeta a rotação estritamente no plano horizontal para não gerar quaternion inválido
                Vector3 flatLookDir = checkDir;
                flatLookDir.y = 0f;
                if (flatLookDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(flatLookDir.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 500f * Time.deltaTime);
                }
            }
            else
            {
                jumpTimer = 0f;
                if (isActuallyMoving) stuckTimer = 0f;
                else if (agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath && agent.remainingDistance > 0.5f) stuckTimer += Time.deltaTime;
                else stuckTimer = Mathf.Max(0f, stuckTimer - Time.deltaTime);
            }

              
            float jumpDelayLimit = targetIsElevated ? 0.35f : (PetSettings.AutoJumpDelay != null ? PetSettings.AutoJumpDelay.Value : 0.8f);

            if (jumpTimer > jumpDelayLimit)
            {
                jumpTimer = 0f;

                Vector3 bestJumpTarget = Vector3.zero;
                bool foundSafeLanding = false;

                float maxDist = Mathf.Min(6.0f, horizontalDistToOwner + 1.5f);
                if (maxDist < 2.0f) maxDist = 2.0f;

                // Para alvos altos, exige que o ponto de aterrissagem esteja a pelo menos 0.9m de distância horizontal (evita pular reto para cima)
                float startTestDist = targetIsElevated ? 0.9f : 0.5f;

                // OTIMIZAÇÃO DE CPU: Busca a lista de players UMA única vez antes do loop
                List<PlayerAvatar> allPlayers = SemiFunc.PlayerGetAll();
                float safeRadiusSqr = 0.85f * 0.85f; // Pré-calcula o raio ao quadrado para evitar o uso de raiz quadrada na engine

                for (float d = startTestDist; d <= maxDist; d += 0.4f)
                {
                    Vector3 testPos = transform.position + checkDir * d;
                    float rayHeight = Mathf.Max(yDiff + 1.5f, maxJumpHeight);
                    Vector3 rayOrigin = new Vector3(testPos.x, transform.position.y + rayHeight, testPos.z);

                    if (FindSupportSurface(rayOrigin, rayHeight + 1.5f, out float surfaceY))
                    {
                        float heightAtTest = surfaceY - transform.position.y;
                        float minHeight = PetSettings.MinJumpObstacleHeight != null ? PetSettings.MinJumpObstacleHeight.Value : 0.75f;

                        if (heightAtTest >= minHeight && heightAtTest <= maxJumpHeight)
                        {
                            Vector3 landingPos = new Vector3(testPos.x, surfaceY, testPos.z);

                            // PROTEÇÃO 1 (Ultraleve): Matemática direta nos eixos sem alocação de Vector3 novos
                            bool landingOnPlayer = false;
                            if (allPlayers != null)
                            {
                                foreach (PlayerAvatar p in allPlayers)
                                {
                                    if (p == null || !p.gameObject.activeInHierarchy) continue;

                                    float dx = landingPos.x - p.transform.position.x;
                                    float dz = landingPos.z - p.transform.position.z;
                                    float horizontalDistSqr = (dx * dx) + (dz * dz); // Pitágoras bruto, zero custo de CPU

                                    float verticalDist = landingPos.y - p.transform.position.y;

                                    if (horizontalDistSqr < safeRadiusSqr && verticalDist > 0.1f && verticalDist < 2.5f)
                                    {
                                        landingOnPlayer = true;
                                        break;
                                    }
                                }
                            }
                            if (landingOnPlayer) continue;

                            bool flatGround = false;
                            int groundHits = Physics.RaycastNonAlloc(landingPos + Vector3.up * 0.5f, Vector3.down, surfaceHitsBuffer, 1.0f, GetGroundMask(), QueryTriggerInteraction.Ignore);
                            for (int h = 0; h < groundHits; h++)
                            {
                                if (!IsColliderPlayerOrItem(surfaceHitsBuffer[h].collider))
                                {
                                    if (surfaceHitsBuffer[h].normal.y > 0.7f) flatGround = true;
                                    break;
                                }
                            }
                            if (!flatGround) continue;

                            Vector3 p1C = landingPos + Vector3.up * 0.35f;
                            Vector3 p2C = landingPos + Vector3.up * 1.3f;

                            bool landingClear = true;
                            int cols = Physics.OverlapCapsuleNonAlloc(p1C, p2C, 0.25f, overlapCollidersBuffer, GetGroundMask(), QueryTriggerInteraction.Ignore);
                            for (int c = 0; c < cols; c++)
                            {
                                if (!IsColliderPlayerOrItem(overlapCollidersBuffer[c]))
                                {
                                    landingClear = false;
                                    break;
                                }
                            }

                            Vector3 arcPeak = (transform.position + landingPos) * 0.5f + Vector3.up * (heightAtTest + 0.5f);
                            bool pathBlocked = false;

                            Vector3 startPath = transform.position + Vector3.up * 0.6f;
                            int pathHits = Physics.RaycastNonAlloc(startPath, (arcPeak - startPath).normalized, surfaceHitsBuffer, Vector3.Distance(startPath, arcPeak), GetGroundMask(), QueryTriggerInteraction.Ignore);
                            for (int h = 0; h < pathHits; h++)
                            {
                                if (!IsColliderPlayerOrItem(surfaceHitsBuffer[h].collider)) { pathBlocked = true; break; }
                            }

                            if (!pathBlocked)
                            {
                                Vector3 endPath = landingPos + Vector3.up * 0.5f;
                                pathHits = Physics.RaycastNonAlloc(arcPeak, (endPath - arcPeak).normalized, surfaceHitsBuffer, Vector3.Distance(arcPeak, endPath), GetGroundMask(), QueryTriggerInteraction.Ignore);
                                for (int h = 0; h < pathHits; h++)
                                {
                                    if (!IsColliderPlayerOrItem(surfaceHitsBuffer[h].collider)) { pathBlocked = true; break; }
                                }
                            }

                            if (landingClear && !pathBlocked)
                            {
                                bestJumpTarget = landingPos;
                                foundSafeLanding = true;
                                if (canDrawNow) PetRuntimeDrawer.DrawLine(bestJumpTarget, bestJumpTarget + Vector3.up * 1.5f, Color.green, 2.0f);
                                break;
                            }
                        }
                    }
                }

                if (foundSafeLanding)
                {
                    StartCoroutine(PerformAutoJump(bestJumpTarget));
                }
                else
                {
                    // Se não encontrou superfície viável, pausa tentativas por 1.5s para não travar
                    nextJumpTime = Time.time + 1.5f;
                }
            }
        }

        private IEnumerator PerformAutoJump(Vector3 targetPos)
        {
            isJumping = true;

              
            if (agent != null && agent.enabled)
            {
                if (agent.isOnNavMesh) agent.isStopped = true;
                agent.enabled = false;
            }

            Vector3 startPos = transform.position;
            float heightDiff = targetPos.y - startPos.y;
            float horizontalDist = Vector2.Distance(new Vector2(startPos.x, startPos.z), new Vector2(targetPos.x, targetPos.z));

            float duration = Mathf.Clamp(horizontalDist * 0.15f + Mathf.Abs(heightDiff) * 0.18f, 0.35f, 0.8f);
            float elapsed = 0f;

            if (aiAudio != null && Time.time >= nextJumpBarkTime)
            {
                aiAudio.PlayBark();
                nextJumpBarkTime = Time.time + 10.0f;
            }

            // LIMITADOR DE TETO: Garante que ela não salte mais alto que o teto da sala e seja abduzida
            float arcBoost = Mathf.Max(heightDiff + 0.2f, horizontalDist * 0.2f + 0.3f);
            float ceilingClearance = GetSafeRaycastHeight(startPos, 3.5f);
            float maxAllowedArc = Mathf.Max(0f, ceilingClearance - 1.2f); // Guarda 1.2m para a cabeça e corpo
            arcBoost = Mathf.Min(arcBoost, maxAllowedArc);

            try
            {
                // DESENHA O ARCO DO PULO (DEBUG RAY)
                if (PetSettings.EnableDebugRays != null && PetSettings.EnableDebugRays.Value)
                {
                    int segments = 15;
                    Vector3 lastDebugPoint = startPos;

                    for (int i = 1; i <= segments; i++)
                    {
                        float p = i / (float)segments; // Progresso falso (de 0 a 1)

                        // As mesmas fórmulas matemáticas da sua Ai-Chan
                        float hProg = Mathf.Sin(p * Mathf.PI * 0.5f);
                        float vArc = Mathf.Sin(p * Mathf.PI) * arcBoost;

                        Vector3 cFlat = Vector3.Lerp(startPos, targetPos, hProg);
                        float cY = Mathf.Lerp(startPos.y, targetPos.y, p) + vArc;
                        Vector3 nextDebugPoint = new Vector3(cFlat.x, cY, cFlat.z);

                        // Usa o seu próprio renderizador de linhas para desenhar o arco em amarelo vivo
                        PetRuntimeDrawer.DrawLine(lastDebugPoint, nextDebugPoint, Color.yellow, 2.5f);
                        lastDebugPoint = nextDebugPoint;
                    }
                }
                while (elapsed < duration)
                {
                    if (state == PetState.Grabbed) yield break;

                    elapsed += Time.deltaTime;
                    float progress = elapsed / duration;

                    float horizontalProgress = Mathf.Sin(progress * Mathf.PI * 0.5f);
                    float verticalArc = Mathf.Sin(progress * Mathf.PI) * arcBoost;

                    Vector3 currentFlatPos = Vector3.Lerp(startPos, targetPos, horizontalProgress);
                    float currentY = Mathf.Lerp(startPos.y, targetPos.y, progress) + verticalArc;
                    Vector3 nextPos = new Vector3(currentFlatPos.x, currentY, currentFlatPos.z);

                    // ANTI-CLIP DE TETO E PAREDE FORTE: Usa cápsula do corpo inteiro dela
                    if (progress > 0.05f && progress < 0.95f)
                    {
                        Vector3 dir = (nextPos - transform.position);
                        float moveDist = dir.magnitude;
                        if (moveDist > 0.001f)
                        {
                            Vector3 p1C = transform.position + Vector3.up * 0.35f;
                            Vector3 p2C = transform.position + Vector3.up * 0.85f;

                            if (Physics.CapsuleCast(p1C, p2C, 0.25f, dir.normalized, out RaycastHit hit, moveDist, GetGroundMask(), QueryTriggerInteraction.Ignore))
                            {
                                // Se bateu num teto (normal apontando pra baixo)
                                if (hit.normal.y < -0.2f)
                                {
                                    targetPos.y = transform.position.y; // Aborta o destino da subida!
                                    nextPos.y = transform.position.y;
                                }
                                else
                                {
                                    // Se bateu na parede, altera o DESTINO FINAL para a parede
                                    // Assim o ímã do Lerp para de tentar puxar ela pra dentro do concreto!
                                    targetPos.x = hit.point.x + hit.normal.x * 0.25f;
                                    targetPos.z = hit.point.z + hit.normal.z * 0.25f;

                                    nextPos.x = targetPos.x;
                                    nextPos.z = targetPos.z;
                                }
                            }
                        }
                    }

                    transform.position = nextPos;
                    yield return null;
                }
            }
              
            finally
            {
                isJumping = false;

                if (state != PetState.Grabbed)
                {
                    transform.position = targetPos;
                    ResolvePenetrations();
                    EnsureGroundedAndNavMesh();

                    // Se a altura final for muito menor que o alvo planejado (falhou o pulo e caiu de volta no chão),
                    // aplica um cooldown de 2.5s para evitar que ela fique pulando em loop infinito.
                    if (targetPos.y - transform.position.y > 0.40f)
                    {
                        nextJumpTime = Time.time + 2.5f;
                        jumpTimer = 0f;
                        isPreparingToJump = false;
                    }
                    else
                    {
                        nextJumpTime = Time.time + 1.2f;
                    }
                }
                else
                {
                    nextJumpTime = Time.time + 1.0f;
                }
            }
        }

        public void EnsureGroundedAndNavMesh()
        {
            if (state == PetState.Grabbed) return;
            if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;

            // 1. Tenta encontrar uma superfície física real sob os pés (aumentada busca vertical para 5m)
            bool foundSurface = PlaceOnSupportSurface(out float surfaceY);
            bool hasNavMesh = NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4.0f, NavMesh.AllAreas);

            // 2. MESA / OBJETO ELEVADO: Só assume se ENCONTROU uma superfície física real E ela estiver acima da NavMesh
            if (foundSurface && hasNavMesh && (surfaceY - hit.position.y) > 0.35f)
            {
                if (agent != null && agent.enabled)
                {
                    if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
                    agent.enabled = false;
                }

                Vector3 topPos = transform.position;
                topPos.y = surfaceY;
                transform.position = topPos;

                if (myRigidbody != null)
                {
                    myRigidbody.isKinematic = true;
                    myRigidbody.useGravity = false;
                    myRigidbody.position = topPos;
                }

                Physics.SyncTransforms();
                ResolvePenetrations();
                return;
            }

            // 3. CHÃO NORMAL: Se está no ar ou no chão mapeado, reconecta direto à NavMesh no nível do solo
            if (hasNavMesh)
            {
                Vector3 targetFloorPos = hit.position;
                if (foundSurface && Mathf.Abs(surfaceY - hit.position.y) <= 0.35f)
                {
                    targetFloorPos.y = surfaceY;
                }

                transform.position = targetFloorPos;

                if (myRigidbody != null)
                {
                    myRigidbody.isKinematic = true;
                    myRigidbody.useGravity = false;
                    myRigidbody.position = targetFloorPos;
                }

                Physics.SyncTransforms();

                if (agent != null)
                {
                    agent.enabled = true;
                    agent.Warp(targetFloorPos);
                    agent.nextPosition = targetFloorPos;
                    agent.isStopped = false;
                }

                lastDestination = Vector3.positiveInfinity;
                lastSetDestinationTime = 0f;
                panicDirection = Vector3.zero;
                smoothedManualDir = Vector3.zero;
                navMeshCooldown = 0f;

                ResolvePenetrations();
                return;
            }

            // 4. Fora da NavMesh e em piso não mapeado
            if (foundSurface)
            {
                Vector3 pos = transform.position;
                pos.y = surfaceY;
                transform.position = pos;

                if (myRigidbody != null)
                {
                    myRigidbody.isKinematic = true;
                    myRigidbody.useGravity = false;
                    myRigidbody.position = pos;
                }

                Physics.SyncTransforms();
                ResolvePenetrations();
            }
        }

        public bool PlaceOnSupportSurface(out float surfaceY)
        {
            surfaceY = transform.position.y;

            if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return false;

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule == null) return false;

            float safeHeight = GetSafeRaycastHeight(transform.position, 1.0f);
            Vector3 origin = transform.position + Vector3.up * safeHeight;

            // Alcance vertical aumentado para 5.0m para garantir que encontra mesas/chão mesmo se ela estiver caindo
            if (FindSupportSurface(origin, safeHeight + 5.0f, out float foundY))
            {
                float capsuleBottom = capsule.center.y - capsule.height * 0.5f;
                surfaceY = foundY;
                Vector3 newPosition = transform.position;
                newPosition.y = foundY - capsuleBottom;

                if (myRigidbody != null)
                {
                    if (!myRigidbody.isKinematic)
                    {
                        myRigidbody.velocity = Vector3.zero;
                        myRigidbody.angularVelocity = Vector3.zero;
                    }
                    myRigidbody.isKinematic = true;
                    myRigidbody.useGravity = false;
                    myRigidbody.position = newPosition;
                }
                else
                {
                    transform.position = newPosition;
                }

                transform.position = newPosition;
                Physics.SyncTransforms();
                ResolvePenetrations();
                return true;
            }

            return false;
        }

        public bool SnapToNavMesh()
        {
            return SnapToNavMesh(out _);
        }

        public bool SnapToNavMesh(out NavMeshHit hit)
        {
            hit = default;
            if (state == PetState.Grabbed) return false;
            if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return false;
            if (agent == null) return false;

            if (!NavMesh.SamplePosition(transform.position, out hit, 1.5f, NavMesh.AllAreas))
            {
                return false;
            }

            float safeHeight = GetSafeRaycastHeight(transform.position, 1.5f);
            if (FindSupportSurface(transform.position + Vector3.up * safeHeight, safeHeight + 1.0f, out float surfaceY))
            {
                float elevationOffset = Mathf.Max(0f, surfaceY - hit.position.y);
                agent.baseOffset = elevationOffset;
            }

            if (!agent.enabled)
                agent.enabled = true;

            // O controller move o Transform manualmente em Update().
            // Não reative o controle de Transform do NavMeshAgent depois do stun/explosão!
            agent.updatePosition = false;
            agent.updateRotation = false;

            bool warped = agent.Warp(hit.position);

            if (warped)
            {
                transform.position = hit.position;
                agent.nextPosition = hit.position;
                agent.isStopped = false;

                Physics.SyncTransforms();
                ResolvePenetrations();
            }

            return warped || agent.isOnNavMesh;
        }

        private PlayerAvatar FindNearestOwner()
        {
            return SemiFunc.PlayerGetNearestPlayerAvatarWithinRange(
                999f,
                transform.position,
                true,
                LayerMask.GetMask("Default"));
        }

        private void MoveTo(Vector3 point, float stoppingDistance)
        {
            if (agent == null || isJumping || isPreparingToJump) return;

            TryOpenNearbyDoors();

            bool isCarrying = state == PetState.CarryItemToCart;

            float currentScale = transform.localScale.y;
            float scaledRadius = 0.15f * currentScale;
            float stepOffset = 0.55f * currentScale;
            float headHeight = (isCarrying ? 0.85f : 1.25f) * currentScale;
            int obstacleMask = GetGroundMask();

            Vector3 flatPet = transform.position;
            flatPet.y = 0f;
            Vector3 flatTarget = point;
            flatTarget.y = 0f;
            float distToTarget = Vector3.Distance(flatPet, flatTarget);
            float yDiff = transform.position.y - point.y;

            float actualStoppingDistance = Mathf.Abs(yDiff) > 0.6f ? 0.1f : Mathf.Max(0.05f, stoppingDistance);

            float distMoved = Vector3.Distance(transform.position, lastStuckCheckPos);
            bool isActuallyMoving = (agent.enabled && agent.isOnNavMesh) ? agent.velocity.sqrMagnitude > 0.05f : (distMoved > 0.02f);

            if (isActuallyMoving) stuckTimer = Mathf.Max(0f, stuckTimer - Time.deltaTime * 2f);
            else if (distToTarget <= actualStoppingDistance) stuckTimer = 0f;

            if (panicTimer > 0f)
            {
                panicTimer -= Time.deltaTime;
                if (panicTimer <= 0f) panicDirection = Vector3.zero; // Reseta ao terminar pânico
            }

            for (int i = deadEndMemories.Count - 1; i >= 0; i--)
            {
                deadEndMemories[i].Timer -= Time.deltaTime;
                if (deadEndMemories[i].Timer <= 0f) deadEndMemories.RemoveAt(i);
            }

            if (navMeshCooldown > 0f) navMeshCooldown -= Time.deltaTime;
            if (escapeTimer > 0f) escapeTimer -= Time.deltaTime;

            bool isTargetOnNavMesh = false;
            Vector3 navDestination = point;
            bool targetElevated = (point.y - transform.position.y) > 0.35f;

            // 1. Busca a NavMesh num raio amplo (até 8m) para encontrar chão navegável perto de prateleiras/loja
            if (NavMesh.SamplePosition(point, out NavMeshHit directHit, targetElevated ? 8.0f : 4.0f, NavMesh.AllAreas))
            {
                isTargetOnNavMesh = true;
                navDestination = directHit.position;
            }

            // 2. Fallback de proteção contra Infinity/NaN
            if (float.IsInfinity(navDestination.x) || float.IsNaN(navDestination.x) ||
                float.IsInfinity(navDestination.y) || float.IsNaN(navDestination.y) ||
                float.IsInfinity(navDestination.z) || float.IsNaN(navDestination.z))
            {
                navDestination = transform.position;
            }

            // 1. NAVEGAÇÃO VIA NAVMESH 
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.autoBraking = false; // Fim do freio nas costuras do mapa

                // Parada suave respeitando o código
                agent.stoppingDistance = actualStoppingDistance;

                bool isShop = PetSpawner.IsShopContext();
                bool shouldSwitchToManual = false;
                string switchReason = "";
                RaycastHit hitObstacle = default;
                bool physicalObstacleInFront = false;

                // O SENSOR DE MESA (WHISKERS) SÓ É AVALIADO E USADO NA LOJA!
                // Níveis normais sempre permanecem na NavMesh, nunca dropando pro manual por conta de física
                if (isShop)
                {
                    Vector3 moveDir = agent.velocity.sqrMagnitude > 0.05f ? agent.velocity.normalized : transform.forward;
                    Vector3 sphereOrigin = transform.position + Vector3.up * (0.45f * currentScale);

                    float probeDistance = 2.40f * currentScale;
                    float sphereRadius = 0.32f * currentScale;

                    // O SENSOR DE MESA ABSOLUTO: 
                    // Qualquer coisa sólida detectada pela esfera que NÃO for a própria Ai-Chan nem um jogador, é parede.
                    if (Physics.SphereCast(sphereOrigin, sphereRadius, moveDir, out hitObstacle, probeDistance, obstacleMask, QueryTriggerInteraction.Ignore))
                    {
                        if (hitObstacle.collider != null && !hitObstacle.collider.transform.IsChildOf(transform) &&
                            hitObstacle.collider.gameObject.layer != LayerMask.NameToLayer("Player") &&
                            hitObstacle.collider.gameObject.layer != LayerMask.NameToLayer("PlayerOnlyCollision"))
                        {
                            // A CURA DA FENDA: Se for muito rasteiro (tipo um tapete/chão mal modelado), ela ignora para não subir à toa
                            float hitHeightRelative = hitObstacle.point.y - transform.position.y;
                            if (hitHeightRelative > 0.15f * currentScale)
                            {
                                physicalObstacleInFront = true;
                            }
                        }
                    }

                    bool isPathEnded = agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathPartial && agent.remainingDistance < 0.8f;
                    bool isStuckLooking = agent.velocity.sqrMagnitude < 0.05f && distToTarget > actualStoppingDistance + 0.5f;

                    if (physicalObstacleInFront && !isPreparingToJump && !targetElevated)
                    {
                        shouldSwitchToManual = true;
                        switchReason = "Obstáculo físico (mesa/bancada) detectado à frente";
                    }
                    else if (!targetElevated && !isTargetOnNavMesh)
                    {
                        shouldSwitchToManual = true;
                        switchReason = "Alvo fora da NavMesh";
                    }
                    else if (!targetElevated && isPathEnded)
                    {
                        shouldSwitchToManual = true;
                        switchReason = "Fim da NavMesh (transição para piso não mapeado)";
                    }
                    else if (isStuckLooking && stuckTimer > 0.5f && !isPreparingToJump)
                    {
                        shouldSwitchToManual = true;
                        switchReason = "Travamento físico por 0.5s";
                    }
                }

                if (shouldSwitchToManual && navMeshCooldown <= 0f)
                {
                    LogNavMeshTransition(false, switchReason);
                    if (agent.isOnNavMesh)
                    {
                        if (agent.hasPath) agent.ResetPath();
                        agent.isStopped = true;
                    }
                    agent.enabled = false;
                    navMeshCooldown = 0.35f;

                    if (physicalObstacleInFront && hitObstacle.normal.sqrMagnitude > 0.1f)
                    {
                        Vector3 tangent = Vector3.Cross(hitObstacle.normal, Vector3.up).normalized;
                        Vector3 alternativeDir = Vector3.Dot(tangent, transform.right) > 0f ? tangent : -tangent;
                        smoothedManualDir = (transform.forward + alternativeDir * 1.5f).normalized;
                    }
                    else
                    {
                        smoothedManualDir = transform.forward;
                    }
                }
                else
                {
                    // Comportamento blindado da NavMesh (Ativo 100% do tempo em Níveis Normais)
                    bool destinationMovedEnough = float.IsInfinity(lastDestination.x) || Vector3.SqrMagnitude(navDestination - lastDestination) > 0.25f;
                    bool needsPath = !agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid;

                    if ((destinationMovedEnough || needsPath) && Time.time >= lastSetDestinationTime + 0.15f)
                    {
                        if (agent.SetDestination(navDestination))
                        {
                            lastDestination = navDestination;
                            lastSetDestinationTime = Time.time;

                            
                        }
                    }

                    // --- DESENHA A ROTA DO NAVMESH (O GPS) ---
                    // Deixando ele aqui fora do "if", o jogo vai desenhar o GPS em todos os frames 
                    // em que ela estiver andando, criando uma linha constante e perfeita!
                    bool drawDebugRays = PetSettings.EnableDebugRays != null && PetSettings.EnableDebugRays.Value;
                    if (drawDebugRays && agent.hasPath)
                    {
                        Vector3[] corners = agent.path.corners;
                        for (int c = 0; c < corners.Length - 1; c++)
                        {
                            Vector3 p1 = corners[c] + Vector3.up * 0.2f;
                            Vector3 p2 = corners[c + 1] + Vector3.up * 0.2f;

                            PetRuntimeDrawer.DrawLine(p1, p2, Color.cyan, 0.05f); // Tempo curto (0.05) pois atualiza todo frame
                            PetRuntimeDrawer.DrawLine(p2, p2 + Vector3.up * 0.5f, Color.white, 0.05f);
                        }
                    }

                    return;
                }
            }

            // 2. MODO MANUAL (Ativo na Loja ou fora da malha em níveis normais)
            if (!agent.enabled)
            {
                if (navMeshCooldown <= 0f && !isJumping && panicTimer <= 0f && escapeTimer <= 0f)
                {
                    if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 0.6f, NavMesh.AllAreas))
                    {
                        if (PlaceOnSupportSurface(out float currentSurfaceY))
                        {
                            if (Mathf.Abs(navHit.position.y - currentSurfaceY) <= 0.35f)
                            {
                                agent.enabled = true;
                                agent.Warp(navHit.position);
                                agent.isStopped = false;
                                lastDestination = Vector3.positiveInfinity;
                                lastSetDestinationTime = 0f;
                                panicDirection = Vector3.zero;
                                smoothedManualDir = Vector3.zero;
                                return;
                            }
                        }
                    }
                }

                // --- ADICIONE ESTE NOVO BLOCO DE RESGATE AQUI ---
                // RESGATE IMEDIATO DA MESA DURANTE A ENTREGA
                // Ela não fica patrulhando a borda. Ela escaneia o chão limpo mais próximo e desce.
                if (state == PetState.CarryItemToCart && navMeshCooldown <= 0f && !isJumping)
                {
                    Vector3 toTarget = (point - transform.position);
                    toTarget.y = 0f;
                    Vector3 searchDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;

                    // Escaneia 8 direções, priorizando a frente do carrinho
                    float[] angles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
                    bool foundRescue = false;
                    Vector3 safeWarpPos = Vector3.zero;

                    foreach (float angle in angles)
                    {
                        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * searchDir;

                        // Testa a partir da borda da mesa para fora (0.5m até 2.5m)
                        for (float dist = 0.5f; dist <= 2.5f; dist += 0.5f)
                        {
                            Vector3 rayOrigin = transform.position + dir * dist + Vector3.up * 0.5f;

                            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hitFloor, 6.0f, GetGroundMask(), QueryTriggerInteraction.Ignore))
                            {
                                // Garante que é um abismo/degrau para baixo
                                if (transform.position.y - hitFloor.point.y > 0.35f)
                                {
                                    // Valida se o chão encontrado tem NavMesh
                                    if (NavMesh.SamplePosition(hitFloor.point, out NavMeshHit navHit, 0.5f, NavMesh.AllAreas))
                                    {
                                        // Valida Headroom: Garante que ela não vai spawnar debaixo da mesa ou dentro de parede
                                        Vector3 p1 = navHit.position + Vector3.up * 0.35f;
                                        Vector3 p2 = navHit.position + Vector3.up * 1.30f;

                                        if (!Physics.CheckCapsule(p1, p2, 0.25f, GetGroundMask(), QueryTriggerInteraction.Ignore))
                                        {
                                            safeWarpPos = navHit.position;
                                            foundRescue = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        if (foundRescue) break;
                    }

                    // Se achou um chão limpo e mapeado, desce instantaneamente e retoma a viagem
                    if (foundRescue)
                    {
                        agent.enabled = true;
                        agent.Warp(safeWarpPos);
                        agent.nextPosition = safeWarpPos;
                        agent.isStopped = false;
                        lastDestination = Vector3.positiveInfinity;
                        navMeshCooldown = 0.5f;
                        return;
                    }
                }
                // --- FIM DO NOVO BLOCO ---

                Vector3 bottom = transform.position + Vector3.up * (stepOffset + scaledRadius);
                Vector3 top = transform.position + Vector3.up * Mathf.Max(headHeight, stepOffset + scaledRadius + 0.1f);

                if (distToTarget > actualStoppingDistance || escapeTimer > 0f || panicTimer > 0f)
                {
                    Vector3 steeringTarget = point;
                    bool seekingNavMeshEntrance = false;

                    if (Time.time > nextNavMeshCheckTime)
                    {
                        nextNavMeshCheckTime = Time.time + 0.5f;
                        if (NavMesh.SamplePosition(transform.position, out NavMeshHit entranceHit, 10.0f, NavMesh.AllAreas))
                        {
                            NavMeshPath testPath = new NavMeshPath();
                            if (NavMesh.CalculatePath(entranceHit.position, point, NavMesh.AllAreas, testPath) && testPath.status != NavMeshPathStatus.PathInvalid)
                            {
                                int corners = testPath.GetCornersNonAlloc(navMeshCorners);
                                steeringTarget = entranceHit.position;

                                for (int c = 1; c < corners; c++)
                                {
                                    Vector3 cornerFlat = new Vector3(navMeshCorners[c].x, 0f, navMeshCorners[c].z);
                                    if (Vector3.Distance(flatPet, cornerFlat) > 0.75f)
                                    {
                                        steeringTarget = navMeshCorners[c];
                                        break;
                                    }
                                }
                                seekingNavMeshEntrance = true;
                            }
                        }
                    }

                    Vector3 flatSteeringTarget = steeringTarget;
                    flatSteeringTarget.y = 0f;

                    Vector3 desiredDir = panicTimer > 0f ? panicDirection : (flatSteeringTarget - flatPet).normalized;
                    if (desiredDir.sqrMagnitude < 0.001f) desiredDir = transform.forward;

                    if (cachedBestDir.sqrMagnitude < 0.001f) cachedBestDir = desiredDir;

                    float targetSpeed = (state == PetState.CarryItemToCart)
                        ? (PetSettings.CarrySpeed != null ? PetSettings.CarrySpeed.Value : 4.0f)
                        : (PetSettings.Speed != null ? PetSettings.Speed.Value : 3.5f);

                    float moveDist = targetSpeed * Time.deltaTime;
                    bool drawDebugRays = PetSettings.EnableDebugRays != null && PetSettings.EnableDebugRays.Value;
                    float debugFade = PetSettings.DebugRaysFadeTime != null ? PetSettings.DebugRaysFadeTime.Value : 0.25f;

                    stuckPositionCheckTimer += Time.deltaTime;
                    if (stuckPositionCheckTimer >= 0.75f)
                    {
                        stuckPositionCheckTimer = 0f;
                        lastStuckCheckPos = transform.position;

                        if (distToTarget > actualStoppingDistance && !isActuallyMoving)
                        {
                            stuckTimer += 0.75f;

                            // A CURA DO CONGELAMENTO NA MESA: Se ela ficou parada na borda da mesa por 1 segundo e meio,
                            // isso significa que o abismo a impede de andar. Ela salta pra NavMesh logo abaixo dela!
                            if (stuckTimer > 1.5f && state == PetState.CarryItemToCart)
                            {
                                if (NavMesh.SamplePosition(transform.position, out NavMeshHit rescueHit, 4.0f, NavMesh.AllAreas))
                                {
                                    agent.enabled = true;
                                    agent.Warp(rescueHit.position);
                                    agent.isStopped = false;
                                    stuckTimer = 0f;
                                    return;
                                }
                            }

                            bool hasPinNearby = false;
                            for (int p = 0; p < deadEndMemories.Count; p++)
                            {
                                if (Vector3.Distance(deadEndMemories[p].Position, transform.position) < 0.8f)
                                {
                                    deadEndMemories[p].Timer = 8.0f;
                                    hasPinNearby = true;
                                    break;
                                }
                            }

                            if (!hasPinNearby && Time.time >= lastPinDropTime + 0.5f)
                            {
                                lastPinDropTime = Time.time;
                                deadEndMemories.Add(new DeadEndMemory { Position = transform.position, Timer = 8.0f });
                            }
                        }
                    }

                    bool isStuckInMaze = false;
                    for (int i = 0; i < deadEndMemories.Count; i++)
                    {
                        if (Vector3.Distance(transform.position, deadEndMemories[i].Position) < 3.0f)
                        {
                            isStuckInMaze = true;
                            break;
                        }
                    }

                    if (drawDebugRays)
                    {
                        for (int p = 0; p < deadEndMemories.Count; p++)
                            PetRuntimeDrawer.DrawLine(deadEndMemories[p].Position, deadEndMemories[p].Position + Vector3.up * 2.0f, Color.red, debugFade);

                        if (panicTimer > 0f) PetRuntimeDrawer.DrawLine(transform.position + Vector3.up * stepOffset, transform.position + Vector3.up * stepOffset + panicDirection * 2.0f, Color.yellow, debugFade);
                    }

                    float probeInterval = PetSettings.GhostProbeUpdateInterval != null ? PetSettings.GhostProbeUpdateInterval.Value : 0.1f;

                    if (Time.time >= nextProbeTime)
                    {
                        nextProbeTime = Time.time + probeInterval;

                        bool useProbing = PetSettings.EnableGhostProbing != null && PetSettings.EnableGhostProbing.Value;
                        int rayCount = PetSettings.GhostProbeRays != null ? PetSettings.GhostProbeRays.Value : 9;

                        if (!useProbing) rayCount = 1;
                        if (rayCount < 1) rayCount = 1;
                        if (rayCount > 21) rayCount = 21;
                        if (rayCount % 2 == 0) rayCount++;

                        List<float> angles = new List<float> { 0f };
                        int pairs = (rayCount - 1) / 2;
                        float maxHalfAngle = 70f;
                        float angleStep = pairs > 0 ? (maxHalfAngle / pairs) : 0f;

                        for (int i = 1; i <= pairs; i++)
                        {
                            angles.Add(angleStep * i);
                            angles.Add(-angleStep * i);
                        }

                        float bestScore = float.NegativeInfinity;
                        Vector3 bestCandidateDir = desiredDir;

                        float probeDist = PetSettings.GhostProbeDistance != null ? PetSettings.GhostProbeDistance.Value : 2.5f;
                        int futureSteps = 5;
                        float simStepDistance = probeDist / futureSteps;

                        for (int a = 0; a < angles.Count; a++)
                        {
                            Vector3 initialProbeDir = Quaternion.Euler(0f, angles[a], 0f) * desiredDir;
                            Vector3 currentGhostPos = transform.position;
                            float totalClearance = 0f;

                            for (int s = 0; s < futureSteps; s++)
                            {
                                Vector3 ghostKnee = currentGhostPos + Vector3.up * (stepOffset + scaledRadius);
                                Vector3 ghostHead = currentGhostPos + Vector3.up * Mathf.Max(headHeight, stepOffset + scaledRadius + 0.1f);

                                bool kneeHit = Physics.SphereCast(ghostKnee, scaledRadius, initialProbeDir, out RaycastHit kHit, simStepDistance, obstacleMask, QueryTriggerInteraction.Ignore) && !IsColliderPlayerOrItem(kHit.collider);
                                bool headHit = Physics.SphereCast(ghostHead, scaledRadius, initialProbeDir, out RaycastHit hHit, simStepDistance, obstacleMask, QueryTriggerInteraction.Ignore) && !IsColliderPlayerOrItem(hHit.collider);

                                if (!kneeHit && !headHit)
                                {
                                    totalClearance += simStepDistance;
                                    if (drawDebugRays) PetRuntimeDrawer.DrawLine(ghostKnee, ghostKnee + initialProbeDir * simStepDistance, Color.cyan, debugFade);
                                    currentGhostPos += initialProbeDir * simStepDistance;
                                }
                                else if (kneeHit && !headHit)
                                {
                                    Vector3 stepRayOrigin = ghostKnee + initialProbeDir * (kHit.distance + 0.1f) + Vector3.up * stepOffset;
                                    if (Physics.Raycast(stepRayOrigin, Vector3.down, out RaycastHit stepHit, stepOffset * 2.5f, obstacleMask, QueryTriggerInteraction.Ignore) && !IsColliderPlayerOrItem(stepHit.collider))
                                    {
                                        if (stepHit.normal.y > 0.5f)
                                        {
                                            totalClearance += simStepDistance;
                                            if (drawDebugRays) PetRuntimeDrawer.DrawLine(ghostKnee, ghostKnee + initialProbeDir * simStepDistance, new Color(1f, 0.5f, 0f), debugFade);
                                            currentGhostPos = stepHit.point;
                                            currentGhostPos += initialProbeDir * (simStepDistance - kHit.distance);
                                        }
                                        else break;
                                    }
                                    else break;
                                }
                                else
                                {
                                    float distHit = headHit ? hHit.distance : kHit.distance;
                                    totalClearance += Mathf.Max(0.01f, distHit - 0.05f);
                                    if (drawDebugRays) PetRuntimeDrawer.DrawLine(ghostKnee, ghostKnee + initialProbeDir * distHit, Color.red, debugFade);
                                    break;
                                }
                            }

                            float deadEndPenalty = 0f;
                            for (int m = 0; m < deadEndMemories.Count; m++)
                            {
                                Vector3 toMemory = deadEndMemories[m].Position - transform.position;
                                toMemory.y = 0f;
                                float memDist = toMemory.magnitude;

                                if (memDist < 1.5f && memDist > 0.01f)
                                {
                                    float memDot = Vector3.Dot(initialProbeDir, toMemory.normalized);
                                    if (memDot > 0f) deadEndPenalty += memDot * (1f - (memDist / 1.5f)) * 5.0f;
                                }
                            }

                            float targetAlignment = Vector3.Dot(initialProbeDir, desiredDir);
                            float alignmentNorm = (targetAlignment + 1.0f) * 0.5f;

                            float momentumDot = Vector3.Dot(initialProbeDir, smoothedManualDir.normalized);
                            float momentumBonus = (momentumDot > 0.5f) ? (momentumDot * 1.5f) : 0f;

                            float memoryPenalty = 0f;
                            if (lastWallHitNormal.sqrMagnitude > 0.1f)
                            {
                                float hitDot = Vector3.Dot(initialProbeDir, -lastWallHitNormal);
                                if (hitDot > 0f) memoryPenalty = hitDot * 15.0f;
                            }

                            float tieBreakerRightHand = 0f;
                            float indecisionPenalty = 0f;

                            if (isStuckInMaze && panicTimer <= 0f)
                            {
                                tieBreakerRightHand = (a % 2 != 0) ? 0.4f : 0f;
                                if (momentumDot > 0.1f) momentumBonus += (momentumDot * 4.0f);

                                bool isActivelyDodging = Vector3.Angle(desiredDir, smoothedManualDir.normalized) > 30f;
                                if (isActivelyDodging && momentumDot < 0f) indecisionPenalty = 15.0f;
                            }

                            float clearanceRatio = totalClearance / probeDist;
                            float wallPenalty = 0f;
                            if (clearanceRatio < 0.4f) wallPenalty = 20.0f;
                            else if (clearanceRatio < 1.0f) wallPenalty = (1.0f - clearanceRatio) * 10.0f;

                            float navMeshBonus = 0f;
                            if (seekingNavMeshEntrance)
                            {
                                float navDot = Vector3.Dot(initialProbeDir, (steeringTarget - transform.position).normalized);
                                if (navDot > 0.3f) navMeshBonus = navDot * 3.0f;
                            }

                            float score = (clearanceRatio * 10.0f) + (alignmentNorm * 3.0f) + navMeshBonus + momentumBonus + tieBreakerRightHand - wallPenalty - deadEndPenalty - indecisionPenalty - memoryPenalty;

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestCandidateDir = initialProbeDir;
                            }
                        }

                        cachedBestDir = bestCandidateDir;
                    }

                    if (drawDebugRays) PetRuntimeDrawer.DrawLine(transform.position + Vector3.up * stepOffset, transform.position + Vector3.up * stepOffset + cachedBestDir * 1.8f, Color.green, debugFade);

                    if (smoothedManualDir.sqrMagnitude < 0.001f) smoothedManualDir = transform.forward;
                    smoothedManualDir = Vector3.Slerp(smoothedManualDir, cachedBestDir.normalized, Time.deltaTime * 12f);

                    Vector3 flatManual = new Vector3(smoothedManualDir.x, 0f, smoothedManualDir.z);
                    if (flatManual.sqrMagnitude > 0.01f)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(flatManual.normalized, Vector3.up);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 480f * Time.deltaTime);

                        float angleToTarget = Vector3.Angle(transform.forward, smoothedManualDir.normalized);
                        float moveFactor = 1f;
                        if (angleToTarget > 45f) moveFactor = Mathf.Clamp01((120f - angleToTarget) / 75f);

                        if (moveFactor > 0.05f)
                        {
                            float actualDist = moveDist * moveFactor;

                            Vector3 futureStepPos = transform.position + smoothedManualDir.normalized * actualDist;
                            Vector3 abyssCheckOrigin = futureStepPos + Vector3.up * stepOffset;

                            if (!Physics.SphereCast(abyssCheckOrigin, 0.15f * currentScale, Vector3.down, out _, 4.0f * currentScale, obstacleMask, QueryTriggerInteraction.Ignore))
                            {
                                actualDist = 0f;
                                lastWallHitNormal = -smoothedManualDir.normalized;
                                if (drawDebugRays) PetRuntimeDrawer.DrawLine(abyssCheckOrigin, abyssCheckOrigin + Vector3.down * (4.0f * currentScale), Color.magenta, debugFade);
                            }

                            if (actualDist > 0f)
                            {
                                if (!CheckPathClear(bottom, top, scaledRadius, smoothedManualDir.normalized, actualDist + 0.05f, obstacleMask, out RaycastHit wallHit))
                                {
                                    transform.position += smoothedManualDir.normalized * actualDist;
                                    lastWallHitNormal = Vector3.zero;
                                }
                                else
                                {
                                    Vector3 elevatedBottom = bottom + Vector3.up * (stepOffset * 0.5f);
                                    Vector3 elevatedTop = top + Vector3.up * (stepOffset * 0.5f);

                                    if (!CheckPathClear(elevatedBottom, elevatedTop, scaledRadius, smoothedManualDir.normalized, actualDist + 0.05f, obstacleMask, out _))
                                    {
                                        transform.position += Vector3.up * (stepOffset * 0.35f) + smoothedManualDir.normalized * actualDist;
                                        lastWallHitNormal = Vector3.zero;
                                    }
                                    else
                                    {
                                        lastWallHitNormal = wallHit.normal;

                                        Vector3 slideDir = Vector3.ProjectOnPlane(smoothedManualDir.normalized, wallHit.normal);
                                        slideDir.y = 0f;

                                        if (slideDir.sqrMagnitude > 0.01f && !CheckPathClear(bottom, top, scaledRadius, slideDir.normalized, actualDist * 0.7f + 0.02f, obstacleMask, out _))
                                        {
                                            transform.position += slideDir.normalized * (actualDist * 0.7f);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    float currentSafeHeight = GetSafeRaycastHeight(transform.position, 0.65f * currentScale);
                    Vector3 currentGroundCheck = transform.position + Vector3.up * currentSafeHeight;
                    bool hasCurrentGround = FindSupportSurface(currentGroundCheck, currentSafeHeight + 2.5f, out float currentGroundY);

                    Vector3 forwardOffset = (smoothedManualDir.sqrMagnitude > 0.01f) ? smoothedManualDir.normalized * 0.30f * currentScale : Vector3.zero;
                    float aheadSafeHeight = GetSafeRaycastHeight(transform.position + forwardOffset, 0.65f * currentScale);
                    Vector3 aheadGroundCheck = transform.position + forwardOffset + Vector3.up * aheadSafeHeight;
                    bool hasAheadGround = FindSupportSurface(aheadGroundCheck, aheadSafeHeight + 2.5f, out float aheadGroundY);

                    if (hasCurrentGround || hasAheadGround)
                    {
                        Vector3 adjustedPosition = transform.position;
                        float targetY = hasCurrentGround ? currentGroundY : aheadGroundY;

                        if (hasAheadGround && aheadGroundY > targetY && (aheadGroundY - targetY) <= stepOffset) targetY = aheadGroundY;
                        else if (hasAheadGround && hasCurrentGround && (currentGroundY < aheadGroundY - 0.5f)) targetY = aheadGroundY;

                        if (adjustedPosition.y < targetY)
                        {
                            adjustedPosition.y = Mathf.Lerp(adjustedPosition.y, targetY, 30f * Time.deltaTime);
                            if (targetY - adjustedPosition.y < 0.02f) adjustedPosition.y = targetY;
                        }
                        else if (adjustedPosition.y > targetY)
                        {
                            float dropSpeed = Mathf.Min(30f, 15f + (adjustedPosition.y - targetY) * 10f);
                            adjustedPosition.y = Mathf.Lerp(adjustedPosition.y, targetY, dropSpeed * Time.deltaTime);
                            if (adjustedPosition.y - targetY < 0.02f) adjustedPosition.y = targetY;
                        }

                        transform.position = adjustedPosition;
                    }
                    else
                    {
                        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit fallHit, 4.0f, GetGroundMask(), QueryTriggerInteraction.Ignore))
                        {
                            if (!IsColliderPlayerOrItem(fallHit.collider)) transform.position = new Vector3(transform.position.x, Mathf.Lerp(transform.position.y, fallHit.point.y, 25f * Time.deltaTime), transform.position.z);
                        }
                    }

                    ResolvePenetrations();

                    // Reengate da NavMesh quando o caminho à frente estiver desobstruído e mapeado
                    if (navMeshCooldown <= 0f && escapeTimer <= 0f && panicTimer <= 0f && !isJumping)
                    {
                        if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 0.8f, NavMesh.AllAreas))
                        {
                            if (Mathf.Abs(navHit.position.y - transform.position.y) <= 0.35f)
                            {
                                Vector3 forwardCheckOrigin = transform.position + Vector3.up * (0.35f * currentScale);
                                bool pathClearOfObstacle = !Physics.SphereCast(forwardCheckOrigin, 0.28f * currentScale, transform.forward, out RaycastHit hitClear, 0.6f * currentScale, obstacleMask, QueryTriggerInteraction.Ignore)
                                                                               || IsColliderPlayerOrItem(hitClear.collider);

                                if (pathClearOfObstacle)
                                {
                                    NavMeshPath path = new NavMeshPath();
                                    if (NavMesh.CalculatePath(navHit.position, navDestination, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid)
                                    {
                                        agent.enabled = true;
                                        agent.Warp(navHit.position);
                                        agent.isStopped = false;
                                        lastDestination = Vector3.positiveInfinity;
                                        lastSetDestinationTime = 0f;
                                        LogNavMeshTransition(true, "Caminho desobstruído, retornando para NavMesh");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void StopMoving()
        {
            if (agent == null) return;

            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                if (agent.hasPath) agent.ResetPath();
                agent.velocity = Vector3.zero;
            }

            lastDestination = Vector3.positiveInfinity;
            lastSetDestinationTime = 0f;
        }
    }

    /// Renderizador de linhas 3D visíveis diretamente dentro do jogo compilado (Standalone).

    public static class PetRuntimeDrawer
    {
        private class LineEntry
        {
            public GameObject Object;
            public LineRenderer Renderer;
            public float ExpireTime;
        }

        private class DrawerRunner : MonoBehaviour
        {
            private void Update()
            {
                float now = Time.time;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i].Object != null && pool[i].Object.activeSelf && pool[i].ExpireTime <= now)
                    {
                        pool[i].Object.SetActive(false);
                    }
                }
            }
        }

        private static GameObject container;
        private static Material lineMat;
        private static readonly List<LineEntry> pool = new List<LineEntry>();
        private const int MaxLines = 512;

        private static void EnsureInit()
        {
            if (container != null) return;

            container = new GameObject("AiChan_RuntimeDebugDrawer");
            UnityEngine.Object.DontDestroyOnLoad(container);
            container.AddComponent<DrawerRunner>();

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            lineMat = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        public static void DrawLine(Vector3 start, Vector3 end, Color color, float duration = 0.05f)
        {
            EnsureInit();

            float now = Time.time;
            LineEntry entry = null;

            for (int i = 0; i < pool.Count; i++)
            {
                if (!pool[i].Object.activeSelf || pool[i].ExpireTime <= now)
                {
                    entry = pool[i];
                    break;
                }
            }

            if (entry == null)
            {
                if (pool.Count >= MaxLines)
                {
                    entry = pool[0];
                }
                else
                {
                    GameObject obj = new GameObject("DebugLine_" + pool.Count);
                    obj.transform.SetParent(container.transform);
                    LineRenderer lr = obj.AddComponent<LineRenderer>();
                    lr.material = lineMat;
                    lr.startWidth = 0.025f;
                    lr.endWidth = 0.025f;
                    lr.positionCount = 2;
                    lr.useWorldSpace = true;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;

                    entry = new LineEntry { Object = obj, Renderer = lr };
                    pool.Add(entry);
                }
            }

            entry.ExpireTime = now + duration;
            entry.Renderer.startColor = color;
            entry.Renderer.endColor = color;
            entry.Renderer.SetPosition(0, start);
            entry.Renderer.SetPosition(1, end);
            entry.Object.SetActive(true);
        }
    }
}