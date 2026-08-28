using Photon.Pun;
using UnityEngine;
using System.Reflection;
using HarmonyLib;

namespace ElsaPetMod
{
    public partial class PetCompanionController
    {
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

        public bool IsGrabbedByLocalPlayer()
        {
            if (myGrabObject == null)
                myGrabObject = GetComponent<PhysGrabObject>();

            if (myGrabObject == null)
                return false;

            PlayerAvatar localPlayer = SemiFunc.PlayerAvatarLocal();

            if (localPlayer == null || localPlayer.physGrabber == null)
                return false;

            PhysGrabObject heldObject = GrabbedPhysGrabObjectField?.GetValue(localPlayer.physGrabber) as PhysGrabObject;

            if (heldObject == myGrabObject)
                return true;

            // Fallback para o estado replicado
            return myGrabObject.playerGrabbing != null &&
                   myGrabObject.playerGrabbing.Contains(localPlayer.physGrabber);
        }
        public void UpdateRemoteNetworkInterpolation()
        {
            if (!IsNetworkClientOnly || !hasNetworkTarget)
                return;

            float distance = Vector3.Distance(transform.position, networkTargetPosition);

            if (distance > RemoteSnapDistance)
            {
                transform.position = networkTargetPosition;
                transform.rotation = networkTargetRotation;
                return;
            }

            float posFactor = 1f - Mathf.Exp(-12f * Time.deltaTime);
            transform.position = Vector3.Lerp(
                transform.position,
                networkTargetPosition,
                posFactor
            );
            
            float rotFactor = 1f - Mathf.Exp(-15f * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                networkTargetRotation,
                rotFactor
            );
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

        public void SnapNetworkInterpolation(
            Vector3 newPosition,
            Quaternion newRotation)
        {
            networkTargetPosition = newPosition;
            networkTargetRotation = newRotation;
            hasNetworkTarget = true;

            transform.position = newPosition;
            transform.rotation = newRotation;
        }

        public void NetworkSyncTransform(
            Vector3 position,
            Quaternion rotation)
        {
            if (!IsNetworkClientOnly)
                return;

            if (!hasNetworkTarget)
                InitializeNetworkInterpolation();

            networkTargetPosition = position;
            networkTargetRotation = rotation;
        }
    }
}