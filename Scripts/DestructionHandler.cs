using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
#endif
using UnityEngine;
using UnityEngine.Jobs;

namespace zombDestruction
{
    [DefaultExecutionOrder(-10)]
    public class DestructionHandler : MonoBehaviour
    {
        #region InstanceIds
        private ConcurrentDictionary<int, GlobalFracData> partColsInstanceId = new();

        private class GlobalRbDataToSet
        {
            public GlobalRbData rbData;

            /// <summary>
            /// 0 = remove, 1 = add+reset, 2 = add+update
            /// </summary>
            public byte updateStatus;
        }

        [System.Serializable]
        public class GlobalFracData
        {
            public DestructableObject fracThis;
            public short partIndex;
        }

        private void Awake()
        {
            //setup rb job
            GetRbVelocities_setup();

            //verify global handlers
            //If more than one global handler exists merge other with this one
            DestructionHandler[] handlers = GameObject.FindObjectsByType<DestructionHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < handlers.Length; i += 1)
            {
                if (handlers[i] == this) continue;
                handlers[i].GetRbVelocities_end();//make sure the job aint running

#pragma warning disable CS0162 // Unreachable code detected
                if (FracGlobalSettings.canAutomaticallyRemoveAddedRigidbodies == false) partColsInstanceId.AddRange(handlers[i].partColsInstanceId);
                else foreach (var idFrac in handlers[i].partColsInstanceId)
                     {
                         if (idFrac.Value.fracThis == null) continue;
                         idFrac.Value.fracThis.globalHandler = this;
                         partColsInstanceId.TryAdd(idFrac.Key, idFrac.Value);
                     }
#pragma warning restore CS0162 // Unreachable code detected

                int rbCount = handlers[i].jGRV_rbData.Count;
                for (int rI = 0; rI < rbCount; rI++)
                {
                    if (handlers[i].jGRV_rbData[rI].rb == null) continue;

                    OnAddOrUpdateRb(handlers[i].jGRV_rbData[rI]);
                }

                foreach (var pair in handlers[i].jGRV_rbToSet)
                {
                    jGRV_rbToSet.Add(pair.Key, pair.Value);
                }

                Destroy(handlers[i]);
            }

            //Get ogFixedTimeStep
            if (handlers.Length == 0) ogFixedTimeStep = Time.fixedDeltaTime;
            else ogFixedTimeStep = handlers[0].ogFixedTimeStep;
        }

        private void Start()
        {
            //When a new scene is loaded old rigidbodies and fracs can often be null
#pragma warning disable CS0162 // Unreachable code detected
            if (FracGlobalSettings.canAutomaticallyRemoveAddedRigidbodies == true) RemoveNullRigidbodies();
#pragma warning restore CS0162 // Unreachable code detected
        }

        public void OnAddFracPart(DestructableObject frac, short partI)
        {
            partColsInstanceId.TryAdd(frac.allPartsCol[partI].GetInstanceID(), new()
            {
                fracThis = frac,
                partIndex = partI
            });
        }

        /// <summary>
        /// Should be called the same frame you destroy a destructable object
        /// </summary>
        public void OnDestroyFracture(DestructableObject destroyedFrac)
        {
            for (int i = 0; i < destroyedFrac.allParts.Count; i++)
            {
                partColsInstanceId.TryRemove(destroyedFrac.allPartsCol[i].GetInstanceID(), out _);
            }
        }

        /// <summary>
        /// Call to remove a rigidbody that you added with OnAddOrUpdateRb()
        /// </summary>
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
        /// call if you have teleported the rigidbody or created new rb. Mass must be > 0.0f
        /// </summary>
        /// <param name="desMass">The mass the rigidbody should have for the destruction system</param>
        /// <param name="rbMass">The mass the rigidbody actually has, should always be equal to rbToAddOrReset.mass</param>
        public void OnAddOrResetRb(GlobalRbData rbData)
        {
            int rbId = rbData.rb.GetInstanceID();
            if (rbData.rb.isKinematic == true)
            {
                rbData.desMass *= -1;
                rbData.rbMass *= -1;
            }

            if (jGRV_rbToSet.TryAdd(rbId, new()
            {
                rbData = rbData,
                updateStatus = 1
            }) == false)
            {
                if (jGRV_rbToSet[rbId].updateStatus == 2) jGRV_rbToSet[rbId].updateStatus = 1;
                jGRV_rbToSet[rbId].rbData = rbData;
            }
        }

        /// <summary>
        /// Makes sure the rigidbody is added to the destruction system and updates its properties,
        /// call if you have modified the rigidbody mass or created new rb. Mass must be > 0.0f
        /// </summary>
        /// <param name="desMass">The mass the rigidbody should have for the destruction system</param>
        /// <param name="rbMass">The mass the rigidbody actually has, should always be equal to rbToAddOrUpdate.mass</param>
        public void OnAddOrUpdateRb(GlobalRbData rbData, bool onlyUpdateMass = false)
        {
            int rbId = rbData.rb.GetInstanceID();
            if (rbData.rb.isKinematic == true)
            {
                rbData.desMass *= -1;
                rbData.rbMass *= -1;
            }

            if (jGRV_rbToSet.TryAdd(rbId, new()
            {
                rbData = rbData,
                updateStatus = 2
            }) == false)
            {
                if (onlyUpdateMass == true)
                {
                    float ogDesMass = rbData.desMass;
                    float ogRbMass = rbData.rbMass;
                    rbData = jGRV_rbData[rbId].ShallowCopy();
                    rbData.desMass = ogDesMass;
                    rbData.rbMass = ogRbMass;
                }

                jGRV_rbToSet[rbId].rbData = rbData;
            }
        }

        /// <summary>
        /// Call to remove all rigidbodies that has been destroyed
        /// </summary>
        public void RemoveNullRigidbodies()
        {
            //Remove rigidbodies
            foreach (var rbIdIndex in rbInstancIdToJgrvIndex)
            {
                if (jGRV_rbData[rbIdIndex.Value].rb != null) continue;

                if (jGRV_rbToSet.TryAdd(rbIdIndex.Key, new() { updateStatus = 0 }) == false)
                    jGRV_rbToSet[rbIdIndex.Key].updateStatus = 0;
            }

            //Remove fractures
            foreach (var idFrac in partColsInstanceId)
            {
                if (idFrac.Value.fracThis != null) continue;

                partColsInstanceId.TryRemove(idFrac.Key, out _);
            }
        }

        #endregion InstanceIds




        #region MainUpdate and Editor
        [Header("Global Settings")]
        [Tooltip("The layers raycasts should hit")] public LayerMask groundLayers = Physics.DefaultRaycastLayers;

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
            public FracSaveAsset saveAsset;
            public DestructableObject fracThis;
        }

#endif

        /// <summary>
        /// Creates a temporary saveAsset and assigns it to createFor (The asset will be destroyed automatically if createFor is no longer using it) Editor only
        /// </summary>
        /// <param name="createFor"></param>
        /// <returns>False if unable to create a new temp saveAsset</returns>
        public bool TryCreateTempSaveAsset(DestructableObject createFor)
        {
#if UNITY_EDITOR
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
            FracSaveAsset fracSaveAsset = ScriptableObject.CreateInstance<FracSaveAsset>();
            AssetDatabase.CreateAsset(fracSaveAsset, folderPath);
            tempSaveAssets.Add(new() { saveAsset = fracSaveAsset, fracThis = createFor });
            createFor.saveAsset = fracSaveAsset;

            return true;
#else
            return false;
#endif
        }

        private float syncTime = 0.0f;
        private int syncFrames = 0;
        private float ogFixedTimeStep;

        private void Update()
        {
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
        private Dictionary<int, GlobalRbDataToSet> jGRV_rbToSet = new();
        private TransformAccessArray jGRV_rbTrans;
        /// <summary>
        /// Contains data about the rigidbody mass and its customProperties
        /// </summary>
        private List<GlobalRbData> jGRV_rbData;
        private JobHandle jGRV_handle;
        public GetRbVelocities_work jGRV_job;
        public Dictionary<int, int> rbInstancIdToJgrvIndex = new();
        private bool jGRV_jobIsActive = false;

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
            jGRV_rbData = new();
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
                    //Get rbToSet
                    var rbToSet = jGRV_rbToSet[rbId];
                    byte uStatus = rbToSet.updateStatus;
                    Transform rbTrans;

                    //Remove if rbToSet has been destoyed 
                    if (uStatus > 0)
                    {
                        if (rbToSet.rbData.rb == null)
                        {
                            rbTrans = null;
                            uStatus = 0;
                        }
                        else rbTrans = rbToSet.rbData.rb.transform;
                    }
                    else rbTrans = null;

                    if (rbInstancIdToJgrvIndex.TryGetValue(rbId, out int jIndex) == true)
                    {
                        if (uStatus > 0)
                        {
                            //update and maybe reset
                            jGRV_rbData[jIndex] = rbToSet.rbData;

                            if (uStatus == 2) continue;

                            var posD = jGRV_job.rb_posData[jIndex];
                            posD.rbLToWNow = rbTrans.localToWorldMatrix;
                            posD.rbWToLPrev = rbTrans.worldToLocalMatrix;
                            jGRV_job.rb_posData[jIndex] = posD;
                        }
                        else
                        {
                            //remove
                            jGRV_rbTrans.RemoveAtSwapBack(jIndex);
                            jGRV_rbData.RemoveAtSwapBack(jIndex);
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

                        jGRV_rbData.Add(rbToSet.rbData);

                        jGRV_job.rb_posData.Add(new()
                        {
                            rbLToWNow = rbTrans.localToWorldMatrix,
                            rbWToLPrev = rbTrans.worldToLocalMatrix
                        });

                        rbInstancIdToJgrvIndex.Add(rbId, rbInstancIdToJgrvIndex.Count);
                    }
                }

                jGRV_rbToSet.Clear();
            }
        }

        [BurstCompile]
        public struct GetRbVelocities_work : IJobParallelForTransform
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

                posD.rbWToLPrev = posD.rbLToWNow.inverse;
                posD.rbLToWNow = transform.localToWorldMatrix;
                
                rb_posData[index] = posD;
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
            //Get fixed deltaTime
            fixedDeltatime = Time.fixedDeltaTime;

            //Update ignored ids
            if (impIdsToIgnore.Count > 0)
            {
                //This feels extremely overcomplicated and slow, but everything else I have tried causes collection was modified error
                //Get keys to update or remove
                HashSet<int> keysToRemove = new();
                List<int> keysToUpdate = new();
                List<IgnoredImpIdData> updatedData = new();

                foreach (var kvp in impIdsToIgnore)
                {
                    var impId = kvp.Key;
                    var ignoreData = kvp.Value;

                    if (ignoreData.timeLeft <= 0.0f)
                    {
                        keysToRemove.Add(impId);
                    }
                    else
                    {
                        ignoreData.timeLeft -= fixedDeltatime;
                        keysToUpdate.Add(impId);
                        updatedData.Add(ignoreData);
                    }
                }

                //Remove keys
                foreach (var key in keysToRemove)
                {
                    impIdsToIgnore.Remove(key);
                }

                //Update keys
                for (int i = 0; i < keysToUpdate.Count; i++)
                {
                    impIdsToIgnore[keysToUpdate[i]] = updatedData[i];
                }
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
        private Dictionary<int, IgnoredImpIdData> impIdsToIgnore = new();
        private readonly object toIgnoreLock = new();

        private class IgnoredImpIdData
        {
            public float timeLeft;
            public Vector3 velDir;
        }

        private class ImpPair
        {
            public float impForceTotal;
            public float sourceTransCapTotal;

            /// <summary>
            /// The velocity of the collision
            /// </summary>
            public Vector3 impVel;

            /// <summary>
            /// The destructable object that was hit
            /// </summary>
            public DestructableObject impFrac;

            /// <summary>
            /// Contains all parts that was hit and the force applied to each of them
            /// </summary>
            public List<DestructionPoint> impPoints;

            /// <summary>
            /// Contains all part indexes that was hit
            /// </summary>
            public HashSet<int> impPartIndexs;

            /// <summary>
            /// The difference between desMass and rb mass (thisRbData.rbMass / thisRbData.desMass)
            /// </summary>
            public float thisRbDesMassDiff;

            /// <summary>
            /// The jgrvIndex of the rigidbody we collided with (The rigidbody on the destructable object)
            /// </summary>
            public int thisRbI;

            /// <summary>
            /// impPairsI[X] is the contactPair index impPoints[X] was created from
            /// </summary>
            public List<int> impPairsI;

            /// <summary>
            /// Contains all contactPair indexes used to create this impPair
            /// </summary>
            public HashSet<int> impPairsIndexes;
        }

        public delegate void Event_OnDestructionImpact(ref List<DestructionPoint> impactPoints, ref float totalImpactForce, ref Vector3 impactVelocity, int hitRbJgrvIndex);
        
        /// <summary>
        /// You should only subscribe/unsubscribe to this event in the Awake/Start/Enable/Destroy/Disable methods.
        /// The event can be invoked from any thread.
        /// Its recommended that you add the data you need later to a concurrentQueue and read it in the next Update()
        /// </summary>
        public event Event_OnDestructionImpact OnDestructionImpact;

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
            Vector3 impPos;
            Vector3[] impPoss;
#pragma warning disable CS0162
            if (FracGlobalSettings.canGetImpactNormalFromPlane == true) impPoss = new Vector3[3];
#pragma warning restore CS0162

            float impFriction;
            float impBouncyness;
            NativeArray<int> tempInputIds = new(3, Allocator.Temp);

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
                float maxImpF = float.MinValue;
                int maxImpI = 0;
                int impCount = iPair.impPoints.Count;

                HashSet<int> debug = new();

                for (int i = 0; i < impCount; i++)
                {
                    //get highest impact force
                    var impP = iPair.impPoints[i];

                    if (impP.force > maxImpF)
                    {
                        maxImpF = impP.force;
                        maxImpI = i;
                    }

                    //Normlize impact force
                    impP.force /= iPair.impForceTotal;

                    if (debug.Add(impP.partI) == false)
                    {
                        Debug.LogError("double " + impP.partI);
                    }

                    iPair.impPoints[i] = impP;

                }

                if (iPair.sourceTransCapTotal > 0.0f && maxImpF > iPair.sourceTransCapTotal) maxImpF = iPair.sourceTransCapTotal;

                //Get top 10% impact
                float neededForce = maxImpF * 0.9f;
                float totTopForce = 0.0f;
                int totTopCount = 0;

                for (int i = 0; i < impCount; i++)
                {
                    var desP = iPair.impPoints[i];
                    desP.force *= maxImpF;

                    if (desP.force > neededForce)
                    {
                        totTopForce += desP.force;
                        totTopCount++;
                    }

                    iPair.impPoints[i] = desP;
                }

                //Multiply normlized impact forces with highest impact force, Because we want more impact points to result in less force at each impact point
                bool somethingLikelyBreaks = false;
                float transCap = float.MaxValue;

                for (int i = 0; i < impCount; i++)
                {
                    var desP = iPair.impPoints[i];

                    //Ignore collisions if part most likely will break
                    if (somethingLikelyBreaks == true) continue;

                    var tempPair = pairs[iPair.impPairsI[i]];
               
                    if (iPair.impFrac.GuessIfForceCauseBreaking(totTopForce * Mathf.Clamp01((desP.force / totTopForce) * Mathf.Max(1.0f, totTopCount / 2.0f))
                        , desP.partI, out float thisTransCap, tempPair.GetBounciness(0)) == true)
                    {
                        somethingLikelyBreaks = true;

                        foreach (int pI in iPair.impPairsIndexes)
                        {
                            for (int conI = 0; conI < pairs[pI].contactCount; conI++)
                            {
                                pairs[pI].SetMaxImpulse(conI, 0.0f);
                            }
                        }
                    }

                    if (thisTransCap < transCap) transCap = thisTransCap;
                }

                //if no impact is likely to cause breaking, mark contacts between source and frac to be ignored the next few physics frames
                if (somethingLikelyBreaks == false)
                {
                    //Add to ids to ignore
                    lock (toIgnoreLock)
                    {
                        impIdsToIgnore.TryAdd(impId, new() { timeLeft = 0.1f, velDir = iPair.impVel.normalized });
                    }

                    transCap *= iPair.thisRbDesMassDiff;
                    transCap /= iPair.impPairsIndexes.Count;
                    transCap /= Mathf.Max(1.0f, iPair.impPoints.Count / 2.0f);

                    foreach (int pI in iPair.impPairsIndexes)
                    {
                        float maxImpulse = transCap / pairs[pI].contactCount;
                        if (pairs[pI].GetMaxImpulse(0) < maxImpulse) continue;

                        for (int conI = 0; conI < pairs[pI].contactCount; conI++)
                        {
                            pairs[pI].SetMaxImpulse(conI, maxImpulse);
                        }
                    }
                }

                //Invoke on destruction impact event
                OnDestructionImpact?.Invoke(ref iPair.impPoints, ref maxImpF, ref iPair.impVel, iPair.thisRbI);

                //notify destructable object about impact
                iPair.impFrac.RegisterDestruction(new()
                {
                    impForceTotal = maxImpF,
                    impVel = iPair.impVel,
                    parentI = iPair.impFrac.allPartsParentI[iPair.impPoints[maxImpI].partI],
                }, iPair.impPoints.ToNativeArray(Allocator.Persistent), iPair.thisRbI, impId, false);
            }

            void CalcImpPair()
            {
                //get the destructable object we hit, or return if we did not hit one
                if (partColsInstanceId.TryGetValue(pair.colliderInstanceID, out GlobalFracData fracD_a) == true
                    && fracD_a.fracThis.allPartsParentI[fracD_a.partIndex] < 0)
                {
                    if (rbInstancIdToJgrvIndex.TryGetValue(pair.bodyInstanceID, out _) == false) return;
                    fracD_a = null;
                }

                if (partColsInstanceId.TryGetValue(pair.otherColliderInstanceID, out GlobalFracData fracD_b) == true
                    && fracD_b.fracThis.allPartsParentI[fracD_b.partIndex] < 0)
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
                        if (rbA_causedImp == true) rbForceVel = FracHelpFunc.GetRelativeVelocity(rbA_vel * Mathf.Clamp01(norrDiffA + FracGlobalSettings.normalInfluenceReductionSelf),
                            rbB_vel * Mathf.Clamp01(norrDiffB + FracGlobalSettings.normalInfluenceReductionSelf));
                        else rbForceVel = FracHelpFunc.GetRelativeVelocity(rbA_vel * Mathf.Clamp01(norrDiffA + FracGlobalSettings.normalInfluenceReduction),
                            rbB_vel * Mathf.Clamp01(norrDiffB + FracGlobalSettings.normalInfluenceReduction));

                        float rbA_forceApplied = Mathf.Min(
                            GuessMaxForceApply(rbForceVel, null, rbI_a, impBouncyness, out _, fracD_a.fracThis.allParents[fracD_a.fracThis.allPartsParentI[fracD_a.partIndex]].parentKinematic > 0),
                            GuessMaxForceApply(rbForceVel, fracD_b, rbI_b, impBouncyness, out float transCap, false));
                        //float rbA_forceApplied = GuessMaxForceApply(rbForceVel, fracD_b, rbI_b, impBouncyness, false);

                        CalcImpContact(fracD_a, rbA_causedImp == true ? -rbA_vel : rbB_vel, rbA_forceApplied, rbI_b, transCap, rbI_a);
                    }

                    if (fracD_b != null)
                    {
                        //if rbA caused imp, use self
                        if (rbB_causedImp == true) rbForceVel = FracHelpFunc.GetRelativeVelocity(rbA_vel * Mathf.Clamp01(norrDiffA + FracGlobalSettings.normalInfluenceReductionSelf),
                            rbB_vel * Mathf.Clamp01(norrDiffB + FracGlobalSettings.normalInfluenceReductionSelf));
                        else rbForceVel = FracHelpFunc.GetRelativeVelocity(rbA_vel * Mathf.Clamp01(norrDiffA + FracGlobalSettings.normalInfluenceReduction),
                            rbB_vel * Mathf.Clamp01(norrDiffB + FracGlobalSettings.normalInfluenceReduction));

                        float rbB_forceApplied = Mathf.Min(
                            GuessMaxForceApply(rbForceVel, null, rbI_b, impBouncyness, out _, fracD_b.fracThis.allParents[fracD_b.fracThis.allPartsParentI[fracD_b.partIndex]].parentKinematic > 0),
                            GuessMaxForceApply(rbForceVel, fracD_a, rbI_a, impBouncyness, out float transCap, false));

                        CalcImpContact(fracD_b, rbB_causedImp == true ? -rbB_vel : rbA_vel, rbB_forceApplied, rbI_a, transCap, rbI_b);
                    }
                }
            }

            Vector3 CalcRigidbodyVel(int bodyId, out int rbI, out float norDiff)
            {
                Vector3 rbVel;

                if (rbInstancIdToJgrvIndex.TryGetValue(bodyId, out rbI) == true)
                {
                    var rbWToLPrev = jGRV_job.rb_posData[rbI].rbWToLPrev;
                    var rbWToLNow = jGRV_job.rb_posData[rbI].rbLToWNow;

                    rbVel = FracHelpFunc.GetObjectVelocityAtPoint(
                            ref rbWToLPrev,
                            ref rbWToLNow,
                            ref impPos, fixedDeltatime
                            );

                    //Normals are bad for fast moving objects (Unless using continues collision) This is because collision does
                    //not happen until obj is inside frac causing it to use normals from the inside.
                    //Dont think it will cause and significant issues and its not really possible to fix??
                    norDiff = Vector3.Dot(impNormal, rbVel.normalized);
                    if (norDiff < 0.0f) norDiff *= -1.0f;//reverse normal since it is impossible for X to move forward and hit a wall that is pointing in the same dir

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

            void CalcImpContact(GlobalFracData fracD, Vector3 impactVel, float forceApplied, int otherRbI, float sourceTransCap, int thisRbI)
            {
                //Ignore impact if too weak
                if (forceApplied < FracGlobalSettings.minimumImpactForce) return;

                //Get impact id
                tempInputIds[0] = pair.bodyInstanceID;
                tempInputIds[1] = pair.otherBodyInstanceID;
                tempInputIds[2] = fracD.fracThis.fracInstanceId;
                int thisImpId = FracHelpFuncBurst.GetHashFromInts(ref tempInputIds);

                //Get if id is ignored
                lock (toIgnoreLock)
                {
                    if (impIdsToIgnore.TryGetValue(thisImpId, out IgnoredImpIdData ignoreData) == true)
                    {
                        impactVel = ignoreData.velDir * impactVel.magnitude;
                    }
                }
                //if (impIdsToIgnore.ContainsKey(thisImpId) == true) return;//return if imp should be ignored

                //Get impact from dictorary
                if (impIdToImpPair.TryGetValue(thisImpId, out ImpPair impPair) == false)
                {
                    GlobalRbData sourceRbData = otherRbI < 0 ? null : jGRV_rbData[otherRbI];
                    GlobalRbData thisRbData = thisRbI < 0 ? null : jGRV_rbData[thisRbI];

                    impPair = new()
                    {
                        impForceTotal = 0.0f,
                        sourceTransCapTotal = 0.0f,
                        impFrac = fracD.fracThis,
                        impPoints = new(),
                        impPartIndexs = new(),
                        impPairsI = new(),
                        impPairsIndexes = new(),
                        impVel = Vector3.zero,
                        thisRbDesMassDiff = thisRbI < 0 ? 1.0f : (thisRbData.rbMass / thisRbData.desMass),
                        thisRbI = thisRbI
                    };
                }

                //Modify impact data
                if (impPair.impVel.sqrMagnitude < impactVel.sqrMagnitude) impPair.impVel = impactVel;
                if (impPair.impPartIndexs.Add(fracD.partIndex) == true)
                {
                    //New part, just add it
                    impPair.impPoints.Add(new()
                    {
                        partI = fracD.partIndex,
                        force = forceApplied,
                        impPosW = impPos
                    });//partIndex some times does not have a parent, wtf??

                    impPair.impPairsI.Add(pairI);
                    impPair.impForceTotal += forceApplied;
                    impPair.sourceTransCapTotal += sourceTransCap;
                }
                else
                {
                    //Part already exist, combine with new impact
                    int oldPointI = impPair.impPoints.FindIndex(point => point.partI == fracD.partIndex);
                    var oldPoint = impPair.impPoints[oldPointI];

                    oldPoint.impPosW = (oldPoint.impPosW + impPos) / 2.0f;
                    if (oldPoint.force < forceApplied)
                    {
                        impPair.impForceTotal += forceApplied - oldPoint.force;
                        oldPoint.force = forceApplied;
                    }

                    impPair.impPoints[oldPointI] = oldPoint;
                }

                impPair.impPairsIndexes.Add(pairI);
                impIdToImpPair[thisImpId] = impPair;
            }

            float GuessMaxForceApply(Vector3 forceVel, GlobalFracData fracD_hit, int rbI_hit, float bouncyness, out float transCap, bool rbIsKinematic = false)
            {
                if (fracD_hit != null) return fracD_hit.fracThis.GuessMaxForceApply(forceVel, fracD_hit.partIndex, out transCap, bouncyness);

                //if the opposite object is not destructable it has infinit stenght
                transCap = 0.0f;

                if (rbI_hit < 0 || rbIsKinematic == true) return float.MaxValue;
                float forceConsume = forceVel.magnitude * Mathf.Abs(jGRV_rbData[rbI_hit].desMass);
                return forceConsume - (forceConsume * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption);
            }
        }
        #endregion CollisionHandling





        #region UserApiFunctions

        /// <summary>
        /// Applies force as destruction to the given collider, returns false if the collider aint breakable
        /// (You may wanna add force directly to the collider rigidbody if you want it to move)
        /// </summary>
        /// <param name="col">The collider to apply force to</param>
        /// <param name="impactPoint"></param>
        /// <param name="impactForce">The force applied to the collider</param>
        /// <param name="velocity">The target velocity for the collider</param>
        /// <param name="treatAsExplosion">If true, force will be more like a chock wave originating from impactPoint</param>
        public bool RegisterImpact(Collider col, Vector3 impactPoint, float impactForce, Vector3 velocity, bool treatAsExplosion = false)
        {
            var fracD = TryGetFracPartFromColInstanceId(col.GetInstanceID());
            if (fracD == null) return false;

            NativeArray<DestructionPoint> impPoints = new(1, Allocator.Persistent);
            impPoints[0] = new()
            { 
                force = impactForce,
                impPosW = impactPoint,
                partI = fracD.partIndex
            };

            if (rbInstancIdToJgrvIndex.TryGetValue(col.attachedRigidbody.GetInstanceID(), out int thisRbI) == false) thisRbI = -1;

            fracD.fracThis.RegisterDestruction(new()
            {
                centerImpPos = impactPoint,
                impForceTotal = impactForce,
                impVel = velocity,
                isExplosion = treatAsExplosion,
                parentI = fracD.fracThis.allPartsParentI[fracD.partIndex],
            }, impPoints, thisRbI, 0, false);

            return true;
        }

        /// <summary>
        /// Applies force as destruction to the given collider, returns false if the collider aint breakable
        /// (You may wanna add force directly to the collider rigidbody if you want it to move)
        /// This function can be called from any thread
        /// </summary>
        /// <param name="fracD">Use TryGetFracPartFromColInstanceId() to get fracData from collider</param>
        /// <param name="jGrvIndex">Use rbInstancIdToJgrvIndex.TryGetValue() to get jGrvIndex from rigidbody</param>
        /// <param name="impactPoint"></param>
        /// <param name="impactForce">The force applied to the collider</param>
        /// <param name="velocity">The target velocity for the collider</param>
        /// <param name="treatAsExplosion">If true, force will be more like a chock wave originating from impactCenter</param>
        public bool RegisterImpact(ref GlobalFracData fracD, int jGrvIndex, ref Vector3 impactPoint, float impactForce, ref Vector3 velocity, ref Vector3 impactCenter, bool treatAsExplosion = false)
        {
            if (fracD == null) return false;

            NativeArray<DestructionPoint> impPoints = new(1, Allocator.Persistent);
            impPoints[0] = new()
            {
                force = impactForce,
                impPosW = impactPoint,
                partI = fracD.partIndex
            };

            fracD.fracThis.RegisterDestruction(new()
            {
                centerImpPos = impactCenter,
                impForceTotal = impactForce,
                impVel = velocity,
                isExplosion = treatAsExplosion,
                parentI = fracD.fracThis.allPartsParentI[fracD.partIndex],
            }, impPoints, jGrvIndex, 0, false);

            return true;
        }

        private class RegImpactDic_value
        {
            public HashSet<int> listIndexes;
            public DestructableObject frac;
        }

        private class RegImpactDic_key
        {
            public DestructableObject frac;
            public int parentIndex;

            public override bool Equals(object obj)
            {
                if (obj is RegImpactDic_key otherKey)
                {
                    return frac == otherKey.frac && parentIndex == otherKey.parentIndex;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(frac, parentIndex);
            }
        }

        /// <summary>
        /// Applies force as destruction at all impactPoints
        /// (You may wanna add force directly to the collider rigidbody if you want it to move)
        /// </summary>
        /// <param name="fracDatas">All parts to apply force to</param>
        /// <param name="impactPoints">The position on each collider the force orginate from (Must have same lenght as fracDatas)</param>
        /// <param name="impactForce">The force to apply at each impactPoint, you may wanna devide it with impactPoints.count</param>
        /// <param name="impactCenter">The center of the explosion, only used if treatAsExplosion is true</param>
        /// <param name="velocity">The target velocity for each collider</param>
        /// <param name="treatAsExplosion">If true, velocity direction will be replaced with the direction from impactCenter to fracPart</param>
        /// <param name="threadSafe">If true, this function can be called from any thread</param>
        public void RegisterImpact(ref List<GlobalFracData> fracDatas, ref List<Vector3> impactPoints, float impactForce, ref Vector3 impactCenter, ref Vector3 velocity, bool treatAsExplosion = false, bool threadSafe = false)
        {
            //Get all different frac parts we hit and sort them by parent & frac
            Dictionary<RegImpactDic_key, RegImpactDic_value> fracToListIndexes = new();

            for (int listI = 0; listI < fracDatas.Count; listI++)
            {
                var fracD = fracDatas[listI];

                RegImpactDic_key newK = new()
                {
                    frac = fracD.fracThis, parentIndex = fracD.fracThis.allPartsParentI[fracD.partIndex]
                };

                if (fracToListIndexes.ContainsKey(newK) == true) fracToListIndexes[newK].listIndexes.Add(listI);
                else fracToListIndexes.Add(newK, new() { frac = fracD.fracThis, listIndexes = new() { listI } });
            }

            //Register destruction
            foreach (var fracKeys in fracToListIndexes)
            {
                //Store the impact positions in nativeArray
                NativeArray<DestructionPoint> impPoints = new(fracKeys.Value.listIndexes.Count, Allocator.Persistent);
                int i = 0;
                int partI = 0;

                foreach (int listI in fracKeys.Value.listIndexes)
                {
                    partI = fracDatas[listI].partIndex;

                    impPoints[i] = new()
                    { 
                        force = impactForce,
                        impPosW = impactPoints[listI],
                        partI = partI
                    };

                    i++;
                }

                int thisRbI = -1;
                if (threadSafe == false)
                {
                    if (rbInstancIdToJgrvIndex.TryGetValue(fracKeys.Key.frac.allPartsCol[partI].attachedRigidbody.GetInstanceID(), out thisRbI) == false) thisRbI = -1;
                }

                fracKeys.Key.frac.RegisterDestruction(new()
                {
                    centerImpPos = impactCenter,
                    impForceTotal = impactForce * impPoints.Length,
                    impVel = velocity,
                    isExplosion = treatAsExplosion,
                    parentI = fracKeys.Key.parentIndex
                }, impPoints, thisRbI, 0, false);
            }
        }

        public void RegisterExplosion(Vector3 explosionPosition, Vector3 groundNormal, float explosionForce, float explosionSpeed, LayerMask hitMask, out RaycastHit[] rayHits, out int hitCount, float rayWidth = 0.5f, float explosionRadius = 5.0f, int resolution = 32)
        {
            List<GlobalFracData> hitFracs = new(4);
            List<Vector3> hitPoints = new(4);
            rayHits = new RaycastHit[resolution];

            hitCount = 0;

            //Get parts inside explosion center
            Collider[] cols = Physics.OverlapSphere(explosionPosition, rayWidth * 1.1f, hitMask, QueryTriggerInteraction.Ignore);
            foreach (Collider col in cols)
            {
                var fracD = TryGetFracPartFromColInstanceId(col.GetInstanceID());
                if (fracD == null) continue;

                hitFracs.Add(fracD);
                hitPoints.Add(explosionPosition);
            }

            var explosionVel = new Vector3(explosionSpeed, 0.0f, 0.0f);
            if (hitFracs.Count > 0)
            {
                RegisterImpact(ref hitFracs, ref hitPoints, explosionForce / hitFracs.Count, ref explosionPosition, ref explosionVel, true, false);
            }

            //Add force too all nearby stuff
            cols = Physics.OverlapSphere(explosionPosition, explosionRadius, hitMask, QueryTriggerInteraction.Ignore);
            foreach (Collider col in cols)
            {
                if (col.attachedRigidbody == null) continue;
                float velMultiply = 1.0f;
                var fracD = TryGetFracPartFromColInstanceId(col.GetInstanceID());
                if (fracD != null)
                {
                    int parentI = fracD.fracThis.allPartsParentI[fracD.partIndex];
                    if (parentI >= 0) velMultiply /= fracD.fracThis.allParents[parentI].partIndexes.Count;
                }

                Vector3 disDir = (col.bounds.center - explosionPosition);

                col.attachedRigidbody.AddForceAtPosition(
                    (1.0f - (disDir.magnitude / explosionRadius)) * explosionSpeed * velMultiply * ((groundNormal + disDir.normalized) / 2.0f), explosionPosition, ForceMode.Impulse);
            }

            //Do grenade fragments
            foreach (var dir in FracHelpFunc.GetSphereDirections(resolution))
            {

                //if (Physics.Raycast(explosionPosition, dir, out rayHits[hitCount], explosionRadius, hitMask, QueryTriggerInteraction.Ignore) == false) continue;
                if (Physics.SphereCast(explosionPosition, rayWidth, dir, out rayHits[hitCount], explosionRadius, hitMask, QueryTriggerInteraction.Ignore) == false) continue;

                //if (rayHits[hitCount].rigidbody != null) rayHits[hitCount].rigidbody.AddForceAtPosition(
                //    0.2f * explosionSpeed * dir, explosionPosition, ForceMode.Impulse);

                var fracD = TryGetFracPartFromColInstanceId(rayHits[hitCount].colliderInstanceID);
                if (fracD == null) continue;
                Debug.DrawLine(explosionPosition, rayHits[hitCount].point, Color.red, 1.0f, true);


                hitFracs.Add(fracD);
                hitPoints.Add(rayHits[hitCount].point);
                hitCount++;
            }

            
            RegisterImpact(ref hitFracs, ref hitPoints, explosionForce / resolution, ref explosionPosition, ref explosionVel, true, false);
        }

        /// <summary>
        /// Returns the given rigidbody customProperties, returns null if rb does not have customProperties
        /// </summary>
        public GlobalRbData TryGetRigidbodyCustomProperties(Rigidbody rb)
        {
            if (rbInstancIdToJgrvIndex.TryGetValue(rb.GetInstanceID(), out int rbIndex) == false) return null;
            return jGRV_rbData[rbIndex];
        }

        /// <summary>
        /// Returns the DestructableObject script and the part index the given collider instanceId is for, null if no part col with instanceId exist
        /// </summary>
        /// <param name="instanceId">The instance id for a collider (Use collider.GetInstanceId())</param>
        public GlobalFracData TryGetFracPartFromColInstanceId(int instanceId)
        {
            if (partColsInstanceId.TryGetValue(instanceId, out GlobalFracData fracD) == true) return fracD;

            return null;
        }

        /// <summary>
        /// Returns a valid DestructionHandler, returns null if no valid DestructionHandler exist in scene
        /// </summary>
        public static DestructionHandler TryGetGlobalHandler(GameObject sourceObj, DestructableObject sourceFrac = null, bool canLogError = true)
        {
            DestructionHandler[] handlers = GameObject.FindObjectsOfType<DestructionHandler>(true);
            if (handlers == null || handlers.Length < 1 || handlers[0].isActiveAndEnabled == false)
            {
                if (canLogError == true) Debug.LogError("There is no active DestructionHandler script in " + sourceObj.scene.name + " (Scene), make sure a active Gameobject has the script attatch to it");
                return null;
            }
            else if (handlers.Length > 1)
            {
                if (canLogError == true) Debug.LogError("There are more than one DestructionHandler script in " + sourceObj.scene.name + " (Scene), please remove all but one and refracture all objects");
                return null;
            }

            if (handlers[0].gameObject.scene != sourceObj.scene)
            {
                int prefabT = sourceFrac != null ? sourceFrac.GetFracturePrefabType() : 0;
                if (prefabT != 2 && canLogError == true) Debug.LogError("The DestructionHandler script must be in the same scene as " + sourceObj.transform.name);
                return prefabT == 2 ? handlers[0] : null;
            }

            return handlers[0];
        }

        #endregion UserApiFunctions





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
                foreach (DestructableObject frac in GameObject.FindObjectsOfType<DestructableObject>(true))
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
        private Dictionary<int, DestructableObject> eOnly_beenClonedStatus = new();

        /// <summary>
        /// Returns true if the given object has been cloned (Editor only)
        /// </summary>
        public bool Eonly_HasFracBeenCloned(DestructableObject frac, bool saved = false)
        {
            if (saved == true)
            {
                eOnly_beenClonedStatus[frac.saveAsset.GetInstanceID()] = frac;
                return true;
            }

            if (frac.saved_fracId < 0) return false;
            int id = frac.saveAsset.GetInstanceID();
            if (frac.GetFracturePrefabType() == 0 && eOnly_beenClonedStatus.TryGetValue(id, out DestructableObject oFrac) == true && oFrac != frac && oFrac != null) return true;
            eOnly_beenClonedStatus[id] = frac;
            return false;
        }
#endif
        #endregion EditorMemoryCleanup
    }
}
