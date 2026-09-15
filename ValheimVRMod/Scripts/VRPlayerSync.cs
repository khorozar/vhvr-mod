using System.Linq;
using RootMotion.FinalIK;
using UnityEngine;
using ValheimVRMod.Utilities;
using ValheimVRMod.VRCore;

using static ValheimVRMod.Utilities.LogUtils;

namespace ValheimVRMod.Scripts {
    public class VRPlayerSync : MonoBehaviour, WeaponWieldSync.TwoHandedStateProvider {

        private VRIK vrikSync;

        const float MIN_CHANGE = 0.001f;
        // A VR pose contains hands, pelvis, fingers, weapon and optional feet. Sending it every
        // rendered frame needlessly updates the player's ZDO and allocates a package, while 30 Hz
        // remains smooth because clients interpolate every physics step.
        private const float OWNER_SYNC_INTERVAL = 1f / 30f;
        private const float OWNER_SYNC_HEARTBEAT_INTERVAL = 1f;
        private const float POSITION_SYNC_THRESHOLD = 0.005f;
        private const float ROTATION_SYNC_THRESHOLD = 1f;
        private const float FINGER_ROTATION_SYNC_THRESHOLD = 2f;

        public GameObject camera = null;
        public GameObject rightHand = null;
        public GameObject leftHand = null;
        public GameObject pelvis = null;
        public GameObject leftFoot = null;
        public GameObject rightFoot = null;
        public Vector3 weaponSyncLocalPosition;
        public Quaternion weaponSyncLocalRotation;

        public bool isLeftHanded { get { return player == Player.m_localPlayer ? !VRPlayer.isRightHandMainWeaponHand : clientIsLeftHanded; } }
        public EquipType mainHandEquipType = EquipType.None;
        public EquipType offHandEquipType = EquipType.None;
        public EquipType leftHandEquipType { get { return isLeftHanded ? mainHandEquipType : offHandEquipType; } }
        public EquipType rightHandEquipType { get { return isLeftHanded ? offHandEquipType : mainHandEquipType; } }
        public bool hasReceivedData { get; private set; }
        public bool receivingFootData { get; private set; } = false;

        private bool hasReceivedWeaponSync = false;
        private bool clientIsLeftHanded = false; 
        private WeaponWield.TwoHandedState twoHandedState = WeaponWield.TwoHandedState.SingleHanded;
        private bool inverseHold = false;

        private Player player;
        private ZNetView netView;
        private Vector3 ownerLastPositionCamera = Vector3.zero;
        private Vector3 ownerVelocityCamera = Vector3.zero;
        private Vector3 ownerLastPositionLeft = Vector3.zero;
        private Vector3 ownerVelocityLeft = Vector3.zero;
        private Vector3 ownerLastPositionRight = Vector3.zero;
        private Vector3 ownerVelocityRight = Vector3.zero;

        private bool hasTempRelPos = false;
        private Vector3 clientTempRelPosCamera = Vector3.zero;
        private Vector3 clientTempRelPosLeft = Vector3.zero;
        private Vector3 clientTempRelPosRight = Vector3.zero;
        private Vector3 clientTempRelPosPelvis = Vector3.zero;
        private Vector3 clientTempRelPosLeftFoot = Vector3.zero;
        private Vector3 clientTempRelPosRightFoot = Vector3.zero;

        private uint lastDataRevision = 0;
        private float deltaTimeCounter = 0f;
        private float nextOwnerSyncTime;
        private float lastOwnerSyncTime;

        private static readonly string[] FINGERS = { 
            "LeftHandThumb1","LeftHandIndex1","LeftHandMiddle1","LeftHandRing1","LeftHandPinky1",
            "RightHandThumb1","RightHandIndex1","RightHandMiddle1","RightHandRing1","RightHandPinky1"
        };

        private Quaternion[] leftFingerRotations = new Quaternion[20];
        private Quaternion[] rightFingerRotations = new Quaternion[20];
        private readonly OwnerPoseSnapshot lastSentOwnerPose = new OwnerPoseSnapshot();
        // These placeholders belong to this synchronizer. The public fields are later
        // replaced with the local VR rig's objects and must not destroy those objects.
        private GameObject ownedCamera;
        private GameObject ownedLeftHand;
        private GameObject ownedRightHand;
        private GameObject ownedPelvis;
        private GameObject ownedLeftFoot;
        private GameObject ownedRightFoot;

        private bool fingersUpdated;
        // TODO: remove this once weapon sync is fully supported
        

        private void Awake() {
            camera = ownedCamera = new GameObject();
            leftHand = ownedLeftHand = new GameObject();
            rightHand = ownedRightHand = new GameObject();
            pelvis = ownedPelvis = new GameObject();
            leftFoot = ownedLeftFoot = new GameObject();
            rightFoot = ownedRightFoot = new GameObject();
            player = GetComponent<Player>();
            netView = GetComponent<ZNetView>();
        }

        private void OnDestroy()
        {
            Destroy(ownedCamera);
            Destroy(ownedLeftHand);
            Destroy(ownedRightHand);
            Destroy(ownedPelvis);
            Destroy(ownedLeftFoot);
            Destroy(ownedRightFoot);
        }

        void Start()
        {
            if (isOwner())
            {
                updateOwnerLastPositions();
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (isOwner())
            {
                calculateOwnerVelocities(dt);
                return;
            }

            if (!isValid()) {
                return;
            }

            clientSync(dt);
        }

        public void DestroyVrik()
        {
            if (vrikSync == null)
            {
                return;
            }

            Destroy(vrikSync);
            vrikSync = null;
        }

        public WeaponWield.TwoHandedState GetTwoHandedState()
        {
            return twoHandedState;
        }

        public bool IsLeftHanded()
        {
            return isLeftHanded;
        }

        public bool InverseHold()
        {
            if (isOwner())
            {
                if (EquipScript.CurrentMainHandEquipType() == EquipType.Knife)
                {
                    inverseHold = LocalWeaponWield.IsDominantHandHoldInversed;
                }
                else if (EquipScript.IsSpearEquipped())
                {
                    inverseHold = LocalWeaponWield.IsDominantHandHoldInversed || LocalWeaponWield.isCurrentlyTwoHanded();
                }
                else
                {
                    inverseHold = LocalWeaponWield.IsDominantHandHoldInversed && !LocalWeaponWield.isCurrentlyTwoHanded();
                }
            }
            return inverseHold;
        }
        
        public bool IsVrEnabled()
        {
            return vrikSync != null;
        }

        public bool MaybeAddClientWeaponSync(GameObject item)
        {
            // Check hasReceivedData instead once weapon sync is fully supported
            if (hasReceivedWeaponSync)
            {
                item.AddComponent<ClientWeaponSync>();
            }
            return hasReceivedWeaponSync;
        }

        public void UpdateWeaponTransform(Vector3 localPosition, Quaternion localRotation)
        {
            weaponSyncLocalPosition = localPosition;
            weaponSyncLocalRotation = localRotation;
        }

        private void calculateOwnerVelocities(float dt)
        {
            ownerVelocityCamera = (camera.transform.position - player.transform.position - ownerLastPositionCamera) / dt;
            ownerVelocityLeft = (leftHand.transform.position - player.transform.position - ownerLastPositionLeft) / dt;
            ownerVelocityRight = (rightHand.transform.position - player.transform.position - ownerLastPositionRight) / dt;
            // Update "last" position for next cycle velocity calculation
            updateOwnerLastPositions();
        }

        private void updateOwnerLastPositions()
        {
            ownerLastPositionCamera = camera.transform.position - player.transform.position;
            ownerLastPositionLeft = leftHand.transform.position - player.transform.position;
            ownerLastPositionRight = rightHand.transform.position - player.transform.position;
        }

        private void LateUpdate()
        {
            if (isOwner())
            {
                ownerSync();
                return;
            }

            if (vrikSync == null)
            {
                return;
            }

            if (!fingersUpdated) {
                return;
            }
            
            applyFingers(vrikSync.references.leftHand, leftFingerRotations);
            applyFingers(vrikSync.references.rightHand, rightFingerRotations);
            fingersUpdated = false;
        }

        // Transmit position, rotation, and velocity information to server
        private void ownerSync()
        {
            if (!VHVRConfig.UseVrControls() || VRPlayer.ShouldPauseMovement || VRPlayer.vrikRef == null) {
                return;
            }

            var now = Time.unscaledTime;
            if (now < nextOwnerSyncTime)
            {
                return;
            }
            // Rebase on the current time rather than catching up after a hitch: a burst of old
            // poses is less useful than the newest pose and just increases network pressure.
            nextOwnerSyncTime = now + OWNER_SYNC_INTERVAL;

            var isVrikEnabled = VRPlayer.vrikRef.enabled;
            var isPullingBow = BowLocalManager.instance != null && BowLocalManager.instance.pulling;
            var handedness = isLeftHanded;
            var wieldState = LocalWeaponWield.LocalPlayerTwoHandedState;
            var isInverseHold = InverseHold();
            Transform trackedLeftFoot = VRPlayer.leftFoot;
            Transform trackedRightFoot = VRPlayer.rightFoot;
            var isFootTrackingActive =
                VRPlayer.vrPlayerInstance != null &&
                VRPlayer.vrPlayerInstance.shouldTrackFeet() &&
                trackedLeftFoot != null &&
                trackedRightFoot != null;

            if (isVrikEnabled)
            {
                pelvis.transform.SetPositionAndRotation(
                    VRPlayer.vrikRef.solver.spine.pelvis.solverPosition,
                    VRPlayer.vrikRef.solver.spine.pelvis.solverRotation);
            }

            var stateChanged = lastSentOwnerPose.HasChanged(
                camera.transform, leftHand.transform, rightHand.transform, pelvis.transform,
                VRPlayer.vrikRef.references.leftHand, VRPlayer.vrikRef.references.rightHand,
                isVrikEnabled, isPullingBow, handedness, wieldState, isInverseHold,
                weaponSyncLocalPosition, weaponSyncLocalRotation, isFootTrackingActive,
                trackedLeftFoot, trackedRightFoot);
            if (!stateChanged && now - lastOwnerSyncTime < OWNER_SYNC_HEARTBEAT_INTERVAL)
            {
                return;
            }

            lastSentOwnerPose.Capture(
                camera.transform, leftHand.transform, rightHand.transform, pelvis.transform,
                VRPlayer.vrikRef.references.leftHand, VRPlayer.vrikRef.references.rightHand,
                isVrikEnabled, isPullingBow, handedness, wieldState, isInverseHold,
                weaponSyncLocalPosition, weaponSyncLocalRotation, isFootTrackingActive,
                trackedLeftFoot, trackedRightFoot);
            lastOwnerSyncTime = now;

            ZPackage pkg = new ZPackage();

            writeData(pkg, camera, ownerVelocityCamera);
            if (!isVrikEnabled)
            {
                // Stati that should temporarily disable VRIK such as sleeping/staggering/dodging are not sent over network,
                // so we send a vr_data package short of further data to signal remote players that VRIK is temporarily disabled
                netView.GetZDO().Set("vr_data", pkg.GetArray());
                return;
            }

            writeData(pkg, leftHand, ownerVelocityLeft);
            writeData(pkg, rightHand, ownerVelocityRight);
            writeData(pkg, pelvis, ownerVelocityCamera);
            writeFingers(pkg, VRPlayer.vrikRef.references.leftHand);
            writeFingers(pkg, VRPlayer.vrikRef.references.rightHand);
            pkg.Write(isPullingBow);
            pkg.Write(handedness);
            pkg.Write((byte)(twoHandedState = wieldState));
            pkg.Write(isInverseHold);
            pkg.Write(weaponSyncLocalPosition);
            pkg.Write(weaponSyncLocalRotation);
            if (isFootTrackingActive)
            {
                // Snapshot and packet must observe the same physical tracker transforms.
                writeTransformRelativeToPlayer(pkg, trackedLeftFoot);
                writeTransformRelativeToPlayer(pkg, trackedRightFoot);
            }

            netView.GetZDO().Set("vr_data", pkg.GetArray());
        }

        private void writeTransformRelativeToPlayer(ZPackage pkg, Transform transform) 
        {
            pkg.Write(transform.position - player.transform.position);
            pkg.Write(transform.rotation);
        }

        private void writeData(ZPackage pkg, GameObject obj, Vector3 ownerVelocity)
        {
            writeTransformRelativeToPlayer(pkg, obj.transform);
            pkg.Write(ownerVelocity);
        }

        private void clientSync(float dt) {
            ZDO zdo = netView.GetZDO();
            if (zdo == null)
            {
                return;
            }
            var vr_data = zdo.GetByteArray("vr_data");
            if (vr_data == null)
            {
                return;
            }
            hasReceivedData = true;
            ZPackage pkg = new ZPackage(vr_data);
            var currentDataRevision = zdo.DataRevision;
            if (currentDataRevision != lastDataRevision)
            {
                // New data revision since last sync so we reset our deltaT counter
                deltaTimeCounter = 0f;
                // Save the current data revision so we can detect the next new data package
                lastDataRevision = currentDataRevision;
            }
            deltaTimeCounter += dt;
            deltaTimeCounter = Mathf.Min(deltaTimeCounter, 2f);

            extractAndUpdate(pkg, ref camera, ref clientTempRelPosCamera, hasTempRelPos);

            if (!hasMoreData(pkg))
            {
                // If the remote player sends a vr_data package with only camera data but nothing else,
                // they are trying to signal that VRIK should be temporarily disabled (e. g. due to sleeping/staggering/dodging).
                if (vrikSync != null)
                {
                    vrikSync.enabled = false;
                }
                return;
            }

            extractAndUpdate(pkg, ref leftHand, ref clientTempRelPosLeft, hasTempRelPos);
            extractAndUpdate(pkg, ref rightHand, ref clientTempRelPosRight, hasTempRelPos);
            extractAndUpdate(pkg, ref pelvis, ref clientTempRelPosPelvis, hasTempRelPos);

            maybeAddVrik();
            if (vrikSync != null)
            {
                vrikSync.enabled = true;
            }
            readFingers(pkg);
            maybePullBow(pkg.ReadBool());
            clientIsLeftHanded = pkg.ReadBool();
            twoHandedState = (WeaponWield.TwoHandedState) pkg.ReadByte();
            inverseHold = pkg.ReadBool();
            if (hasMoreData(pkg)) // TODO: remove this check once weapon sync is fully supported
            {
                ExtractAndSmoothUpdateVector(pkg, ref weaponSyncLocalPosition, hasTempRelPos);
                weaponSyncLocalRotation = pkg.ReadQuaternion();
                hasReceivedWeaponSync = true;
            }
            if (hasMoreData(pkg))
            {
                ExtractAndSmoothUpdatePositionRelativeToPlayer(pkg, ref leftFoot, ref clientTempRelPosLeftFoot, receivingFootData);
                updateRotation(leftFoot, pkg.ReadQuaternion());
                ExtractAndSmoothUpdatePositionRelativeToPlayer(pkg, ref rightFoot, ref clientTempRelPosRightFoot, receivingFootData);
                updateRotation(rightFoot, pkg.ReadQuaternion());
                receivingFootData = true;
                if (vrikSync != null)
                {
                    VrikCreator.EnableFootTracking(vrikSync);
                }
            }
            else
            {
                receivingFootData = false;
                if (vrikSync != null)
                {
                    VrikCreator.DisableFootTracking(vrikSync);
                }
            }
            hasTempRelPos = true;

            correctHandedness();
        }

        private void correctHandedness()
        {
            // Client isLeftHanded sync may happen after equipping.
            // Force re-equip to trigger a patch with the updated isLeftHanded.
            var mainHandItem = player.m_visEquipment.m_rightItemInstance;
            var mainHandItemHash = player.m_visEquipment.m_currentRightItemHash;
            var mainHandItemQuality = player.m_visEquipment.m_currentRightItemQuality;
            if (mainHandItem != null && mainHandItemHash != 0)
            {
                if (isLeftHanded ?
                    mainHandItem.transform.parent == player.m_visEquipment.m_rightHand :
                    mainHandItem.transform.parent == player.m_visEquipment.m_leftHand)
                {
                    LogUtils.LogDebug("Switching main hand item to the other hand");
                    player.m_visEquipment.SetRightHandEquipped(0, 0);
                    player.m_visEquipment.SetRightHandEquipped(mainHandItemHash, mainHandItemQuality);
                }
            }

            var offHandItem = player.m_visEquipment.m_leftItemInstance;
            var offHandItemHash = player.m_visEquipment.m_currentLeftItemHash;
            var offHandItemQuality = player.m_visEquipment.m_currentLeftItemQuality;
            if (offHandItem != null && offHandItemHash != 0)
            {
                if (isLeftHanded ?
                    offHandItem.transform.parent == player.m_visEquipment.m_leftHand :
                    offHandItem.transform.parent == player.m_visEquipment.m_rightHand)
                {
                    LogUtils.LogDebug("Switching secondary hand item to right hand");
                    var variant = player.m_visEquipment.m_currentLeftItemVariant;
                    player.m_visEquipment.SetLeftHandEquipped(0, 0, 0);
                    player.m_visEquipment.SetLeftHandEquipped(offHandItemHash, variant, offHandItemQuality);
                }
            }
        }

        private void maybePullBow(bool pulling) {
            GameObject bow = player.m_visEquipment.m_leftItemInstance;
            if (bow == null)
            {
                return;
            }

            var bowManager = bow.GetComponentInChildren<BowManager>();
            if (bowManager == null) {
                if (!pulling) {
                    return;
                }
                var bowMesh = bow.GetComponentInChildren<MeshFilter>();
                if (bowMesh == null)
                {
                    LogUtils.LogDebug("No bow mesh found for remote player despite pulling a bow");
                    return;
                }
                bowManager = bowMesh.gameObject.AddComponent<BowManager>();
            }
            bowManager.arrowHandTransform = isLeftHanded ? leftHand.transform : rightHand.transform;
            bowManager.pulling = pulling;
        }

        private void ExtractAndSmoothUpdateVector(ZPackage pkg, ref Vector3 v, bool hasRead)
        {
            var newValue = pkg.ReadVector3();
            if (!hasRead)
            {
                v = newValue;
            }
            else if (Vector3.Distance(v, newValue) > MIN_CHANGE)
            {
                v = Vector3.Lerp(v, newValue, 0.2f);
            }
        }

        private void ExtractAndSmoothUpdatePositionRelativeToPlayer(
            ZPackage pkg, ref GameObject obj, ref Vector3 tempRelPos, bool hasTempRelPos)
        {
            var position = pkg.ReadVector3();
            if (!hasTempRelPos)
            {
                tempRelPos = position;
            }

            if (Vector3.Distance(tempRelPos, position) > MIN_CHANGE)
            {
                tempRelPos = Vector3.Lerp(tempRelPos, position, 0.2f);
                position = tempRelPos;
            }

            updatePosition(obj, position + player.transform.position);
        }

        private void extractAndUpdate(ZPackage pkg, ref GameObject obj, ref Vector3 tempRelPos, bool hasTempRelPos)
        {
            ExtractAndSmoothUpdatePositionRelativeToPlayer(pkg, ref obj, ref tempRelPos, hasTempRelPos);
            updateRotation(obj, pkg.ReadQuaternion());
            var velocity = pkg.ReadVector3(); // Deprecated
        }

        private static bool hasMoreData(ZPackage pkg)
        {
            return pkg.m_reader.BaseStream.Position < pkg.GetArray().Length;
        }

        private static void updatePosition(GameObject obj, Vector3 position)
        {
            if (Vector3.Distance(obj.transform.position, position) > MIN_CHANGE)
            {
                obj.transform.position = position;
            }
        }

        private static void updateRotation(GameObject obj, Quaternion rotation)
        {
            if (Quaternion.Angle(obj.transform.rotation, rotation) > MIN_CHANGE)
            {
                obj.transform.rotation = Quaternion.Slerp(obj.transform.rotation, rotation, 0.2f);
            }
        }

        private void maybeAddVrik() {
            if (vrikSync != null)
            {
                return;
            }
            vrikSync =
                VrikCreator.initialize(
                    gameObject, leftHand.transform, rightHand.transform, camera.transform, pelvis.transform, leftFoot.transform, rightFoot.transform);
            VrikCreator.resetVrikHandTransform(player);
        }

        private bool isOwner()
        {
            if (!isValid())
            {
                return false;
            }
            var zdo = netView.GetZDO();
            if (zdo == null)
            {
                LogError("Null ZDO during isOwner check.");
                return false;
            }
            return zdo.IsOwner();
        }

        private bool isValid()
        {
            return netView != null && netView.IsValid();
        }
        
        private void writeFingers(ZPackage pkg, Transform hand) {

            for (int i = 0; i < hand.childCount; i++) {

                var child = hand.GetChild(i);

                if (FINGERS.Contains(child.name)) {
                    writeFinger(pkg, child);
                }
            }
        }

        private void writeFinger(ZPackage pkg, Transform finger) {
            pkg.Write(finger.localRotation);
            if (finger.childCount > 0) {
                writeFinger(pkg, finger.GetChild(0));
            } 
        }
        
        private void readFingers(ZPackage pkg) {

            for (int i = 0; i < 20; i++) {
                leftFingerRotations[i] = pkg.ReadQuaternion();
            }
            for (int i = 0; i < 20; i++) {
                rightFingerRotations[i] = pkg.ReadQuaternion();
            }

            fingersUpdated = true;
        }

        private void applyFingers(Transform hand, Quaternion[] fingerRotations) {

            int fingerCounter = 0;
            
            for (int i = 0; i < hand.childCount; i++) {

                var child = hand.GetChild(i);

                if (FINGERS.Contains(child.name)) {
                    applyFinger(child, fingerRotations, ref fingerCounter);
                }
            }
        }

        private void applyFinger(Transform finger, Quaternion[] fingerRotations, ref int fingerCounter) {
            
            finger.localRotation = fingerRotations[fingerCounter];
            fingerCounter++;
            if (finger.childCount > 0) {
                applyFinger(finger.GetChild(0), fingerRotations, ref fingerCounter);
            }
        }

        private class OwnerPoseSnapshot
        {
            private bool hasValue;
            private bool vrikEnabled;
            private bool pullingBow;
            private bool leftHanded;
            private WeaponWield.TwoHandedState wieldState;
            private bool inverseHold;
            private bool footTrackingActive;
            private Vector3 cameraPosition, leftHandPosition, rightHandPosition, pelvisPosition;
            private Quaternion cameraRotation, leftHandRotation, rightHandRotation, pelvisRotation;
            private Vector3 weaponPosition, leftFootPosition, rightFootPosition;
            private Quaternion weaponRotation, leftFootRotation, rightFootRotation;
            private readonly Quaternion[] leftFingerRotations = new Quaternion[20];
            private readonly Quaternion[] rightFingerRotations = new Quaternion[20];

            public bool HasChanged(
                Transform camera, Transform leftHand, Transform rightHand, Transform pelvis,
                Transform leftFingerRoot, Transform rightFingerRoot,
                bool currentVrikEnabled, bool currentPullingBow, bool currentLeftHanded,
                WeaponWield.TwoHandedState currentWieldState, bool currentInverseHold,
                Vector3 currentWeaponPosition, Quaternion currentWeaponRotation, bool currentFootTrackingActive,
                Transform leftFoot, Transform rightFoot)
            {
                if (!hasValue ||
                    vrikEnabled != currentVrikEnabled ||
                    HasTransformChanged(cameraPosition, cameraRotation, camera) ||
                    pullingBow != currentPullingBow || leftHanded != currentLeftHanded ||
                    wieldState != currentWieldState || inverseHold != currentInverseHold)
                {
                    return true;
                }

                if (!currentVrikEnabled)
                {
                    return false;
                }

                return
                    HasTransformChanged(leftHandPosition, leftHandRotation, leftHand) ||
                    HasTransformChanged(rightHandPosition, rightHandRotation, rightHand) ||
                    HasTransformChanged(pelvisPosition, pelvisRotation, pelvis) ||
                    HasFingerRotationChanged(leftFingerRoot, leftFingerRotations) ||
                    HasFingerRotationChanged(rightFingerRoot, rightFingerRotations) ||
                    HasPositionChanged(weaponPosition, currentWeaponPosition) ||
                    HasRotationChanged(weaponRotation, currentWeaponRotation) ||
                    footTrackingActive != currentFootTrackingActive ||
                    (currentFootTrackingActive &&
                        (HasTransformChanged(leftFootPosition, leftFootRotation, leftFoot) ||
                         HasTransformChanged(rightFootPosition, rightFootRotation, rightFoot)));
            }

            public void Capture(
                Transform camera, Transform leftHand, Transform rightHand, Transform pelvis,
                Transform leftFingerRoot, Transform rightFingerRoot,
                bool currentVrikEnabled, bool currentPullingBow, bool currentLeftHanded,
                WeaponWield.TwoHandedState currentWieldState, bool currentInverseHold,
                Vector3 currentWeaponPosition, Quaternion currentWeaponRotation, bool currentFootTrackingActive,
                Transform leftFoot, Transform rightFoot)
            {
                hasValue = true;
                vrikEnabled = currentVrikEnabled;
                pullingBow = currentPullingBow;
                leftHanded = currentLeftHanded;
                wieldState = currentWieldState;
                inverseHold = currentInverseHold;
                footTrackingActive = currentFootTrackingActive;
                CaptureTransform(camera, out cameraPosition, out cameraRotation);
                weaponPosition = currentWeaponPosition;
                weaponRotation = currentWeaponRotation;

                if (!currentVrikEnabled)
                {
                    return;
                }

                CaptureTransform(leftHand, out leftHandPosition, out leftHandRotation);
                CaptureTransform(rightHand, out rightHandPosition, out rightHandRotation);
                CaptureTransform(pelvis, out pelvisPosition, out pelvisRotation);
                CaptureFingerRotations(leftFingerRoot, leftFingerRotations);
                CaptureFingerRotations(rightFingerRoot, rightFingerRotations);
                if (currentFootTrackingActive)
                {
                    CaptureTransform(leftFoot, out leftFootPosition, out leftFootRotation);
                    CaptureTransform(rightFoot, out rightFootPosition, out rightFootRotation);
                }
            }

            private static bool HasTransformChanged(Vector3 position, Quaternion rotation, Transform transform)
            {
                return HasPositionChanged(position, transform.position) || HasRotationChanged(rotation, transform.rotation);
            }

            private static bool HasPositionChanged(Vector3 previous, Vector3 current)
            {
                return (previous - current).sqrMagnitude > POSITION_SYNC_THRESHOLD * POSITION_SYNC_THRESHOLD;
            }

            private static bool HasRotationChanged(Quaternion previous, Quaternion current)
            {
                return Quaternion.Angle(previous, current) > ROTATION_SYNC_THRESHOLD;
            }

            private static void CaptureTransform(Transform transform, out Vector3 position, out Quaternion rotation)
            {
                position = transform.position;
                rotation = transform.rotation;
            }

            private static bool HasFingerRotationChanged(Transform fingerRoot, Quaternion[] previousRotations)
            {
                var index = 0;
                for (var i = 0; i < fingerRoot.childCount; i++)
                {
                    var child = fingerRoot.GetChild(i);
                    if (FINGERS.Contains(child.name) && HasFingerChainChanged(child, previousRotations, ref index))
                    {
                        return true;
                    }
                }
                return false;
            }

            private static bool HasFingerChainChanged(Transform finger, Quaternion[] previousRotations, ref int index)
            {
                if (index >= previousRotations.Length ||
                    Quaternion.Angle(previousRotations[index++], finger.localRotation) > FINGER_ROTATION_SYNC_THRESHOLD)
                {
                    return true;
                }

                if (finger.childCount > 0)
                {
                    return HasFingerChainChanged(finger.GetChild(0), previousRotations, ref index);
                }

                return false;
            }

            private static void CaptureFingerRotations(Transform fingerRoot, Quaternion[] rotations)
            {
                var index = 0;
                CaptureFingerRotationsRecursive(fingerRoot, rotations, ref index);
            }

            private static void CaptureFingerRotationsRecursive(Transform transform, Quaternion[] rotations, ref int index)
            {
                for (var i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (FINGERS.Contains(child.name))
                    {
                        CaptureFingerChain(child, rotations, ref index);
                    }
                }
            }

            private static void CaptureFingerChain(Transform finger, Quaternion[] rotations, ref int index)
            {
                if (index >= rotations.Length)
                {
                    return;
                }

                rotations[index++] = finger.localRotation;
                if (finger.childCount > 0)
                {
                    CaptureFingerChain(finger.GetChild(0), rotations, ref index);
                }
            }
        }
    }

    public class ClientWeaponSync : MonoBehaviour
    {
        private VRPlayerSync playerSync { get { return _playerSync == null ? (_playerSync = GetComponentInParent<VRPlayerSync>()) : _playerSync; } }
        private VRPlayerSync _playerSync;

        protected void OnRenderObject()
        {
            if (playerSync == null)
            {
                return;
            }
            transform.localPosition = playerSync.weaponSyncLocalPosition;
            transform.localRotation = playerSync.weaponSyncLocalRotation;
        }
    }
}
