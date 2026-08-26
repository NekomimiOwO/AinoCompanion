using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;
using System.Reflection; // A CURA DO ERRO: Permite o uso do FieldInfo
using HarmonyLib;

namespace ElsaPetMod
{
    public partial class PetCompanionController : MonoBehaviour
    {
        public enum PetState { WaitingForLevel, FollowOwner, CarryItemToCart, Petting, Grabbed, Stunned, Dead }

        public float currentHealth = 100f;
        private static readonly FieldInfo GrabbedPhysGrabObjectField =
            AccessTools.Field(typeof(PhysGrabber), "grabbedPhysGrabObject");

        public float maxHealth = 100f;

        public NavMeshAgent agent;
        public Animator animator;
        public Transform visualRoot;
        public PlayerAvatar owner;
        public AiChanAudio aiAudio;
        public PetState state = PetState.WaitingForLevel;

        public float noGrabUntilTime;
        public bool IsRecovering => waitingToStandUp || isStandingUp;

        private bool initialized;
        private PhysGrabObject myGrabObject;
        private Rigidbody myRigidbody;
        private CapsuleCollider myCapsule;

        private float grabbedReleaseTime;
        private float standUpTime;
        private bool waitingToStandUp;
        private bool isStandingUp;
        private bool isJumping;
        private Vector3 lastAnimPosition;
        private float smoothedSpeedSqr;

        private float debugJitterTimer;
        private Vector3 debugLastLogicPos;
        private Vector3 debugLastVisualPos;
        private bool debugJitterInitialized;
        private float debugLastSampleTime;


        private PetState lastLoggedState = PetState.WaitingForLevel;

        private static readonly int IsBigHash = Animator.StringToHash("isBig");
        private static readonly int MovingHash = Animator.StringToHash("moving");
        private static readonly int StandingHash = Animator.StringToHash("standing");
        private static readonly int ChasingHash = Animator.StringToHash("chasing");
        private static readonly int FallingHash = Animator.StringToHash("falling");
        private static readonly int FlyingHash = Animator.StringToHash("flying");
        private static readonly int LookingUnderHash = Animator.StringToHash("lookingUnder");
        private static readonly int StunnedHash = Animator.StringToHash("stunned");
        private static readonly int PetTriggerHash = Animator.StringToHash("Pet");

        private readonly RaycastHit[] groundedRaycastHits = new RaycastHit[8];
        private readonly Collider[] groundedOverlapColliders = new Collider[16];

        private Vector3 syncCarryPos = Vector3.zero;
        private Quaternion syncCarryRot = Quaternion.identity;

        private int groundMask;

        private void Awake()
        {
            groundMask = ~LayerMask.GetMask(
                "Player",
                "PlayerOnlyCollision",
                "Ignore Raycast"
            );

            agent = GetComponent<NavMeshAgent>();
            if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();

            myGrabObject = GetComponent<PhysGrabObject>();
            myRigidbody = GetComponent<Rigidbody>();
            myCapsule = GetComponent<CapsuleCollider>();

            aiAudio = GetComponent<AiChanAudio>();
            if (aiAudio == null) aiAudio = gameObject.AddComponent<AiChanAudio>();

            agent.radius = 0.15f; // Mitigação do Perímetro Radial Interno
            agent.height = 1.35f;
            agent.acceleration = 28f;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = 1.15f;

            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.autoBraking = true;
            agent.autoRepath = true;

            // O SEGREDO DO DESACOPLAMENTO: A NavMesh fará as contas, mas NÃO tocará no corpo!
            agent.updatePosition = false;
            agent.updateRotation = false;


            if (visualRoot == null) visualRoot = transform;

            lastAnimPosition = transform.position;
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            owner = null;
            chosenOwnersThisLevel.Clear();
            if (state == PetState.CarryItemToCart) ClearCarryState();
        }

        private void LogStateTransitions()
        {
            if (!PetSettings.EnableDebugLogs.Value)
                return;

            if (!PetSettings.EnableStateTransitionLogs.Value)
                return;

            if (state != lastLoggedState)
            {
                int viewID = 0;
                PhotonView pv = GetComponent<PhotonView>();
                if (pv != null) viewID = pv.ViewID;

                int actorNum = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
                int masterNum = PhotonNetwork.MasterClient != null ? PhotonNetwork.MasterClient.ActorNumber : 0;
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

                Plugin.Log.LogInfo($"[AiNet] Transition: {lastLoggedState} -> {state} | ViewID: {viewID} | Actor: {actorNum} | Master: {masterNum} | Scene: {sceneName}");
                lastLoggedState = state;
            }
        }

        private void OnDisable()
        {
            isJumping = false;
            waitingToStandUp = false;
            isStandingUp = false;

            if (gameObject.activeInHierarchy && agent != null && !agent.enabled && state != PetState.Grabbed && state != PetState.Dead)
            {
                EnsureGroundedAndNavMesh();
            }
        }

        private IEnumerator Start()
        {
            if (PhotonNetwork.NetworkingClient != null && PhotonNetwork.NetworkingClient.LoadBalancingPeer != null)
            {
                PhotonNetwork.NetworkingClient.LoadBalancingPeer.TrafficStatsEnabled = true;
            }

            yield return new WaitForSeconds(0.2f);

            if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            {
                if (agent != null) agent.enabled = false;
                if (myRigidbody != null) { myRigidbody.isKinematic = true; myRigidbody.useGravity = false; }
                initialized = true;
                state = PetState.FollowOwner;
                yield break;
            }

            if (SemiFunc.RunIsLevel())
            {
                while (LevelGenerator.Instance == null || !LevelGenerator.Instance.Generated) yield return null;
            }

            // SUBSTITUIR POR:
            EnsureGroundedAndNavMesh();
            CreateHoldPoint();

            if (myRigidbody != null)
            {
                myRigidbody.isKinematic = true;
                myRigidbody.useGravity = false;
            }

            owner = FindNearestOwner();
            if (owner != null && !chosenOwnersThisLevel.Contains(owner)) chosenOwnersThisLevel.Add(owner);

            initialized = true;
            state = PetState.FollowOwner;

            float switchIntervalMinutes = PetSettings.PlayerSwitchInterval != null ? PetSettings.PlayerSwitchInterval.Value : 3f;
            if (switchIntervalMinutes > 0f) nextPlayerSwitchTime = Time.time + switchIntervalMinutes * 60f;
        }

        // Memória para saber o exato momento em que o jogador pegou e soltou
        private bool wasLocallyGrabbed;

        private void Update()
        {
            if (!initialized || state == PetState.Dead) return;
            // ... (restante do código)

            UpdateDynamicSettings();
            LogStateTransitions();

            if (agent != null && agent.enabled && agent.isOnNavMesh && !isJumping && !IsRecovering && state != PetState.Grabbed)
            {
                Vector3 brainPosition = agent.nextPosition;

                if (Vector3.Distance(transform.position, brainPosition) > 1.5f)
                {
                    transform.position = brainPosition;
                }
                else
                {
                    // A CURA DO VOO: Usa o baseOffset para memorizar a altura do chão físico real
                    float targetY = brainPosition.y + agent.baseOffset;
                    float smoothY = Mathf.Lerp(transform.position.y, targetY, Time.deltaTime * 15f);
                    transform.position = new Vector3(brainPosition.x, smoothY, brainPosition.z);
                }

                Vector3 moveDir = agent.desiredVelocity;
                moveDir.y = 0f;
                if (moveDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(moveDir.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, agent.angularSpeed * Time.deltaTime);
                }
            }

            if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            {
                // O Cliente agora é um Proxy Visual absoluto.
                // Não ativamos a gravidade local pois o jogo proíbe levantar objetos sem Ownership.
                if (agent != null && agent.enabled) agent.enabled = false;

                if (myRigidbody != null && !myRigidbody.isKinematic)
                {
                    myRigidbody.velocity = Vector3.zero;
                    myRigidbody.angularVelocity = Vector3.zero;
                    myRigidbody.isKinematic = true;
                    myRigidbody.useGravity = false;
                }

                UpdateAnimation();
                return;
            }



            // Trava a física caso ela não esteja sendo segurada, atordoada ou pulando
            if (myRigidbody != null && !myRigidbody.isKinematic && state != PetState.Grabbed && state != PetState.Stunned && !isJumping && !waitingToStandUp)
            {
                myRigidbody.isKinematic = true;
                myRigidbody.useGravity = false;
            }

            if (agent != null && agent.enabled && !agent.isOnNavMesh && state != PetState.Grabbed && !isJumping && !IsRecovering)
            {
                if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                }
                else
                {
                    agent.enabled = false;
                }
            }

            if (state != PetState.Grabbed && !waitingToStandUp) UpdateContinuousSurfaceOffset();

            bool isGrabbed = IsGrabbedByAnyone(myGrabObject);
            if (isGrabbed) HandleGrabbedState();
            else if (state == PetState.Grabbed) HandleReleasedState();

            UpdateAnimation();

            if (IsRecovering || state == PetState.Grabbed || isJumping) return;

            TickMultiplayerOwnerSwitch();
            TickAutoJump();

            if (state == PetState.Stunned) TickStun();
            else if (state == PetState.FollowOwner) TickFollowOwner();
            else if (state == PetState.CarryItemToCart) TickCarryItemToCart();
            else if (state == PetState.Petting) TickPetting();
        }

        private void HandleGrabbedState()
        {
            if (state != PetState.Grabbed)
            {
                // SUBSTITUIR POR:
                if (agent != null && agent.enabled)
                {
                    if (agent.isOnNavMesh)
                    {
                        agent.isStopped = true;
                        if (agent.hasPath) agent.ResetPath();
                    }
                    agent.enabled = false;
                }
                if (myRigidbody != null) { myRigidbody.isKinematic = false; myRigidbody.useGravity = true; myRigidbody.constraints = RigidbodyConstraints.None; }
                DropItemAtFeet();
                state = PetState.Grabbed;
            }
            grabbedReleaseTime = Time.time;
            waitingToStandUp = false;
            isStandingUp = false;
            stuckTimer = 0f;
            stuckHighTimer = 0f;
        }

        private void HandleReleasedState()
        {
            // A CURA DOS 5 SEGUNDOS: Acorda o Rigidbody e devolve a gravidade no Host!
            // Isso impede que a pet flutue e dispare o timeout de emergência de 5s.
            if (!waitingToStandUp && !isStandingUp)
            {
                if (myRigidbody != null && myRigidbody.isKinematic)
                {
                    myRigidbody.isKinematic = false;
                    myRigidbody.useGravity = true;
                    myRigidbody.WakeUp();
                }
            }

            bool isGrounded = CheckIsGrounded();
            bool timeout = Time.time - grabbedReleaseTime > 5f;

            if (!waitingToStandUp && !isStandingUp && (isGrounded || timeout))
            {
                waitingToStandUp = true;
                standUpTime = Time.time + (PetSettings.StandUpDelay != null ? PetSettings.StandUpDelay.Value : 1.5f);
                if (myRigidbody != null && !myRigidbody.isKinematic) myRigidbody.constraints = RigidbodyConstraints.None;
            }

            if (waitingToStandUp && Time.time >= standUpTime)
            {
                waitingToStandUp = false;
                isStandingUp = true;

                if (myCapsule != null) myCapsule.enabled = true;

                Vector3 flatForward = (owner != null && owner.gameObject.activeInHierarchy) ? owner.transform.position - transform.position : transform.forward;
                flatForward.y = 0f;
                if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;

                Quaternion uprightRot = Quaternion.LookRotation(flatForward.normalized, Vector3.up);

                transform.rotation = uprightRot;

                if (myRigidbody != null)
                {
                    myRigidbody.velocity = Vector3.zero;
                    myRigidbody.angularVelocity = Vector3.zero;
                    myRigidbody.rotation = uprightRot;
                }

                EnsureGroundedAndNavMesh();

                if (agent != null && agent.enabled)
                {
                    agent.nextPosition = transform.position;
                }

                isStandingUp = false;
                state = PetState.FollowOwner;
            }
        }

        private bool IsGrabbedByAnyone(PhysGrabObject grabObj)
        {
            if (grabObj == null) return false;
            if (Time.time < noGrabUntilTime) return false;
            if (grabObj.playerGrabbing != null && grabObj.playerGrabbing.Count > 0) return true;
            return grabObj.grabbed;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;

            if (state == PetState.Grabbed || state == PetState.Dead || isJumping || collision.rigidbody == null) return;
            if (collision.gameObject.layer == LayerMask.NameToLayer("Player") || collision.gameObject.layer == LayerMask.NameToLayer("PlayerOnlyCollision")) return;
            // Filtro para ignorar pequenos impactos / objetos muito leves
            if (collision.rigidbody.mass < 0.2f) return;
            // Avalia APENAS a velocidade absoluta do objeto que colidiu com ela.
            // Isso previne que a própria velocidade de movimento dela conte como força de impacto.
            float incomingSpeedSqr = collision.rigidbody.velocity.sqrMagnitude;

            // --- AQUI: Puxa o valor da configuração (2.0 m/s padrão) elevado ao quadrado para checagem rápida ---
            float threshold = PetSettings.KnockdownSpeedThreshold != null ? PetSettings.KnockdownSpeedThreshold.Value : 2.0f;


            // Só sofre tombo se o objeto estiver se movendo rápido (~2 m/s) EM DIREÇÃO A ELA
            // Exemplo: um item jogado pelo jogador, ou caindo do teto
            if (incomingSpeedSqr > 4.0f)
                if (incomingSpeedSqr > (threshold * threshold))
            {
                if (agent != null && agent.enabled) agent.enabled = false;
                if (myRigidbody != null)
                {
                    if (!myRigidbody.isKinematic) { myRigidbody.velocity = Vector3.zero; myRigidbody.angularVelocity = Vector3.zero; }
                    myRigidbody.isKinematic = false;
                    myRigidbody.useGravity = true;
                    myRigidbody.constraints = RigidbodyConstraints.None;
                    
                    myRigidbody.AddForce(collision.rigidbody.velocity * 0.6f, ForceMode.Impulse);
                }
                DropItemAtFeet();
                state = PetState.Grabbed;
            }
        }

        private void UpdateDynamicSettings()
        {
            if (myRigidbody != null)
            {
                float configuredMass = PetSettings.PetMass != null ? PetSettings.PetMass.Value : 1.5f;
                if (Mathf.Abs(myRigidbody.mass - configuredMass) > 0.01f) { myRigidbody.mass = configuredMass; if (myGrabObject != null) myGrabObject.massOriginal = configuredMass; }

                float configuredDrag = PetSettings.AngularDrag != null ? PetSettings.AngularDrag.Value : 0.5f;
                if (Mathf.Abs(myRigidbody.angularDrag - configuredDrag) > 0.01f) myRigidbody.angularDrag = configuredDrag;
            }
            if (agent != null)
            {
                // Alterna entre a velocidade de transporte e a velocidade padrão
                float targetSpeed = (state == PetState.CarryItemToCart)
                    ? (PetSettings.CarrySpeed != null ? PetSettings.CarrySpeed.Value : 4.0f)
                    : (PetSettings.Speed != null ? PetSettings.Speed.Value : 3.5f);

                if (Mathf.Abs(agent.speed - targetSpeed) > 0.01f) agent.speed = targetSpeed;

                float boostAccel = (state == PetState.CarryItemToCart) ? 120f : 28f;
                if (Mathf.Abs(agent.acceleration - boostAccel) > 0.01f) agent.acceleration = boostAccel;
            }
        }

        private int GetGroundMask()
        {
            int mask = LayerMask.GetMask("Player", "PlayerOnlyCollision", "Ignore Raycast");
            mask |= (1 << 14); // Ignora a RoomVolume (Layer 14) do R.E.P.O explicitamente!
            mask |= (1 << LayerMask.NameToLayer("RoomVolume")); // Prevenção dupla
            return ~mask;
        }

        private bool CheckIsGrounded()
        {
            Vector3 rayOrigin = transform.position + Vector3.up * 0.35f;

            int raycastCount = Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                groundedRaycastHits,
                0.75f,
                groundMask,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < raycastCount; i++)
            {
                Collider collider = groundedRaycastHits[i].collider;

                if (collider != null &&
                    !collider.transform.IsChildOf(transform))
                {
                    return true;
                }
            }

            Vector3 overlapCenter = transform.position + Vector3.up * 0.15f;

            int overlapCount = Physics.OverlapSphereNonAlloc(
                overlapCenter,
                0.35f,
                groundedOverlapColliders,
                groundMask,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < overlapCount; i++)
            {
                Collider collider = groundedOverlapColliders[i];

                if (collider != null &&
                    !collider.transform.IsChildOf(transform))
                {
                    return true;
                }
            }

            return false;
        }

        private void LateUpdate()
        {
            if (state == PetState.Grabbed || holdPoint == null) return;

            if (carriedItem != null)
            {
                carriedItem.transform.position = holdPoint.position;
                carriedItem.transform.rotation = holdPoint.rotation;

                if (carriedRigidbody != null)
                {
                    if (!carriedRigidbody.isKinematic)
                    {
                        carriedRigidbody.velocity = Vector3.zero;
                        carriedRigidbody.angularVelocity = Vector3.zero;
                        carriedRigidbody.isKinematic = true;
                        carriedRigidbody.useGravity = false;
                    }
                    carriedRigidbody.position = holdPoint.position;
                    carriedRigidbody.rotation = holdPoint.rotation;
                }
            }
            else if (carriedPlayerAvatar != null)
            {
                // A CURA DO PING 500: O cálculo é feito no milissegundo final da pipeline de renderização,
                // impossibilitando que a Unity mude o modelo de lugar antes da sua câmera!
                Vector3 carryPos = transform.position + transform.forward * 0.4f + Vector3.up * 1.6f;
                Quaternion carryRot = transform.rotation;

                carriedPlayerAvatar.transform.position = carryPos;
                carriedPlayerAvatar.transform.rotation = carryRot;

                if (carriedTumble != null)
                {
                    carriedTumble.transform.position = carryPos;
                    carriedTumble.transform.rotation = carryRot;
                }

                if (carriedRigidbody != null)
                {
                    if (!carriedRigidbody.isKinematic)
                    {
                        carriedRigidbody.velocity = Vector3.zero;
                        carriedRigidbody.angularVelocity = Vector3.zero;
                        carriedRigidbody.isKinematic = true;
                        carriedRigidbody.useGravity = false;
                    }
                    carriedRigidbody.position = carryPos;
                    carriedRigidbody.rotation = carryRot;
                }
            }
        }
        private void FixedUpdate()
        {
            // Vazio. O transporte cinemático é gerido unicamente no LateUpdate 
            // para evitar conflitos de ciclos e a velocidade fantasma de física.
        }

        public void SetScaleMultiplier(float multiplier)
        {
            multiplier = Mathf.Clamp(multiplier, 0.1f, 5.0f);

            if (PhotonNetwork.InRoom)
            {
                GetComponent<PhotonView>().RPC(nameof(SyncScaleRPC), RpcTarget.AllBuffered, multiplier);
            }
            else
            {
                SyncScaleRPC(multiplier);
            }
        }

        [PunRPC]
        private void SyncScaleRPC(float multiplier)
        {
            transform.localScale = Vector3.one * multiplier;

            if (agent != null)
            {
                agent.radius = 0.32f * multiplier;
                agent.height = 1.35f * multiplier;
            }

            if (myGrabObject != null)
            {
                float baseMass = 1.5f;
                myGrabObject.massOriginal = baseMass * multiplier;

                if (myRigidbody != null)
                {
                    myRigidbody.mass = myGrabObject.massOriginal;
                }
            }
        }

        private void UpdateAnimation()
        {
            if (animator == null) return;

            float currentSpeedSqr;
            if (agent != null && agent.enabled && agent.isOnNavMesh) currentSpeedSqr = agent.velocity.sqrMagnitude;
            else
            {
                Vector3 delta = transform.position - lastAnimPosition;
                delta.y = 0f;
                currentSpeedSqr = (delta / Mathf.Max(Time.deltaTime, 0.001f)).sqrMagnitude;
            }
            lastAnimPosition = transform.position;

            smoothedSpeedSqr = Mathf.Lerp(smoothedSpeedSqr, currentSpeedSqr, Time.deltaTime * 8f);

            bool moving = state != PetState.Grabbed && state != PetState.Stunned && smoothedSpeedSqr > 0.05f;
            bool falling = state == PetState.Grabbed;
            bool stunned = state == PetState.Stunned;
            bool flying = isJumping;

            animator.SetBool(IsBigHash, false);
            animator.SetBool(MovingHash, moving && !flying);
            animator.SetBool(StandingHash, !moving && !falling && !stunned && !flying);
            animator.SetBool(ChasingHash, moving && !flying);
            animator.SetBool(FallingHash, falling);
            animator.SetBool(FlyingHash, flying);
            animator.SetBool(LookingUnderHash, false);
            animator.SetBool(StunnedHash, stunned);
        }
    }
}