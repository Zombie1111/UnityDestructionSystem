using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using Unity.VisualScripting;
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

        private void OnDrawGizmosSelected()
        {
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
                OnAddRigidbody(rb);
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
        public void OnAddRigidbody(Rigidbody addedRb)
        {
            rigidbodiesInstanceId.TryAdd(addedRb.GetInstanceID(), new() { rb = addedRb, rbMass = addedRb.mass });
        }

        /// <summary>
        /// This function should be called everytime you change the mass of a rigidbody (To update its stored mass value)
        /// </summary>
        /// <param name="rbWithNewMass"></param>
        /// <param name="newMass"></param>
        public void OnChangeRigidbodyMass(Rigidbody rbWithNewMass)
        {
            rigidbodiesInstanceId.TryUpdate(rbWithNewMass.GetInstanceID(), new() { rb = rbWithNewMass, rbMass = rbWithNewMass.mass }, null);
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

        private class ImpPoint
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
                GetPairImpact(pairs[i], i);
            }

            //denoise impact points and register them
            bool didAnyBreak;

            for (int i = 0; i < impPairs.Count; i++)
            {
                float ogDForce = impPairs[i].impForce;
                impPairs[i].impForce /= math.max(impPairs[i].impPoints.Count * impPairs[i].fracThis.partAvgBoundsExtent, 1.0f);
                didAnyBreak = false;

                //print("divide " + math.max(impPairs[i].impPoints.Count, 1.0f) + " force " + ogDForce
                //+ " force calced " + (impPairs[i].impForce / math.max(impPairs[i].impPoints.Count * impPairs[i].fracThis.partAvgBoundsExtent, 1.0f))
                //+ " cCount " + impPairs[i].pairIndexes.Count);

                foreach (var iPoint in impPairs[i].impPoints)
                {
                    FractureHelperFunc.Debug_drawBox(iPoint.impPos, 0.1f, Color.magenta, 1.0f);
                    if (impPairs[i].fracThis.RegisterImpact(
                        iPoint.partIndex,
                        impPairs[i].impForce,
                        impPairs[i].impVel,
                        iPoint.impPos,
                        impPairs[i].rbCausedImp) == true)
                    {
                        didAnyBreak = true;
                    }
                }

                if (didAnyBreak == false) continue;

                //when atleast one part will break, ignore contact
                foreach (int pairI in impPairs[i].pairIndexes)
                {
                    //if (pairs[pairI].contactCount > 1)
                    //{
                    //    FractureHelperFunc.Debug_drawBox(pairs[pairI].GetPoint(pairI), 0.1f, Color.magenta, 5.0f);
                    //    Debug.Log("force " + impPairs[i].impForce + " vel " + impPairs[i].impVel.magnitude + " count " + pairs[pairI].contactCount);
                    //}

                    for (int ii = 0; ii < pairs[pairI].contactCount; ii++) pairs[pairI].IgnoreContact(ii);
                }
            }

            void GetPairImpact(ModifiableContactPair pair, int pairI)
            {
                //get the rigidbody that caused the impact
                GlobalRbData rbCausedImp;
                float impForce;
                Vector3 impVel;

                if (pair.bodyVelocity.sqrMagnitude > pair.otherBodyVelocity.sqrMagnitude)
                {
                    impVel = pair.bodyVelocity;
                    impForce = impVel.magnitude - pair.otherBodyVelocity.magnitude;

                    if (impForce < damageThreshold) return;
                    if (rigidbodiesInstanceId.TryGetValue(pair.bodyInstanceID, out rbCausedImp) == false) return;
                }
                else
                {
                    impVel = pair.otherBodyVelocity;
                    impForce = impVel.magnitude - pair.bodyVelocity.magnitude;

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

                    for (int i = 0; i < impPairs.Count; i++)
                    {
                        if (impPairs[i].fracThis == impPart.fracThis && impPairs[i].rbCausedImp == rbCausedImp.rb)
                        {
                            iToUse = i;
                            break;
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
                            impPoints = new()});
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
