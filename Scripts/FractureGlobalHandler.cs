using g3;
using OpenCover.Framework.Model;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Jobs;

namespace Zombie1111_uDestruction
{
    public class FractureGlobalHandler : MonoBehaviour
    {
        #region InstanceIds
        private ConcurrentDictionary<int, GlobalFracData> partColsInstanceId = new();

        private class GlobalTransToSet
        {
            public Rigidbody rb;
            public float mass;
            /// <summary>
            /// 0 = remove, 1 = add+reset, 2 = add+update
            /// </summary>
            public byte updateStatus;
        }

        [System.Serializable]
        public class GlobalFracData
        {
            public FractureThis fracThis;
            public short partIndex;
        }

        private void Awake()
        {
            //setup rb job
            GetRbVelocities_setup();

#pragma warning disable IDE0079
#pragma warning disable 0162
            //get all rigidbodies in scene
            if (FracGlobalSettings.addAllActiveRigidbodiesOnLoad == true)
            {
                Rigidbody[] bodies = GameObject.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (Rigidbody rb in bodies)
                {
                    OnAddOrUpdateRb(rb, rb.mass);
                    //rigidbodiesInstanceId.TryAdd(rb.GetInstanceID(), rb);
                }
            }
#pragma warning restore 0162
#pragma warning restore IDE0079

            //verify global handlers
            //If more than one global handler exists merge other with this one
            FractureGlobalHandler[] handlers = GameObject.FindObjectsByType<FractureGlobalHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < handlers.Length; i += 1)
            {
                if (handlers[i] == this) continue;
                handlers[i].GetRbVelocities_end();//make sure the job aint running

                partColsInstanceId.AddRange(handlers[i].partColsInstanceId);

                for (int rI = 0; rI < handlers[i].jGRV_rb_mass.Count; rI++)
                {
                    if (handlers[i].jGRV_rbTrans[rI].transform == null
                        || handlers[i].jGRV_rbTrans[rI].transform.TryGetComponent(out Rigidbody rb) == false) continue;

                    OnAddOrUpdateRb(rb, handlers[i].jGRV_rb_mass[rI].mass);//potential issue, if the rb hit something the same frame it wont cause any damage
                }

                foreach (var pair in handlers[i].jGRV_rbToSet)
                {
                    jGRV_rbToSet.Add(pair.Key, pair.Value);
                }

                Destroy(handlers[i]);
            }

            ogFixedTimeStep = Time.fixedDeltaTime;
        }

        private void Start()
        {
#pragma warning disable IDE0079
#pragma warning disable 0162
            //When a new scene is loaded old rigidbodies and fracs can often be null
            if (FracGlobalSettings.addAllActiveRigidbodiesOnLoad == true) RemoveNullInstanceIds();
#pragma warning restore 0162
#pragma warning restore IDE0079
        }

        public void OnAddFracPart(FractureThis frac, short partI)
        {
            partColsInstanceId.TryAdd(frac.saved_allPartsCol[partI].GetInstanceID(), new()
            {
                fracThis = frac,
                partIndex = partI
            });
        }

        /// <summary>
        /// Should be called the same frame you destroy a destructable object
        /// </summary>
        public void OnDestroyFracture(FractureThis destroyedFrac)
        {
            for (int i = 0; i < destroyedFrac.allParts.Count; i++)
            {
                partColsInstanceId.TryRemove(destroyedFrac.saved_allPartsCol[i].GetInstanceID(), out _);
            }
        }

        /// <summary>
        /// Returns the fractureThis script and the part index the given collider instanceId is for, null if no part col instanceId
        /// </summary>
        /// <param name="instanceId">The instance id for a collider</param>
        /// <returns></returns>
        public GlobalFracData TryGetFracPartFromColInstanceId(int instanceId)
        {
            if (partColsInstanceId.TryGetValue(instanceId, out GlobalFracData fracD) == true) return fracD;

            return null;
        }

        /// <summary>
        /// Call to remove a rigidbody that you added with OnAddOrUpdateRb(), should always be called before you destroy a rigidbody component
        /// </summary>
        /// <param name="rbToRemove">The rigidbody </param>
        public void OnRemoveRigidbody(Rigidbody rbToRemove)
        {
            int rbId = rbToRemove.GetInstanceID();
            if (jGRV_rbToSet.TryAdd(rbId, new() { updateStatus = 0 }) == false)
            {
                jGRV_rbToSet[rbId].updateStatus = 0;
            }
        }

        /// <summary>
        /// Makes sure the rigidbody is added to the destruction system and resets its properties,
        /// call if you have teleported the rigidbody
        /// </summary>
        /// <param name="mass">The mass the rigidbody has for the destruction system</param>
        public void OnAddOrResetRb(Rigidbody rbToAddOrReset, float mass)
        {
            int rbId = rbToAddOrReset.GetInstanceID();
            if (jGRV_rbToSet.TryAdd(rbId, new()
            {
                mass = mass,
                rb = rbToAddOrReset,
                updateStatus = 1 }) == false)
            {
                if (jGRV_rbToSet[rbId].updateStatus == 2) jGRV_rbToSet[rbId].updateStatus = 1;
                jGRV_rbToSet[rbId].mass = mass;
            }
        }

        /// <summary>
        /// Makes sure the rigidbody is added to the destruction system and updates its properties,
        /// call if you have modified the rigidbody mass
        /// </summary>
        /// <param name="mass">The mass the rigidbody has for the destruction system</param>
        public void OnAddOrUpdateRb(Rigidbody rbToAddOrUpdate, float mass)
        {
            int rbId = rbToAddOrUpdate.GetInstanceID();
            if (jGRV_rbToSet.TryAdd(rbId, new()
            {
                mass = mass,
                rb = rbToAddOrUpdate,
                updateStatus = 2
            }) == false)
            {
                jGRV_rbToSet[rbId].mass = mass;
            }
        }

        /// <summary>
        /// Call to remove all rigidbodies and fractures that has been destroyed
        /// </summary>
        public void RemoveNullInstanceIds()
        {
            bool wasRunning = jGRV_jobIsActive;
            GetRbVelocities_end(); //make sure job aint running

            foreach (int rbId in rbInstancIdToJgrvIndex.Keys)
            {
                if (jGRV_rbTrans[rbInstancIdToJgrvIndex[rbId]].transform != null
                    && jGRV_rbTrans[rbInstancIdToJgrvIndex[rbId]].transform.TryGetComponent<Rigidbody>(out _) == true) continue;

                if (jGRV_rbToSet.TryAdd(rbId, new() { updateStatus = 0 }) == false)
                    jGRV_rbToSet[rbId].updateStatus = 0;
            }

            //start job again
            if (wasRunning == true) GetRbVelocities_start();
        }

        #endregion InstanceIds




        #region MainUpdate and Editor
#if UNITY_EDITOR
        [Space(10)]
        [Header("Debug")]
        [SerializeField][Tooltip("Only exposed for debugging purposes")] private List<GlobalFracData> debugFractureInstanceIds = new();

        private void OnDrawGizmos()
        {
            //maintain temp saveAssets, potential problem, if scene is not saved but the asset is this may leave unused temp assets
            for (int i = tempSaveAssets.Count - 1; i >= 0; i--)
            {
                //if temp has been removed
                if (tempSaveAssets[i].saveAsset == null)
                {
                    tempSaveAssets.RemoveAt(i);
                    continue;
                }

                //if temp should be removed
                if (tempSaveAssets[i].fracThis == null
                    || tempSaveAssets[i].fracThis.saveAsset == null
                    || tempSaveAssets[i].fracThis.saveAsset != tempSaveAssets[i].saveAsset)
                {
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(tempSaveAssets[i].saveAsset));
                    tempSaveAssets.RemoveAt(i);
                }
            }

            //update debug variabels
            if (partColsInstanceId.Count != debugFractureInstanceIds.Count)
            {
                debugFractureInstanceIds = partColsInstanceId.Values.ToList();
            }
        }

        //debug, log destruction time
        [System.NonSerialized] public double totalDesMs = 0.0f;
        [SerializeField] private List<TempSaveAsset> tempSaveAssets = new();

        [System.Serializable]
        private class TempSaveAsset
        {
            public FractureSaveAsset saveAsset;
            public FractureThis fracThis;
        }

        /// <summary>
        /// Creates a temporary saveAsset and assigns it to createFor (The asset will be destroyed automatically if createFor is no longer using it) Editor only
        /// </summary>
        /// <param name="createFor"></param>
        /// <returns>False if unable to create a new temp saveAsset</returns>
        public bool TryCreateTempSaveAsset(FractureThis createFor)
        {
            //verify that the given fracture is valid
            if (createFor == null || createFor.saveAsset != null) return false;

            //Get the path to the folder where all temporary frac save assets should be stored
            string folderPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(this))) + @"\TempFracSaveAssets";
            folderPath = folderPath.Replace(@"\"[0], '/');
            if (AssetDatabase.IsValidFolder(folderPath) == false)
            {
                Debug.LogError("The fracture temp folder is missing, expected " + folderPath + " to be a valid path");
                return false;
            }

            //Create the asset and save it
            folderPath += "/tempFracSaveAsset.asset";
            folderPath = AssetDatabase.GenerateUniqueAssetPath(folderPath);
            FractureSaveAsset fracSaveAsset = ScriptableObject.CreateInstance<FractureSaveAsset>();
            AssetDatabase.CreateAsset(fracSaveAsset, folderPath);
            tempSaveAssets.Add(new() { saveAsset = fracSaveAsset, fracThis = createFor });
            createFor.saveAsset = fracSaveAsset;

            return true;
        }
#endif

        private float syncTime = 0.0f;
        private int syncFrames = 0;
        private float ogFixedTimeStep;

        private void Update()
        {
            //debug keys
            if (Input.GetKeyDown(KeyCode.U) == true)
            {
                Rigidbody[] bodies = GameObject.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (Rigidbody rb in bodies)
                {
                    OnAddOrUpdateRb(rb, rb.mass);
                    //rigidbodiesInstanceId.TryAdd(rb.GetInstanceID(), rb);
                }
            }

#pragma warning disable IDE0079
#pragma warning disable 0162
            //sync fixedTimestep with fps
            if (FracGlobalSettings.syncFixedTimestepWithFps == true)
            {
                syncTime += Time.unscaledDeltaTime;
                syncFrames++;

                if (syncTime >= 10.0f)
                {
                    Time.fixedDeltaTime = MathF.Min(syncTime / syncFrames, ogFixedTimeStep);

                    syncTime = 0.0f;
                    syncFrames = 0;
                }
            }
#pragma warning restore 0162
#pragma warning restore IDE0079
        }

        /// <summary>
        /// Contains all transforms that should be set on the trans velocity system, trans instanceId as key
        /// </summary>
        private Dictionary<int, GlobalTransToSet> jGRV_rbToSet = new();
        private TransformAccessArray jGRV_rbTrans;
        /// <summary>
        /// The mass of the body at the given index, if mass is negative the rigidbody is kinematic (Get abs for real mass)
        /// </summary>
        private List<GlobalRbData> jGRV_rb_mass;
        private JobHandle jGRV_handle;
        private GetRbVelocities_work jGRV_job;
        private Dictionary<int, int> rbInstancIdToJgrvIndex = new();
        private bool jGRV_jobIsActive = false;

        private class GlobalRbData
        {
            public Rigidbody rb;

            /// <summary>
            /// The mass of the rigidbody as seen by the destruction system
            /// </summary>
            public float mass;
        }

        private void OnDestroy()
        {
            ClearUsedMemory();
        }

        private void ClearUsedMemory()
        {
            //dispose GetRbVelocities job
            if (jGRV_rbTrans.isCreated == true) jGRV_rbTrans.Dispose();
            if (jGRV_job.rb_posData.IsCreated == true) jGRV_job.rb_posData.Dispose();
        }

        private void GetRbVelocities_setup()
        {
            jGRV_job = new GetRbVelocities_work()
            {
                rb_posData = new NativeList<GetRbVelocities_work.RbPosData>(Allocator.Persistent)
            };

            jGRV_rbTrans = new();
            jGRV_jobIsActive = false;
            jGRV_rb_mass = new();
        }

        private void GetRbVelocities_start()
        {
            if (jGRV_jobIsActive == true || jGRV_rbTrans.isCreated == false) return;

            //run the job
            jGRV_handle = jGRV_job.Schedule(jGRV_rbTrans);
            jGRV_jobIsActive = true;
        }

        private void GetRbVelocities_end()
        {
            if (jGRV_jobIsActive == false)
            {
                SetNewRbs();
                return;
            }

            //finish the job
            jGRV_handle.Complete();
            jGRV_jobIsActive = false;

            SetNewRbs();

            void SetNewRbs()
            {
                //Set new rbs
                if (jGRV_rbToSet.Count == 0) return;
               
                foreach (int rbId in jGRV_rbToSet.Keys)
                {
                    byte uStatus = jGRV_rbToSet[rbId].updateStatus;
                    Transform rbTrans;
                    if (uStatus > 0)
                    {
                        if (jGRV_rbToSet[rbId].rb == null)
                        {
                            rbTrans = null;
                            uStatus = 0;
                        }
                        else rbTrans = jGRV_rbToSet[rbId].rb.transform;
                    }
                    else rbTrans = null;

                    if (rbInstancIdToJgrvIndex.TryGetValue(rbId, out int jIndex) == true)
                    {
                        if (uStatus > 0)
                        {
                            //update or reset
                            jGRV_rb_mass[jIndex].mass = jGRV_rbToSet[rbId].mass;
                            var posD = jGRV_job.rb_posData[jIndex];
                            //posD.rb_centerW = jGRV_rbToSet[rbId].rb.worldCenterOfMass;
                            //posD.rb_centerL = rbTrans.InverseTransformPoint(posD.rb_centerW);

                            if (uStatus == 2)
                            {
                                jGRV_job.rb_posData[jIndex] = posD;
                                continue;
                            }

                            //posD.rb_velocity = Vector3.zero;
                            //posD.rb_rot = rbTrans.rotation;
                            //posD.rb_rotVel = Quaternion.identity;
                            posD.rbLToWNow = rbTrans.localToWorldMatrix;
                            posD.rbWToLPrev = rbTrans.worldToLocalMatrix;
                            jGRV_job.rb_posData[jIndex] = posD;
                        }
                        else
                        {
                            //remove
                            jGRV_rbTrans.RemoveAtSwapBack(jIndex);
                            jGRV_rb_mass.RemoveAtSwapBack(jIndex);
                            jGRV_job.rb_posData.RemoveAtSwapBack(jIndex);
                            rbInstancIdToJgrvIndex.Remove(rbId);
                            int movedId = rbInstancIdToJgrvIndex.Count;

                            foreach (var pair in rbInstancIdToJgrvIndex)
                            {
                                if (pair.Value != movedId) continue;
                                movedId = pair.Key;
                                break;
                            }

                            rbInstancIdToJgrvIndex[movedId] = jIndex;
                        }
                    }
                    else if (uStatus > 0)
                    {
                        //add
                        if (jGRV_rbTrans.isCreated == false) jGRV_rbTrans = new(new Transform[1] { rbTrans });
                        else jGRV_rbTrans.Add(rbTrans);
                        jGRV_rb_mass.Add(new() { mass = jGRV_rbToSet[rbId].mass, rb = jGRV_rbToSet[rbId].rb });
                        jGRV_job.rb_posData.Add(new()
                        {
                            rbLToWNow = rbTrans.localToWorldMatrix,
                            rbWToLPrev = rbTrans.worldToLocalMatrix
                            //rb_centerW = jGRV_rbToSet[rbId].rb.worldCenterOfMass,
                            //rb_centerL = jGRV_rbToSet[rbId].rb.centerOfMass,
                            //rb_velocity = Vector3.zero,
                            //rb_rot = rbTrans.rotation,
                            //rb_rotVel = Quaternion.identity
                         });

                        rbInstancIdToJgrvIndex.Add(rbId, rbInstancIdToJgrvIndex.Count);
                    }
                }

                jGRV_rbToSet.Clear();
            }
        }

        [BurstCompile]
        private struct GetRbVelocities_work : IJobParallelForTransform
        {
            [NativeDisableContainerSafetyRestriction]
            public NativeList<RbPosData> rb_posData;

            public struct RbPosData
            {
                public Matrix4x4 rbWToLPrev;
                public Matrix4x4 rbLToWNow;
            }

            public void Execute(int index, TransformAccess transform)
            {
                RbPosData posD = rb_posData[index];
                //InterpolateMatrix(ref posD.rbWToLPrev, posD.rbLToWNow.inverse, 0.4f);
                //InterpolateMatrix(ref posD.rbLToWNow, transform.localToWorldMatrix, 0.4f);
                posD.rbWToLPrev = posD.rbLToWNow.inverse;
                posD.rbLToWNow = transform.localToWorldMatrix;

                rb_posData[index] = posD;

                //void InterpolateMatrix(ref Matrix4x4 from, Matrix4x4 to, float t)
                //{
                //    for (int i = 0; i < 16; i++)
                //    {
                //        from[i] = Mathf.Lerp(from[i], to[i], t);
                //    }
                //}
            }
        }

        #endregion MainUpdate and Editor




        #region CollisionHandling
        private void OnEnable()
        {
            //subscribe to on col events
            Physics.ContactModifyEvent += ModificationEvent;
            Physics.ContactModifyEventCCD += ModificationEvent;
        }

        private void OnDisable()
        {
            //unsubscribe from on col events
            Physics.ContactModifyEvent -= ModificationEvent;
            Physics.ContactModifyEventCCD -= ModificationEvent;
        }

        private void FixedUpdate()
        {
            //update stored fixedDeltatime and ignore timers
            fixedDeltatime = Time.fixedDeltaTime;
            foreach (int impId in impIdsToIgnore.Keys)
            {
                impIdsToIgnore.TryGetValue(impId, out float ignoreTime);
                if (ignoreTime <= 0.0f)
                {
                    impIdsToIgnore.TryRemove(impId, out _);
                    continue;
                }

                impIdsToIgnore.TryUpdate(impId, ignoreTime - fixedDeltatime, ignoreTime);
            }

            //end get rb velocities
            GetRbVelocities_end();

            //run late fixedUpdate later
            StartCoroutine(LateFixedUpdate());
        }

        private IEnumerator LateFixedUpdate()
        {
            //wait for late fix update
            yield return new WaitForFixedUpdate();
            GetRbVelocities_start();

        }

        private float fixedDeltatime = 0.01f;
        private ConcurrentDictionary<int, float> impIdsToIgnore = new();

        private class ImpPair
        {
            public float impForceTotal;

            /// <summary>
            /// The velocity of the collision
            /// </summary>
            public Vector3 impVel;

            /// <summary>
            /// The destructable object that was hit
            /// </summary>
            public FractureThis impFrac;

            /// <summary>
            /// Contains all parts that was hit and the force applied to each of them
            /// </summary>
            public List<FractureThis.DestructionPoint> impPoints;

            /// <summary>
            /// The other rigidbody in the collision
            /// </summary>
            public Rigidbody sourceRb;

            /// <summary>
            /// impPairsI[X] is the contactPair index impPoints[X] was created from
            /// </summary>
            public List<int> impPairsI;
        }

        public void ModificationEvent(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            //variabels used durring calculations
            Dictionary<int, ImpPair> impIdToImpPair = new();
            ModifiableContactPair pair;
            int pairI;
            Vector3 rbForceVel;
            Vector3 rbA_forceVel;
            Vector3 rbB_forceVel;
            Vector3 rbA_vel;
            Vector3 rbB_vel;
            Vector3 impNormal;
            Vector3[] impNormals = new Vector3[3];
            Vector3 impPos;
            Vector3[] impPoss = new Vector3[3];
            float impFriction;
            float impBouncyness;

            //get all different impacts applied to every destructable object
            for (pairI = 0; pairI < pairs.Length; pairI++)
            {
                pair = pairs[pairI];
                CalcImpPair();             
            }

            //compute impacts and notify destruction system about them
            foreach (int impId in impIdToImpPair.Keys)
            {
                //get impPair
                ImpPair iPair = impIdToImpPair[impId];

                //normlize impact forces
                float maxImp = float.MinValue;
                int guessedBreakCount = 0;
                int impCount = iPair.impPoints.Count;
                int maxImpI = 0;

                for (int i = 0; i < impCount; i++)
                {
                    //get highest impact force
                    var impP = iPair.impPoints[i];

                    if (impP.force > maxImp)
                    {
                        maxImp = impP.force;
                        maxImpI = i;
                    }

                    //Ignore collision if part most likely will break
                    pair = pairs[iPair.impPairsI[i]];
                    if (iPair.impFrac.GuessIfForceCanCauseBreaking(impP.force, impP.partI, pair.GetBounciness(0)) == true)
                    {
                        guessedBreakCount++;
                        for (int cI = 0; cI < pair.contactCount; cI++) pair.IgnoreContact(cI);
                    }

                    //Normlize impact force
                    var desP = iPair.impPoints[i];
                    desP.force /= iPair.impForceTotal;
                    iPair.impPoints[i] = desP;
                }

                //Multiply normlized impact forces with highest impact force, Because we want more impact points to result in less force at each impact point
                for (int i = 0; i < iPair.impPoints.Count; i++)
                {
                    var desP = iPair.impPoints[i];
                    desP.force *= maxImp;
                    iPair.impPoints[i] = desP;
                }

                //if only a few impacts most likely caused breaking, mark contacts between source and frac to be ignored the next few physics frames
                Debug.Log(guessedBreakCount / (float)impCount + " " + maxImp);
                if (guessedBreakCount / (float)impCount < 0.5f) impIdsToIgnore.TryAdd(impId, 0.1f);

                ////ignore contact if imp will most likely cause break
                //if (iPair.impFrac.GuessIfForceCanCauseBreaking(maxImp, iPair.impPoints[maxImpI].partI) == true)
                //{
                //    Debug.Log("Ignore " + maxImp);
                //    foreach (int pI in iPair.impPairsI)
                //    {
                //        for (int conI = 0; conI < pairs[pI].contactCount; conI++)
                //        {
                //            pairs[pI].IgnoreContact(conI);
                //        }
                //    }
                //}
                //else
                //{
                //    Debug.Log("Hit " + maxImp);
                //
                //    //if imp most likely does not cause break, mark contacts between source and frac to be ignored the next few physics frames
                //    impIdsToIgnore.TryAdd(impId, 0.1f);
                //}

                //notify destructable object about impact
                iPair.impFrac.RegisterImpact(new()
                {
                    impForceTotal = maxImp,
                    impVel = iPair.impVel,
                    parentI = iPair.impFrac.jCDW_job.partsParentI[iPair.impPoints[maxImpI].partI]
                }, iPair.impPoints.ToNativeArray(Allocator.Persistent), iPair.sourceRb, impId, false);
            }

            void CalcImpPair()
            {
                //get the destructable object we hit, or return if we did not hit one
                if (partColsInstanceId.TryGetValue(pair.colliderInstanceID, out GlobalFracData fracD_a) == true
                    && fracD_a.fracThis.jCDW_job.partsParentI[fracD_a.partIndex] < 0)
                {
                    if (rbInstancIdToJgrvIndex.TryGetValue(pair.bodyInstanceID, out _) == false) return;
                    fracD_a = null;
                }

                if (partColsInstanceId.TryGetValue(pair.otherColliderInstanceID, out GlobalFracData fracD_b) == true
                    && fracD_b.fracThis.jCDW_job.partsParentI[fracD_b.partIndex] < 0)
                {
                    if (rbInstancIdToJgrvIndex.TryGetValue(pair.otherBodyInstanceID, out _) == false) return;
                    fracD_b = null;
                }

                if (fracD_a == null && fracD_b == null) return;

                //get pair contacts info
                if (FracGlobalSettings.canGetImpactNormalFromPlane == true && pair.contactCount > 2)
                {
                    for (int contI = 0; contI < 3; contI++)
                    {
                        impPoss[contI] = pair.GetPoint(contI);
                    }

                    impPos = (impPoss[0] + impPoss[1] + impPoss[2]) / 3.0f;
                    impNormal = Vector3.Cross(impPoss[0] - impPoss[1], impPoss[0] - impPoss[2]).normalized;
                }
                else
                {
                    impNormal = Vector3.zero;
                    impPos = Vector3.zero;

                    for (int contI = 0; contI < pair.contactCount; contI++)
                    {
                        impNormal += -pair.GetNormal(contI);
                        impPos += pair.GetPoint(contI);
                    }

                    impPos /= pair.contactCount;
                    impNormal /= pair.contactCount;
                    impNormal.Normalize();
                }

                impFriction = pair.GetDynamicFriction(0);
                impBouncyness = pair.GetBounciness(0);

                //for (int contI = 0; contI < pair.contactCount; contI++)
                {
                    //Get rigidbody velocity
                    rbA_vel = CalcRigidbodyVel(pair.bodyInstanceID, out int rbI_a, out float norrDiffA);
                    rbA_forceVel = rbA_vel * norrDiffA;
                    //rbA_forceVel = rbA_vel;

                    rbB_vel = CalcRigidbodyVel(pair.otherBodyInstanceID, out int rbI_b, out float norrDiffB);
                    rbB_forceVel = rbB_vel * norrDiffB;

                    bool rbA_causedImp = false;
                    bool rbB_causedImp = false;

                    if (rbA_forceVel.sqrMagnitude > rbB_forceVel.sqrMagnitude)
                    {
                        if ((rbA_forceVel - rbB_forceVel).sqrMagnitude < FracGlobalSettings.minimumImpactVelocity) return;

                        rbA_causedImp = true;
                        if (rbB_vel.sqrMagnitude > FracGlobalSettings.minimumImpactVelocity && Vector3.Dot(rbA_vel.normalized, rbB_vel.normalized) < 0.0f)
                        {
                            rbB_causedImp = true;
                        }
                    }
                    else
                    {
                        if ((rbB_forceVel - rbA_forceVel).sqrMagnitude < FracGlobalSettings.minimumImpactVelocity) return;

                        rbB_causedImp = true;
                        if (rbA_vel.sqrMagnitude > FracGlobalSettings.minimumImpactVelocity && Vector3.Dot(rbB_vel.normalized, rbA_vel.normalized) < 0.0f)
                        {
                            rbA_causedImp = true;
                        }
                    }

                    //Get impact force and source
                    //The force applied to X is the relative velocity between X and Y multiplied by the impact angle, mass and stenght(How easy it breaks+deform) of Y


                    //If force applied aint strong enough to break the weakest material, dont ignore col
                    //If X hit Y. Y get motion in X direction and X get motion in X opposite direction.
                    //X hit Y if X has a higher velocity or X velocity is the opposite of Y velocity

                    if (fracD_a != null)
                    {
                        //if rbA caused imp, use self
                        if (rbA_causedImp == true) rbForceVel = FractureHelperFunc.GetRelativeVelocity(rbA_vel * Mathf.Clamp01(norrDiffA + FracGlobalSettings.normalInfluenceReductionSelf),
                            rbB_vel * Mathf.Clamp01(norrDiffB + FracGlobalSettings.normalInfluenceReductionSelf));
                        else rbForceVel = FractureHelperFunc.GetRelativeVelocity(rbA_vel * Mathf.Clamp01(norrDiffA + FracGlobalSettings.normalInfluenceReduction),
                            rbB_vel * Mathf.Clamp01(norrDiffB + FracGlobalSettings.normalInfluenceReduction));
                        
                        float rbA_forceApplied = Mathf.Min(
                            GuessMaxForceApply(rbForceVel, null, rbI_a, impBouncyness, fracD_a.fracThis.allParents[fracD_a.fracThis.jCDW_job.partsParentI[fracD_a.partIndex]].parentKinematic > 0),
                            GuessMaxForceApply(rbForceVel, fracD_b, rbI_b, impBouncyness, false));
                        //float rbA_forceApplied = GuessMaxForceApply(rbForceVel, fracD_b, rbI_b, impBouncyness, false);

                        CalcImpContact(fracD_a, rbA_causedImp == true ? -rbA_vel : rbB_vel, rbA_forceApplied, rbI_b, 0);
                    }

                    if (fracD_b != null)
                    {
                        //if rbA caused imp, use self
                        if (rbB_causedImp == true) rbForceVel = FractureHelperFunc.GetRelativeVelocity(rbA_vel * Mathf.Clamp01(norrDiffA + FracGlobalSettings.normalInfluenceReductionSelf),
                            rbB_vel * Mathf.Clamp01(norrDiffB + FracGlobalSettings.normalInfluenceReductionSelf));
                        else rbForceVel = FractureHelperFunc.GetRelativeVelocity(rbA_vel * Mathf.Clamp01(norrDiffA + FracGlobalSettings.normalInfluenceReduction),
                            rbB_vel * Mathf.Clamp01(norrDiffB + FracGlobalSettings.normalInfluenceReduction));

                        float rbB_forceApplied = Mathf.Min(
                            GuessMaxForceApply(rbForceVel, null, rbI_b, impBouncyness, fracD_b.fracThis.allParents[fracD_b.fracThis.jCDW_job.partsParentI[fracD_b.partIndex]].parentKinematic > 0),
                            GuessMaxForceApply(rbForceVel, fracD_a, rbI_a, impBouncyness, false));
                        //float rbB_forceApplied = GuessMaxForceApply(rbForceVel, fracD_a, rbI_a, impBouncyness, false);

                        CalcImpContact(fracD_b, rbB_causedImp == true ? -rbB_vel : rbA_vel, rbB_forceApplied, rbI_a, 1);
                    }
                }
            }

            Vector3 CalcRigidbodyVel(int bodyId, out int rbI, out float norDiff)
            {
                Vector3 rbVel;

                if (rbInstancIdToJgrvIndex.TryGetValue(bodyId, out rbI) == true)
                {
                    rbVel = FractureHelperFunc.GetObjectVelocityAtPoint(
                            jGRV_job.rb_posData[rbI].rbWToLPrev,
                            jGRV_job.rb_posData[rbI].rbLToWNow,
                            impPos, fixedDeltatime
                            );

                    //Normals are bad for fast moving objects (Unless using continues collision) This is because collision does
                    //not happen until obj is inside frac causing it to use normals from the inside.
                    //Dont think it will cause and significant issues and its not really possible to fix??
                    norDiff = Vector3.Dot(impNormal, rbVel.normalized);
                    if (norDiff < 0.0f) norDiff *= -1.0f;//reverse normal since it is impossible for X to move forward and hit a wall that is pointing in the same dir
                    
                    Debug.DrawLine(impPos, impPos + impNormal, Color.blue, 0.5f);
                    norDiff = Mathf.Clamp01(norDiff + (impFriction * norDiff));
                }
                else
                {
                    rbI = -1;
                    norDiff = 0.0f;
                    rbVel = Vector3.zero;
                }

                return rbVel;
            }

            void CalcImpContact(GlobalFracData fracD, Vector3 impactVel, float forceApplied, int otherRbI, byte idOffset)
            {
                //Ignore impact if too weak
                if (forceApplied < FracGlobalSettings.minimumImpactForce) return;

                //Potential issue cause, feels like they can get mixed when two destructable objects collide
                ////return if no parent
                //if (fracD.fracThis.jCDW_job.partsParentI[fracD.partIndex] < 0) return;

                //get or create impPair
                int thisImpId = pair.bodyInstanceID + pair.otherBodyInstanceID + idOffset;
                if (impIdsToIgnore.ContainsKey(thisImpId) == true) return;//return if imp should be ignored

                Debug.DrawLine(impPos, impPos + impactVel, Color.red, 0.1f);
                if (impIdToImpPair.TryGetValue(thisImpId, out ImpPair impPair) == false)
                {
                    impPair = new()
                    {
                        impForceTotal = 0.0f,
                        impFrac = fracD.fracThis,
                        impPoints = new(),
                        impPairsI = new(),
                        impVel = Vector3.zero,
                        sourceRb = otherRbI < 0 ? null : jGRV_rb_mass[otherRbI].rb
                    };
                }

                if (impPair.impVel.sqrMagnitude < impactVel.sqrMagnitude) impPair.impVel = impactVel;
                impPair.impPoints.Add(new() { partI = fracD.partIndex, force = forceApplied } );//partIndex some times does not have a parent, wtf??
                impPair.impPairsI.Add(pairI);
                impPair.impForceTotal += forceApplied;

                impIdToImpPair[thisImpId] = impPair;
            }

            float GuessMaxForceConsume(Vector3 forceVel, GlobalFracData fracD_hit, int rbI_hit, float bouncyness)
            {
                if (fracD_hit != null) return fracD_hit.fracThis.GuessMaxForceConsume(forceVel, fracD_hit.partIndex, bouncyness);

                //if the opposite object is not destructable it has infinit stenght
                if (rbI_hit < 0 || jGRV_rb_mass[rbI_hit].mass < 0.0f) return float.MaxValue;

                float forceConsume = forceVel.magnitude * jGRV_rb_mass[rbI_hit].mass;
                return forceConsume + (forceConsume * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption);
            }

            float GuessMaxForceApply(Vector3 forceVel, GlobalFracData fracD_hit, int rbI_hit, float bouncyness, bool rbIsKinematic = false)
            {
                if (fracD_hit != null) return fracD_hit.fracThis.GuessMaxForceApplied(forceVel, fracD_hit.partIndex, bouncyness);

                //if the opposite object is not destructable it has infinit stenght
                if (rbI_hit < 0 || rbIsKinematic == true) return float.MaxValue;
                float forceConsume = forceVel.magnitude * Mathf.Abs(jGRV_rb_mass[rbI_hit].mass);
                return forceConsume - (forceConsume * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption);
            }
        }
        #endregion CollisionHandling

        #region EditorMemoryCleanup
#if UNITY_EDITOR
        [InitializeOnLoad]
        public class EditorCleanup : UnityEditor.AssetModificationProcessor
        {
            static EditorCleanup()
            {
                CompilationPipeline.compilationFinished += OnCompilationFinished;
                UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            }

            private static void OnCompilationFinished(object obj)
            {
                OnMemoryClear();
            }

            private static void OnPlayModeStateChanged(PlayModeStateChange state)
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                {
                    OnMemoryClear();
                }
            }

            private static void OnMemoryClear()
            {
                foreach (FractureThis frac in GameObject.FindObjectsOfType<FractureThis>(true))
                {
                    frac.eOnly_ignoreNextDraw = true;
                    frac.ClearUsedGpuAndCpuMemory();
                }
            }
        }

        //debug stopwatch
        private System.Diagnostics.Stopwatch stopwatch = new();

        public void Debug_toggleTimer(string note = "")
        {
            if (stopwatch.IsRunning == false)
            {
                stopwatch.Restart();
            }
            else
            {
                stopwatch.Stop();
                Debug.Log(note + " time: " + stopwatch.Elapsed.TotalMilliseconds + "ms");
            }
        }

        //if been cloned
        private Dictionary<int, FractureThis> eOnly_beenClonedStatus = new();

        /// <summary>
        /// Returns true if the given object has been cloned (Editor only)
        /// </summary>
        public bool Eonly_HasFracBeenCloned(FractureThis frac, bool saved = false)
        {
            if (saved == true)
            {
                eOnly_beenClonedStatus[frac.saveAsset.GetInstanceID()] = frac;
                return true;
            }

            if (frac.saved_fracId < 0) return false;
            int id = frac.saveAsset.GetInstanceID();
            if (frac.GetFracturePrefabType() == 0 && eOnly_beenClonedStatus.TryGetValue(id, out FractureThis oFrac) == true && oFrac != frac && oFrac != null) return true;
            eOnly_beenClonedStatus[id] = frac;
            return false;
        }
#endif
        #endregion EditorMemoryCleanup
    }
}
