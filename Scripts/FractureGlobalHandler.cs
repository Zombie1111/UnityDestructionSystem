using g3;
using OpenCover.Framework.Model;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor;
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
            public int partIndex;
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

                    OnAddOrUpdateRb(rb, handlers[i].jGRV_rb_mass[rI]);//potential issue, if the rb hit something the same frame it wont cause any damage
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

        public void OnAddFracPart(FractureThis frac, int partI)
        {
            partColsInstanceId.TryAdd(frac.allParts[partI].col.GetInstanceID(), new()
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
                partColsInstanceId.TryRemove(destroyedFrac.allParts[i].col.GetInstanceID(), out _);
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
        private List<float> jGRV_rb_mass;
        private JobHandle jGRV_handle;
        private GetRbVelocities_work jGRV_job;
        private Dictionary<int, int> rbInstancIdToJgrvIndex = new();
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
            jGRV_rb_mass = new();
        }

        private void GetRbVelocities_start()
        {
            if (jGRV_jobIsActive == true || jGRV_rbTrans.isCreated == false) return;

            //run the job
            jGRV_job.deltaTime = Time.fixedDeltaTime;
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
                            jGRV_rb_mass[jIndex] = jGRV_rbToSet[rbId].mass;
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
                        jGRV_rb_mass.Add(jGRV_rbToSet[rbId].mass);
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
            public float deltaTime;

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

                //Vector3 centerW = transform.localToWorldMatrix.MultiplyPoint(posD.rb_centerL);
                //
                //posD.rb_velocity = (centerW - posD.rb_centerW) / deltaTime;
                ////posD.rb_angularVel = FractureHelperFunc.GetAngularVelocity(posD.rb_angel, transform.rotation, deltaTime);
                //
                //posD.rb_rotVel = Quaternion.Inverse(posD.rb_rot) * transform.rotation;
                //
                ////Vector3 axis;
                ////float angle;
                ////posD.rb_rotVel.ToAngleAxis(out angle, out axis);
                ////angle /= deltaTime;
                ////posD.rb_rotVel = Quaternion.AngleAxis(angle, axis);
                //
                //posD.rb_centerW = centerW;
                //posD.rb_rot = transform.rotation;
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
            //end get rb velocities
            GetRbVelocities_end();

            //run late fixedUpdate later
            StartCoroutine(LateFixedUpdate());
        }

        private IEnumerator LateFixedUpdate()
        {
            //wait for late fix update
            yield return new WaitForFixedUpdate();
            fixedDeltatime = Time.fixedDeltaTime;//we wanna set this at the same time we set the job deltatime
            GetRbVelocities_start();

        }

        private Dictionary<int, ImpPair> impIdToImpPair = new();
        private float fixedDeltatime = 0.01f;

        private class ImpPair
        {
            public Vector3 impVel;
            public float impForceT;
            public FractureThis impFrac;
            public List<ImpPoint> impPoints;
            public Rigidbody sourceRb;
            public HashSet<short> pairIndexes;
        }

        public class ImpPoint
        {
            public short partI;
            public float force;
        }

        public void ModificationEvent(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            ModifiableContactPair pair;
            Vector3 rbDiffVel;
            Vector3 rbA_vel;
            Vector3 rbB_vel;

            for (short pairI = 0; pairI < pairs.Length; pairI++)
            {
                pair = pairs[pairI];

                CalcImpPair(pair.colliderInstanceID, pair.bodyInstanceID, pair.otherColliderInstanceID, pair.otherBodyInstanceID, 0, pairI);
                //CalcImpPair(pair.otherColliderInstanceID, pair.otherBodyInstanceID, pair.colliderInstanceID, pair.bodyInstanceID, 1, pairI);

                ////debug
                //for (int i = 0; i < pair.contactCount; i++)
                //{
                //    Vector3 pos = pair.GetPoint(i);
                //    Debug.DrawLine(pos, pos + pair.GetNormal(i), Color.magenta, 1.0f);
                //}
                
                if (rbInstancIdToJgrvIndex.TryGetValue(pair.bodyInstanceID, out int rbI) == true)
                {
                    //Debug.Log("vel " + jGRV_job.rb_posData[rbI].rb_velocity.magnitude + " ang " + jGRV_job.rb_posData[rbI].rb_angularVel.magnitude + " rbI " + rbI);
                    //debug get vel
                    FractureHelperFunc.GetObjectVelocityAtPoint(
                        jGRV_job.rb_posData[rbI].rbWToLPrev,
                        jGRV_job.rb_posData[rbI].rbLToWNow,
                        pair.GetPoint(0), fixedDeltatime
                        );
                }

                if (rbInstancIdToJgrvIndex.TryGetValue(pair.otherBodyInstanceID, out rbI) == true)
                {
                    //Debug.Log("vel " + jGRV_job.rb_posData[rbI].rb_velocity.magnitude + " ang " + jGRV_job.rb_posData[rbI].rb_angularVel.magnitude + " rbI " + rbI);
                    FractureHelperFunc.GetObjectVelocityAtPoint(
                         jGRV_job.rb_posData[rbI].rbWToLPrev,
                         jGRV_job.rb_posData[rbI].rbLToWNow,
                         pair.GetPoint(0), fixedDeltatime
                         );
                }
            }

            void CalcImpPair(int colId, int rbId, int subColId, int subRbId, byte idOffset, short pairI)
            {
                Debug.Log(rbId + " " + subRbId);

                //create a ImpPair for this pair
                int pairKey = rbId + subRbId + idOffset;
                if (impIdToImpPair.TryGetValue(pairKey, out ImpPair impPair) == false)
                {
                    ////When impPair does not already exist, check if this is valid imp and create new impPair
                    ////get rigidbody velocity
                    //if (rbInstancIdToJgrvIndex.TryGetValue(rbId, out int rbI) == true) rbA_vel = FractureHelperFunc.GetObjectVelocityAtPoint(
                    //    jGRV_job.rb_posData[rbI].rb_centerW,
                    //    jGRV_job.rb_posData[rbI].rb_velocity,
                    //    jGRV_job.rb_posData[rbI].rb_angularVel,
                    //    pair.GetPoint(0));
                    //else rbA_vel = Vector3.zero;

                    //if (rbInstancIdToJgrvIndex.TryGetValue(pair.otherBodyInstanceID, out rbI) == true) rbB_vel = FractureHelperFunc.GetObjectVelocityAtPoint(
                    //     jGRV_job.rb_posData[rbI].rb_centerW,
                    //     jGRV_job.rb_posData[rbI].rb_velocity,
                    //     jGRV_job.rb_posData[rbI].rb_angularVel,
                    //     pair.GetPoint(0));
                    //else rbB_vel = Vector3.zero;
                    //
                    //rbDiffVel = rbB_vel.sqrMagnitude > rbA_vel.sqrMagnitude ? (rbB_vel - rbA_vel) : (rbA_vel - rbB_vel);
                }
            }


           
            //
            //if (partColsInstanceId.TryGetValue(pair.colliderInstanceID, out GlobalFracData partD) == true)
            //{
            //    Debug.Log("partI " + partD.partIndex);
            //}
            //
            //if (partColsInstanceId.TryGetValue(pair.otherColliderInstanceID, out partD) == true)
            //{
            //    Debug.Log("partI " + partD.partIndex);
            //}
        }
        #endregion CollisionHandling
    }
}
