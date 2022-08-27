﻿using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace net.fushizen.modular_avatar.core.editor
{
    internal class MeshRetargeterResetHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => HookSequence.SEQ_RESETTERS;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            BoneDatabase.ResetBones();
            return true;
        }
    }

    internal static class BoneDatabase
    {
        private static Dictionary<Transform, bool> IsRetargetable = new Dictionary<Transform, bool>();

        internal static void ResetBones()
        {
            IsRetargetable.Clear();
        }

        internal static void AddMergedBone(Transform bone)
        {
            IsRetargetable[bone] = true;
        }

        internal static void MarkNonRetargetable(Transform bone)
        {
            if (IsRetargetable.ContainsKey(bone)) IsRetargetable[bone] = false;
        }

        internal static Transform GetRetargetedBone(Transform bone)
        {
            if (bone == null || !IsRetargetable.ContainsKey(bone)) return null;

            while (bone != null && IsRetargetable.ContainsKey(bone) && IsRetargetable[bone]) bone = bone.parent;

            if (IsRetargetable.ContainsKey(bone)) return null;
            return bone;
        }

        internal static IEnumerable<KeyValuePair<Transform, Transform>> GetRetargetedBones()
        {
            return IsRetargetable.Where((kvp) => kvp.Value)
                .Select(kvp => new KeyValuePair<Transform, Transform>(kvp.Key, GetRetargetedBone(kvp.Key)))
                .Where(kvp => kvp.Value != null);
        }

        public static Transform GetRetargetedBone(Transform bone, bool fallbackToOriginal)
        {
            Transform retargeted = GetRetargetedBone(bone);

            return retargeted ? retargeted : (fallbackToOriginal ? bone : null);
        }
    }
    
    internal class RetargetMeshes : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => HookSequence.SEQ_RETARGET_MESH;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            foreach (var renderer in avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool isRetargetable = false;
                foreach (var bone in renderer.bones)
                {
                    if (BoneDatabase.GetRetargetedBone(bone) != null)
                    {
                        isRetargetable = true;
                        break;
                    }
                }

                if (isRetargetable)
                {
                    new MeshRetargeter(renderer).Retarget();
                }
            }
            
            // Now remove retargeted bones
            if (true)
            {
                foreach (var bonePair in BoneDatabase.GetRetargetedBones())
                {
                    if (BoneDatabase.GetRetargetedBone(bonePair.Key) == null) continue;

                    var sourceBone = bonePair.Key;
                    var destBone = bonePair.Value;

                    var children = new List<Transform>();
                    foreach (Transform child in sourceBone)
                    {
                        children.Add(child);
                    }
                    
                    foreach (Transform child in children) {
                        child.SetParent(destBone, true);
                    }

                    UnityEngine.Object.DestroyImmediate(sourceBone.gameObject);
                }

            }

            return true;
        }
    }
    
    /**
     * This class processes a given mesh, adjusting the bind poses for any bones that are to be merged to instead match
     * the bind pose of the original avatar's bone.
     */
    public class MeshRetargeter
    {
        private readonly SkinnedMeshRenderer renderer;
        private Mesh src, dst;

        struct BindInfo
        {
            public Matrix4x4 priorLocalToBone;
            public Matrix4x4 localToBone;
            public Matrix4x4 priorToNew;
        }
        
        public MeshRetargeter(SkinnedMeshRenderer renderer)
        {
            this.renderer = renderer;
        }

        public void Retarget()
        {
            
            var avatar = RuntimeUtil.FindAvatarInParents(renderer.transform);
            if (avatar == null) throw new System.Exception("Could not find avatar in parents of " + renderer.name);
            var avatarTransform = avatar.transform;

            var avPos = avatarTransform.position;
            var avRot = avatarTransform.rotation;
            var avScale = avatarTransform.lossyScale;

            avatarTransform.position = Vector3.zero;
            avatarTransform.rotation = Quaternion.identity;
            avatarTransform.localScale = Vector3.one;
            
            src = renderer.sharedMesh;
            dst = Mesh.Instantiate(src);
            dst.name = "RETARGETED: " + src.name;

            RetargetBones();
            AdjustShapeKeys();
            
            avatarTransform.position = avPos;
            avatarTransform.rotation = avRot;
            avatarTransform.localScale = avScale; 
            
            AssetDatabase.CreateAsset(dst, Util.GenerateAssetPath());
        }

        private void AdjustShapeKeys()
        {
            // TODO
        }

        private void RetargetBones()
        {
            var originalBindPoses = src.bindposes;
            var originalBones = renderer.bones;

            var newBones = (Transform[]) originalBones.Clone();
            var newBindPoses = (Matrix4x4[]) originalBindPoses.Clone();

            for (int i = 0; i < originalBones.Length; i++)
            {
                Transform newBindTarget = BoneDatabase.GetRetargetedBone(originalBones[i]);
                if (newBindTarget == null) continue;

                Matrix4x4 Bp = newBindTarget.worldToLocalMatrix * originalBones[i].localToWorldMatrix * originalBindPoses[i];
                
                newBones[i] = newBindTarget;
                newBindPoses[i] = Bp;
            }

            dst.bindposes = newBindPoses;
            renderer.bones = newBones;
            renderer.sharedMesh = dst;
            renderer.rootBone = BoneDatabase.GetRetargetedBone(renderer.rootBone, true);
            renderer.probeAnchor = BoneDatabase.GetRetargetedBone(renderer.probeAnchor, true);
        }
    }
}