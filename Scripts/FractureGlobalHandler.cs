using g3;
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

namespace Zombie1111_uDestruction
{
    [DefaultExecutionOrder(-10)]
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

                    OnAddOrUpdateRb(rb, handlers[i].jGRV_rb_mass[rI].desMass);//potential issue, if the rb hit something the same frame it wont cause any damage
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
        /// call if you have teleported the rigidbody or created new rb. Mass must be > 0.0f
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
        /// call if you have modified the rigidbody mass or created new rb. Mass must be > 0.0f
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
            public FractureSaveAsset saveAsset;
            public FractureThis fracThis;
        }

#endif

        /// <summary>
        /// Creates a temporary saveAsset and assigns it to createFor (The asset will be destroyed automatically if createFor is no longer using it) Editor only
        /// </summary>
        /// <param name="createFor"></param>
        /// <returns>False if unable to create a new temp saveAsset</returns>
        public bool TryCreateTempSaveAsset(FractureThis createFor)
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
            FractureSaveAsset fracSaveAsset = ScriptableObject.CreateInstance<FractureSaveAsset>();
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
        /// Contains data about the rigidbody mass
        /// </summary>
        private List<GlobalRbData> jGRV_rb_mass;
        private JobHandle jGRV_handle;
        public GetRbVelocities_work jGRV_job;
        public Dictionary<int, int> rbInstancIdToJgrvIndex = new();
        private bool jGRV_jobIsActive = false;

        private class GlobalRbData
        {
            public Rigidbody rb;

            /// <summary>
            /// The mass of the rigidbody as seen by the destruction system
            /// </summary>
            public float desMass;

            /// <summary>
            /// The mass of the actuall rigidbody
            /// </summary>
            public float rbMass;
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
                    var rbToSet = jGRV_rbToSet[rbId];
                    byte uStatus = rbToSet.updateStatus;
                    Transform rbTrans;
                    if (uStatus > 0)
                    {
                        if (rbToSet.rb == null)
                        {
                            rbTrans = null;
                            uStatus = 0;
                        }
                        else rbTrans = rbToSet.rb.transform;
                    }
                    else rbTrans = null;

                    if (rbInstancIdToJgrvIndex.TryGetValue(rbId, out int jIndex) == true)
                    {
                        if (uStatus > 0)
                        {
                            //update or reset
                            bool isKinematic = rbToSet.rb.isKinematic;
                            jGRV_rb_mass[jIndex].desMass = isKinematic == false ? rbToSet.mass : -rbToSet.mass;
                            jGRV_rb_mass[jIndex].rbMass = isKinematic == false ? rbToSet.rb.mass : -rbToSet.rb.mass;
                            var posD = jGRV_job.rb_posData[jIndex];

                            if (uStatus == 2)
                            {
                                jGRV_job.rb_posData[jIndex] = posD;
                                continue;
                            }

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

                        bool isKinematic = rbToSet.rb.isKinematic;
                        jGRV_rb_mass.Add(new()
                        {
                            desMass = isKinematic == false ? rbToSet.mass : -rbToSet.mass,
                            rbMass = isKinematic == false ? rbToSet.rb.mass : -rbToSet.rb.mass,
                            rb = rbToSet.rb
                        });

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

        private class PairData
        {
            public int bodyIdA;
            public ImpObj impA;

            public int bodyIdB;
            public ImpObj impB;
            public List<ImpPoint> impPoints;

            public class ImpObj
            {
                public GlobalFracData fracD;
                public int jGrvIndex;
            }

            public class ImpPoint
            {
                public PairContactData conPairData;
                public int conPairIndex;
                public int partIA;
                public int partIB;
                public Vector3 velA;
                public Vector3 velB;
                public float impulseRb;
                public float impulseDes;
            }
        }

        private class PairContactData
        {
            public Vector3 pos;
            public Vector3 nor;
            public float friction;
            public float bouncyness;
        }

        public void ModificationEvent(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            #region GetDataFromPairs
            //Get all collisions that occured between object A and B
            Dictionary<int, PairData> pairIdToPairData = new();
            int pairCount = pairs.Length;

            for (int pairI = 0; pairI < pairCount; pairI++)
            { 
                var pair = pairs[pairI];

                //Get the destructable object we hit
                var fracDA = TryGetFracPartFromColInstanceId(pair.colliderInstanceID);
                var fracDB = TryGetFracPartFromColInstanceId(pair.otherColliderInstanceID);

                if (fracDA == null && fracDB == null) continue;//None is destructable so just ignore contact

                //Get pairId and pairData, create new pairData if first contact between A and B
                int[] idInput = new int[] { pair.bodyInstanceID, pair.otherBodyInstanceID };
                int pairId = FracHelpFunc.GetHashFromInts(ref idInput);

                if (rbInstancIdToJgrvIndex.TryGetValue(pair.bodyInstanceID, out int jGrvIndexA) == false) jGrvIndexA = -1;
                if (rbInstancIdToJgrvIndex.TryGetValue(pair.otherBodyInstanceID, out int jGrvIndexB) == false) jGrvIndexB = -1;

                if (pairIdToPairData.TryGetValue(pairId, out PairData pairData) == false)
                {
                    pairData = new()
                    {
                        bodyIdA = pair.bodyInstanceID,
                        bodyIdB = pair.otherBodyInstanceID,
                        impPoints = new(),

                        impA = new()
                        {
                            fracD = fracDA,
                            jGrvIndex = jGrvIndexA
                        },

                        impB = new()
                        {
                            fracD = fracDB,
                            jGrvIndex = jGrvIndexB
                        }
                     };

                     pairIdToPairData.Add(pairId, pairData);
                }

                //Get contact data and add it to the impPair
                var conPairData = GetPairContactData(pair);
                Vector3 velA = GetRbPointVel(jGrvIndexA, conPairData.pos); 
                Vector3 velB = GetRbPointVel(jGrvIndexB, conPairData.pos);
                var rbAData = jGrvIndexA < 0 ? null : jGRV_rb_mass[jGrvIndexA];
                var rbBData = jGrvIndexB < 0 ? null : jGRV_rb_mass[jGrvIndexB];

                pairData.impPoints.Add(new()
                {
                    conPairIndex = pairI,
                    conPairData = conPairData,
                    partIA = fracDA != null ? fracDA.partIndex : -1,
                    partIB = fracDB != null ? fracDB.partIndex : -1,
                    velA = velA,
                    velB = velB,
                    impulseDes = GetContactImpulse(conPairData, rbAData != null ? rbAData.desMass : float.PositiveInfinity, rbBData != null ? rbBData.desMass : float.PositiveInfinity, velA, velB),
                    impulseRb = GetContactImpulse(conPairData, rbAData != null ? rbAData.rbMass : float.PositiveInfinity, rbBData != null ? rbBData.rbMass : float.PositiveInfinity, velA, velB)
                });

                pairIdToPairData[pairId] = pairData;
            }

            Vector3 GetRbPointVel(int jGrvIndex, Vector3 point)
            {
                if (jGrvIndex < 0) return Vector3.zero;

                var rbPosData = jGRV_job.rb_posData[jGrvIndex];
                return FracHelpFunc.GetObjectVelocityAtPoint(rbPosData.rbWToLPrev, rbPosData.rbLToWNow, point, fixedDeltatime);
            }

            float GetContactImpulse(PairContactData conPairData, float massA, float massB, Vector3 velA, Vector3 velB)
            {
                Vector3 relativeVel = velB - velA;
                if (massA < 0.0f) massA = float.PositiveInfinity;//If negative mass, the rb is kinematic
                if (massB < 0.0f) massB = float.PositiveInfinity;


                //Calc impulse from direct hit
                float velAlongNor = Vector3.Dot(relativeVel, conPairData.nor);
                float impulse = -(1 + conPairData.bouncyness) * velAlongNor / (1 / massA + 1 / massB);

                //Calc impulse from friction
                Vector3 tangent = relativeVel - (velAlongNor * conPairData.nor);
                if (tangent != Vector3.zero) tangent.Normalize();

                float velAlongTag = Vector3.Dot(relativeVel, tangent);
                impulse += conPairData.friction * -velAlongTag / (1 / massA + 1 / massB);

                return Mathf.Abs(impulse);
            }

            static PairContactData GetPairContactData(ModifiableContactPair pair)
            {
                PairContactData newPD = new();

                //Get normal and position
                if (FracGlobalSettings.canGetImpactNormalFromPlane == true && pair.contactCount > 2)
                {
                    //Get normal from triangel
                    Vector3[] impPoss = new Vector3[3];

                    for (int contI = 0; contI < 3; contI++)
                    {
                        impPoss[contI] = pair.GetPoint(contI);
                    }

                    newPD.nor = Vector3.Cross(impPoss[0] - impPoss[1], impPoss[0] - impPoss[2]).normalized;
                    newPD.pos = (impPoss[0] + impPoss[1] + impPoss[2]) / 3.0f;
                }
                else
                {
                    //Get normal from avg
                    newPD.nor = Vector3.zero;
                    newPD.pos = Vector3.zero;

                    for (int contI = 0; contI < pair.contactCount; contI++)
                    {
                        newPD.nor += -pair.GetNormal(contI);
                        newPD.pos += pair.GetPoint(contI);
                    }

                    newPD.pos /= pair.contactCount;
                    newPD.nor.Normalize();
                }

                //Get phyMat properties
                newPD.friction = pair.GetDynamicFriction(0);
                newPD.bouncyness = pair.GetBounciness(0);

                return newPD;
            }

            #endregion GetDataFromPairs

            #region ComputeDataGotten

            foreach (var idData in pairIdToPairData)
            {
                var pairData = idData.Value;
                int impPointCount = pairData.impPoints.Count;
                float totImpulseDes = 0.0f;
                float totImpulseRb = 0.0f;

                foreach (var impPoint in pairData.impPoints)
                {
                    impPoint.impulseDes /= impPointCount;
                    impPoint.impulseRb /= impPointCount;

                    int conCount = pairs[impPoint.conPairIndex].contactCount;

                    for (int conI = 0; conI < conCount; conI++)
                    {
                        pairs[impPoint.conPairIndex].SetMaxImpulse(conI, (impPoint.impulseRb / conCount) * 0.025f);
                        //Looks like only discrete+speculative collision work properly with SetMaxImpulse,
                        //either we can only support discrete+speculative or we ignore contacts and apply them manually before next physics update
                        //Apply manually next also allows us to compute stuff in the background for better performance
                    }

                    totImpulseDes += impPoint.impulseDes;
                    totImpulseRb += impPoint.impulseRb;
                }

                Debug.Log("Imp id " + idData.Key + " rb force " + totImpulseRb + " des force " + totImpulseDes);
            }

            #endregion ComputeDataGotten
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
