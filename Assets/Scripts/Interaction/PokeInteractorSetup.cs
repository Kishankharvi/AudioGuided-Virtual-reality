using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace AGVRSystem.Interaction
{
    /// <summary>
    /// Automatically adds XRPokeInteractor to ALL four fingertip bones of each OVR hand.
    /// The per-finger toggles are removed — every finger always gets an interactor,
    /// making all fingers capable of interacting with world-space UI canvases.
    /// Attach this component to the /Managers root or OVRCameraRig.
    /// </summary>
    public class PokeInteractorSetup : MonoBehaviour
    {
        [Header("Poke Settings")]
        [SerializeField] private float _pokeDepth = 0.02f;
        [SerializeField] private float _pokeWidth = 0.005f;
        [SerializeField] private float _pokeSelectWidth = 0.015f;
        [SerializeField] private float _pokeHoverRadius = 0.05f;
        [SerializeField] private bool _enableUIInteraction = true;

        // All five fingertip bones — every finger can interact with world-space canvases
        private static readonly OVRSkeleton.BoneId[] AllFingerTipBones =
        {
            OVRSkeleton.BoneId.Hand_ThumbTip,
            OVRSkeleton.BoneId.Hand_IndexTip,
            OVRSkeleton.BoneId.Hand_MiddleTip,
            OVRSkeleton.BoneId.Hand_RingTip,
            OVRSkeleton.BoneId.Hand_PinkyTip,
        };

        private const float RetryInterval = 0.5f;
        private const float MaxWaitTime = 10f;

        private void Start()
        {
            StartCoroutine(SetupWhenReady());
        }

        private IEnumerator SetupWhenReady()
        {
            float waited = 0f;
            OVRSkeleton[] skeletons = null;

            while (waited < MaxWaitTime)
            {
                skeletons = FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None);
                bool anyReady = false;
                foreach (var sk in skeletons)
                {
                    if (sk.IsInitialized) { anyReady = true; break; }
                }

                if (anyReady) break;

                waited += RetryInterval;
                yield return new WaitForSeconds(RetryInterval);
            }

            if (skeletons == null || skeletons.Length == 0)
            {
                Debug.LogWarning($"[PokeInteractorSetup] No OVRSkeleton found after {MaxWaitTime}s.");
                yield break;
            }

            foreach (var skeleton in skeletons)
                yield return StartCoroutine(SetupForSkeleton(skeleton));
        }

        private IEnumerator SetupForSkeleton(OVRSkeleton skeleton)
        {
            float waited = 0f;
            while (!skeleton.IsInitialized && waited < MaxWaitTime)
            {
                waited += RetryInterval;
                yield return new WaitForSeconds(RetryInterval);
            }

            if (!skeleton.IsInitialized)
            {
                Debug.LogWarning($"[PokeInteractorSetup] Skeleton '{skeleton.name}' never initialised.");
                yield break;
            }

            // Add XRPokeInteractor to every fingertip — no per-finger toggle so
            // scene-serialized boolean values cannot accidentally disable fingers
            foreach (var boneId in AllFingerTipBones)
            {
                Transform tipBone = FindBone(skeleton, boneId);
                if (tipBone == null)
                {
                    Debug.LogWarning($"[PokeInteractorSetup] Bone '{boneId}' not found on '{skeleton.name}'.");
                    continue;
                }

                // Skip if already added (e.g. script runs twice)
                if (tipBone.GetComponent<XRPokeInteractor>() != null)
                    continue;

                AddPokeInteractor(tipBone.gameObject, skeleton);
                Debug.Log($"[PokeInteractorSetup] XRPokeInteractor added to '{boneId}' on '{skeleton.name}'.");
            }
        }

        private void AddPokeInteractor(GameObject fingertip, OVRSkeleton skeleton)
        {
            var poke = fingertip.AddComponent<XRPokeInteractor>();

            poke.pokeDepth           = _pokeDepth;
            poke.pokeWidth           = _pokeWidth;
            poke.pokeSelectWidth     = _pokeSelectWidth;
            poke.pokeHoverRadius     = _pokeHoverRadius;
            poke.enableUIInteraction = _enableUIInteraction;

            bool isLeft = skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.HandLeft
                       || skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft;

            poke.handedness = isLeft ? InteractorHandedness.Left : InteractorHandedness.Right;

            EnsureInteractionManager();
        }

        private static Transform FindBone(OVRSkeleton skeleton, OVRSkeleton.BoneId boneId)
        {
            if (skeleton.Bones == null) return null;
            foreach (var bone in skeleton.Bones)
            {
                if (bone == null || bone.Transform == null) continue;
                if (bone.Id == boneId) return bone.Transform;
            }
            return null;
        }

        private static void EnsureInteractionManager()
        {
            if (FindAnyObjectByType<XRInteractionManager>() == null)
            {
                new GameObject("[XRInteractionManager]").AddComponent<XRInteractionManager>();
                Debug.Log("[PokeInteractorSetup] Created XRInteractionManager.");
            }
        }
    }
}
