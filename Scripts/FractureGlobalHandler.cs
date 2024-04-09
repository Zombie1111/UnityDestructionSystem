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
            public bool wantToRemove;
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
        /// Call to remove a rigidbody that you added with OnAddOrUpdateRigidbody(), should always be called before you destroy a rigidbody component
        /// </summary>
        /// <param name="rbToRemove">The rigidbody </param>
        public void OnRemoveRigidbody(Rigidbody rbToRemove)
        {
            int rbId = rbToRemove.GetInstanceID();
            if (jGRV_rbToSet.TryAdd(rbId, new() { wantToRemove = true }) == false)
            {
                jGRV_rbToSet[rbId].wantToRemove = true;
            }
        }

        /// <summary>
        /// All rigidbodies that should be able to damage a destructable object must be added through this function
        /// </summary>
        /// <param name="mass">The mass the rigidbody has for the destruction system</param>
        public void OnAddOrUpdateRb(Rigidbody rbToAddOrUpdate, float mass)
        {
            int rbId = rbToAddOrUpdate.GetInstanceID();
            if (jGRV_rbToSet.TryAdd(rbId, new()
            {
                mass = mass,
                rb = rbToAddOrUpdate,
                wantToRemove = false }) == false)
            {
                jGRV_rbToSet[rbId].mass = mass;
                jGRV_rbToSet[rbId].wantToRemove = false;
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

                if (jGRV_rbToSet.TryAdd(rbId, new() { wantToRemove = true }) == false)
                    jGRV_rbToSet[rbId].wantToRemove = true;
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
                    bool wantRemove = jGRV_rbToSet[rbId].wantToRemove;
                    Transform rbTrans = wantRemove == false ? jGRV_rbToSet[rbId].rb.transform : null;

                    if (wantRemove == false && rbInstancIdToJgrvIndex.TryGetValue(rbId, out int jIndex) == true)
                    {
                        //update
                        jGRV_rb_mass[jIndex] = jGRV_rbToSet[rbId].mass;
                        var posD = jGRV_job.rb_posData[jIndex];
                        posD.rb_position = rbTrans.position;
                        posD.rb_velocity = Vector3.zero;
                        posD.rb_angel = rbTrans.rotation;
                        posD.rb_angularVel = Vector3.zero;
                        jGRV_job.rb_posData[jIndex] = posD;
                    }
                    else if (wantRemove == false)
                    {
                        //add
                        if (jGRV_rbTrans.isCreated == false) jGRV_rbTrans = new(new Transform[1] { rbTrans });
                        else jGRV_rbTrans.Add(rbTrans);
                        jGRV_rb_mass.Add(jGRV_rbToSet[rbId].mass);
                        jGRV_job.rb_posData.Add(new()
                        {
                            rb_position = rbTrans.position,
                            rb_velocity = Vector3.zero,
                            rb_angel = rbTrans.rotation,
                            rb_angularVel = Vector3.zero,
                        });

                        rbInstancIdToJgrvIndex.Add(rbId, rbInstancIdToJgrvIndex.Count);
                    }
                    else if (rbInstancIdToJgrvIndex.TryGetValue(rbId, out jIndex) == true)
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
                public Vector3 rb_velocity;
                public Vector3 rb_position;
                public Vector3 rb_angularVel;
                public quaternion rb_angel;
            }

            public void Execute(int index, TransformAccess transform)
            {
                
                //rb_centerWorld[index] = transform.localToWorldMatrix.MultiplyPoint(rb_centerLocal[index]);
                RbPosData posD = rb_posData[index];
                posD.rb_velocity = (transform.position - posD.rb_position) / deltaTime;
                posD.rb_angularVel = FractureHelperFunc.GetAngularVelocity(posD.rb_angel, transform.rotation, deltaTime);
                posD.rb_position = transform.position;
                posD.rb_angel = transform.rotation;
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

            GetRbVelocities_start();

        }

        private class ImpPair
        {
            public Vector3 impVel;
            public float impForce;
            public Rigidbody rbCausedImp;
            public FractureThis fracThis;
            public HashSet<int> pairIndexes;
            public List<ImpPoint> impPoints;
        }

        public class ImpPoint
        {
            public int partIndex;
            public Vector3 impPos;
        }

        public void ModificationEvent(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            foreach (var pair in pairs)
            {
                //for (int i = 0; i < pair.contactCount; i++)
                //{
                //    Vector3 pos = pair.GetPoint(i);
                //    Debug.DrawLine(pos, pos + pair.GetNormal(i), Color.magenta, 1.0f);
                //}
                //
                //if (rbInstancIdToJgrvIndex.TryGetValue(pair.bodyInstanceID, out int rbI) == true)
                //{
                //    Debug.Log("vel " + jGRV_job.rb_posData[rbI].rb_velocity.magnitude + " ang " + jGRV_job.rb_posData[rbI].rb_angularVel.magnitude + " rbI " + rbI);
                //}
                //
                //if (rbInstancIdToJgrvIndex.TryGetValue(pair.otherBodyInstanceID, out rbI) == true)
                //{
                //    Debug.Log("vel " + jGRV_job.rb_posData[rbI].rb_velocity.magnitude + " ang " + jGRV_job.rb_posData[rbI].rb_angularVel.magnitude + " rbI " + rbI);
                //}
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
        }
        #endregion CollisionHandling
    }
}
