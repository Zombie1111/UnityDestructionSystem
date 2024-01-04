using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
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

        private double timeDebug;

        public void ModificationEvent(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            //Stopwatch stopW = new();
            //stopW.Start();
            float speedA;
            float speedB;
            GlobalFracData colPart;
            float impactForce;
            Vector3 impactVelocity;
            Vector3 impactVelocityNor;
            bool didAnyPartBreak;
            Vector3 colPos;
            GlobalRbData rbCauseImpact;

            foreach (var pair in pairs)
            {
                //get impact force
                speedA = Math.Max(pair.bodyVelocity.magnitude - pair.bodyAngularVelocity.magnitude, 0.0f);
                speedB = Math.Max(pair.otherBodyVelocity.magnitude - pair.otherBodyAngularVelocity.magnitude, 0.0f);
                //speedA = pair.bodyVelocity.magnitude;
                //speedB = pair.otherBodyVelocity.magnitude;

                if (speedA > speedB)
                {
                    if (speedA < damageThreshold) continue;

                    colPos = pair.position;
                    if (rigidbodiesInstanceId.TryGetValue(pair.bodyInstanceID, out rbCauseImpact) == true) impactForce = (speedA - speedB) * rbCauseImpact.rbMass;
                    else impactForce = speedA - speedB;
                    //UnityEngine.Debug.Log("Mass " + pair.massProperties.inverseInertiaScale + " " + pair.massProperties.otherInverseInertiaScale);
                    impactVelocity = pair.bodyVelocity;
                    //rbCauseImpact = pair.bodyInstanceID;
                }
                else
                {
                    if (speedB < damageThreshold) continue;

                    colPos = pair.otherPosition;
                    if (rigidbodiesInstanceId.TryGetValue(pair.otherBodyInstanceID, out rbCauseImpact) == true) impactForce = (speedB - speedA) * rbCauseImpact.rbMass;
                    else impactForce = speedB - speedA;
                    //UnityEngine.Debug.Log("Mass " + pair.massProperties.inverseInertiaScale + " " + pair.massProperties.otherInverseInertiaScale);
                    impactVelocity = pair.otherBodyVelocity;
                }

                //register the impact and get if any part broke
                didAnyPartBreak = false;

                if (partsColInstanceId.TryGetValue(pair.colliderInstanceID, out colPart) == true)
                {
                    didAnyPartBreak = colPart.fracThis.RegisterImpact(colPart.partIndex, impactForce, impactVelocity, pair.GetPoint(0), rbCauseImpact);
                }

                if (partsColInstanceId.TryGetValue(pair.otherColliderInstanceID, out colPart) == true)
                {
                    if (didAnyPartBreak == false) didAnyPartBreak = colPart.fracThis.RegisterImpact(colPart.partIndex, impactForce, impactVelocity, pair.GetPoint(0), rbCauseImpact);
                    else colPart.fracThis.RegisterImpact(colPart.partIndex, impactForce, impactVelocity, pair.GetPoint(0), rbCauseImpact);
                }

                if (didAnyPartBreak == false) continue;

                //if any part broke, ignore the collision
                impactVelocityNor = -impactVelocity.normalized;

                for (int i = 0; i < pair.contactCount; i++)
                {
                    //pair.SetMaxImpulse(i, impactVelocity.magnitude / 10.0f);
                    pair.IgnoreContact(i);
                }
            }

            //timeDebug += (double)stopW.ElapsedTicks / Stopwatch.Frequency * 1000.0;
            //stopW.Stop();
        }
    }
}
