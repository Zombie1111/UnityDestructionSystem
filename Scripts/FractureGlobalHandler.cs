using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace Zombie1111_uDestruction
{
    public class FractureGlobalHandler : MonoBehaviour
    {
        [Header("Global Fracture Config")]
        [SerializeField] private float damageThreshold = 2.0f;

        private ConcurrentDictionary<int, GlobalFracData> partsColInstanceId = new();
        private ConcurrentDictionary<int, GlobalRbData> rigidbodiesInstanceId = new();

#if UNITY_EDITOR
        [Space(10)]
        [Header("Debug")]
        [SerializeField] [Tooltip("Only exposed for debugging purposes")] private List<GlobalFracData> debugFractureInstanceIds = new();
        [SerializeField] [Tooltip("Only exposed for debugging purposes")] private List<GlobalRbData> debugRigidbodyInstanceIds = new();

        private void OnDrawGizmos()
        {
            //maintain temp saveAssets, potential problem, if scene is not saved but the asset is this may leave unused temp assets
            for (int i = tempSaveAssets.Count - 1; i >= 0 ; i--)
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
            if (partsColInstanceId.Count != debugFractureInstanceIds.Count)
            {
                debugFractureInstanceIds = partsColInstanceId.Values.ToList();
            }

            if (rigidbodiesInstanceId.Count != debugRigidbodyInstanceIds.Count)
            {
                debugRigidbodyInstanceIds = rigidbodiesInstanceId.Values.ToList();
            }
        }
#endif

        [System.Serializable]
        public class GlobalRbData
        {
            public Rigidbody rb;
            public float rbMass;
        }

        [System.Serializable]
        public class GlobalFracData
        {
            public FractureThis fracThis;
            public int partIndex;
        }

        private void Awake()
        {
            //get all rigidbodies in scene
            Rigidbody[] bodies = GameObject.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Rigidbody rb in bodies)
            {
                OnAddRigidbody(rb, rb.mass);
                //rigidbodiesInstanceId.TryAdd(rb.GetInstanceID(), rb);
            }

            //verify global handlers
            //If more than one global handler exists merge other with this one
            FractureGlobalHandler[] handlers = GameObject.FindObjectsByType<FractureGlobalHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < handlers.Length; i += 1)
            {
                if (handlers[i] == this) continue;

                partsColInstanceId.AddRange(handlers[i].partsColInstanceId);
                rigidbodiesInstanceId.AddRange(handlers[i].rigidbodiesInstanceId);
                Destroy(handlers[i]);
            }
        }

        public void OnEnable()
        {
            Physics.ContactModifyEvent += ModificationEvent;
            Physics.ContactModifyEventCCD += ModificationEvent;
        }

        public void OnDisable()
        {
            Physics.ContactModifyEvent -= ModificationEvent;
            Physics.ContactModifyEventCCD -= ModificationEvent;
        }

        public void AddReferencesFromFracture(FractureThis addFrom)
        {
            for (int i = 0; i < addFrom.allParts.Length; i += 1)
            {
                addFrom.allParts[i].col.hasModifiableContacts = true; //for some weird reason hasModifiableContacts must be set on awake for it to work
                partsColInstanceId.TryAdd(addFrom.allParts[i].col.GetInstanceID(), new() { fracThis = addFrom, partIndex = i });
                if (addFrom.isRealSkinnedM == true)
                {
                    addFrom.allSkinPartCols[i].hasModifiableContacts = true;
                    partsColInstanceId.TryAdd(addFrom.allSkinPartCols[i].GetInstanceID(), new() { fracThis = addFrom, partIndex = i });
                }
            }
        }

        /// <summary>
        /// Returns the fractureThis script and the part index the given collider instanceId is for, null if no part col instanceId
        /// </summary>
        /// <param name="instanceId">The instance id for a collider</param>
        /// <returns></returns>
        public GlobalFracData TryGetFracPartFromColInstanceId(int instanceId)
        {
            if (partsColInstanceId.TryGetValue(instanceId, out GlobalFracData fracD) == true) return fracD;

            return null;
        }

        /// <summary>
        /// Returns the rigidbody the given instanceId is for, null cant find rb with the instanceId
        /// </summary>
        /// <param name="instanceId">The instance id for a rigidbody</param>
        /// <returns></returns>
        public Rigidbody TryGetRigidbodyFromInstanceId(int instanceId)
        {
            if (rigidbodiesInstanceId.TryGetValue(instanceId, out GlobalRbData rb) == true) return rb.rb;

            return null;
        }

        /// <summary>
        /// This function should be called everytime you destroy a rigidbody (Call before the rigidbody is destroyed)
        /// </summary>
        /// <param name="destroyedRb"></param>
        public void OnDestroyRigidbody(Rigidbody destroyedRb)
        {
            rigidbodiesInstanceId.TryRemove(destroyedRb.GetInstanceID(), out _);
        }

        /// <summary>
        /// This function should be called everytime you add a rigidbody to any object (Call after the rigidbody is added)
        /// </summary>
        /// <param name="addedRb"></param>
        public void OnAddRigidbody(Rigidbody addedRb, float mass)
        {
            rigidbodiesInstanceId.TryAdd(addedRb.GetInstanceID(), new() { rb = addedRb, rbMass = mass });
        }

        /// <summary>
        /// This function should be called everytime you change the mass of a rigidbody (To update its stored mass value)
        /// </summary>
        /// <param name="rbWithNewMass"></param>
        /// <param name="newMass"></param>
        public void OnSetRigidbodyMass(Rigidbody rbWithNewMass, float newMass)
        {
            rigidbodiesInstanceId.TryUpdate(rbWithNewMass.GetInstanceID(), new() { rb = rbWithNewMass, rbMass = newMass }, null);
        }

#if UNITY_EDITOR
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

        private class ImpPair
        {
            public Vector3 impVel;
            public float impForce;
            public Rigidbody rbCausedImp;
            public FractureThis fracThis;
            public HashSet<int> pairIndexes;
            public List<ImpPoint> impPoints;
            public bool ignoreCauseRb;
        }

        public class ImpPoint
        {
            public int partIndex;
            public Vector3 impPos;
        }

        public void ModificationEvent(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            //get all impact points
            List<ImpPair> impPairs = new();

            for (int i = 0; i < pairs.Length; i++)
            {
                //Debug.Log(pairs[i].contactCount);
                //for (int ii = 0; ii < pairs[i].contactCount; ii++)
                //{
                //    FractureHelperFunc.Debug_drawBox(pairs[i].GetPoint(ii), 0.03f, Color.magenta, 1.0f);
                //}

                GetPairImpact(pairs[i], i);
            }

            //denoise impact points and register them
            bool didAnyBreak;

            for (int i = 0; i < impPairs.Count; i++)
            {
                foreach (var iii in impPairs[i].pairIndexes)
                {
                    Debug.DrawLine(pairs[iii].GetPoint(0), pairs[iii].GetPoint(0) + pairs[iii].GetNormal(0).normalized, Color.magenta, 0.5f, false);
                }

                //Debug.Log("count " +impPairs[i].impPoints.Count + " divide " + impPairs[i].impPoints.Count * impPairs[i].fracThis.partAvgBoundsExtent + "ogforce " + impPairs[i].impForce);
                //impPairs[i].impForce /= math.max(impPairs[i].impPoints.Count * impPairs[i].fracThis.partAvgBoundsExtent, 1.0f);
                //didAnyBreak = false;
                Debug.DrawLine(impPairs[i].impPoints[0].impPos, impPairs[i].impPoints[0].impPos + impPairs[i].impVel.normalized, Color.yellow, 0.5f, false);

                //foreach (var iPoint in impPairs[i].impPoints)
                //{
                //
                //    if (impPairs[i].fracThis.RegisterCollision(
                //        iPoint.partIndex,
                //        impPairs[i].impForce,
                //        impPairs[i].impVel,
                //        iPoint.impPos,
                //        impPairs[i].ignoreCauseRb == false ? impPairs[i].rbCausedImp : null) == true)
                //    {
                //        didAnyBreak = true;
                //    }
                //}

                if (impPairs[i].fracThis.RegisterCollision(
                    impPairs[i].impPoints,
                    impPairs[i].impForce,
                    impPairs[i].impVel,
                    impPairs[i].ignoreCauseRb == false ? impPairs[i].rbCausedImp : null) == false) continue;

                //when atleast one part will break, ignore contact
                foreach (int pairI in impPairs[i].pairIndexes)
                {
                    for (int ii = 0; ii < pairs[pairI].contactCount; ii++) pairs[pairI].IgnoreContact(ii);
                }
            }

            void GetPairImpact(ModifiableContactPair pair, int pairI)
            {
                //get the rigidbody that caused the impact
                GlobalRbData rbCausedImp;
                float impForce;
                Vector3 impVel;
                float speedA = math.max(pair.bodyVelocity.magnitude - pair.bodyAngularVelocity.magnitude, 0.0f);
                float speedB = math.max(pair.otherBodyVelocity.magnitude - pair.otherBodyAngularVelocity.magnitude, 0.0f);
                
                if (speedA > speedB)
                {
                    impVel = pair.bodyVelocity;
                    impForce = speedA - speedB;

                    if (impForce < damageThreshold) return;
                    if (rigidbodiesInstanceId.TryGetValue(pair.bodyInstanceID, out rbCausedImp) == false) return;
                }
                else
                {
                    impVel = pair.otherBodyVelocity;
                    impForce = speedB - speedA;

                    if (impForce < damageThreshold) return;
                    if (rigidbodiesInstanceId.TryGetValue(pair.otherBodyInstanceID, out rbCausedImp) == false) return;
                }

                //add the hit point to the list if we hit a part
                TryAddImpact(pair.colliderInstanceID);
                TryAddImpact(pair.otherColliderInstanceID);

                void TryAddImpact(int colId)
                {
                    //get the part that was hit
                    if (partsColInstanceId.TryGetValue(colId, out GlobalFracData impPart) == false) return;

                    //get if cause source already exists, if not add it
                    int iToUse = -1;
                    bool ignoreRb = false;

                    for (int i = 0; i < impPairs.Count; i++)
                    {
                        if (impPairs[i].rbCausedImp == rbCausedImp.rb)
                        {
                            if (impPairs[i].fracThis == impPart.fracThis)
                            {
                                iToUse = i;
                                break;
                            }
                            else if (impPairs[i].ignoreCauseRb == false)
                            {
                                //when should ignore a cause rb
                                if (impPairs[i].fracThis.allPartsResistance[impPairs[i].impPoints[0].partIndex]
                                    > impPart.fracThis.allPartsResistance[impPart.partIndex]) impPairs[i].ignoreCauseRb = true;
                                else ignoreRb = true;
                            }
                        }
                    }

                    if (iToUse < 0)
                    {
                        //add new cause source
                        iToUse = impPairs.Count;

                        impPairs.Add(new() {
                            fracThis = impPart.fracThis,
                            impForce = impForce * rbCausedImp.rbMass,
                            rbCausedImp = rbCausedImp.rb,
                            impVel = impVel,
                            pairIndexes = new(),
                            impPoints = new(),
                            ignoreCauseRb = ignoreRb});
                    }

                    impPairs[iToUse].pairIndexes.Add(pairI);

                    //get if this part in cause source already has been hit
                    foreach (var iPoint in impPairs[iToUse].impPoints)
                    {
                        if (iPoint.partIndex == impPart.partIndex)
                        {
                            return;
                        }
                    }

                    //add hit part to impact list
                    impPairs[iToUse].impPoints.Add(new() {
                        impPos = pair.GetPoint(0), //Maybe we want to loop and add all contact points?
                        partIndex = impPart.partIndex });
                }
            }
        }
    }
}
