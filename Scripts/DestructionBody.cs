using System.Collections.Generic;
using UnityEngine;

namespace zombDestruction
{
    public class DestructionBody : MonoBehaviour
    {
        [Tooltip("The mass the destructionSystem thinks this rigidbody has is rigidbody.mass * desMassMultiplier")]
        [SerializeField] private float desMassMultiplier = 2.0f;
        [SerializeField] private GlobalRbData customRigidbodyProperties = new();
        [SerializeField] private bool includeDisabled = false;
        [SerializeField] private bool includeKinematic = false;
        [Tooltip("If true, rigidbodies attatched to children of this gameobject will also be affected")]
        [SerializeField] private bool includeChildren = true;
        private DestructionHandler globalHandler;
        private HashSet<Rigidbody> usedRigidbodies = new();

        private void Start()
        {
            //Get rigidbodies to use
            globalHandler = DestructionHandler.TryGetDestructionHandler(gameObject);

            if (globalHandler == null) return;

            if (includeChildren == false)
            {
                if (TryGetComponent(out Rigidbody rb) == true && (rb.isKinematic == false || includeKinematic == true)) usedRigidbodies.Add(rb);
            }
            else
            {
                foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>(includeDisabled))
                {
                    if (rb.isKinematic == true && includeKinematic == false) continue;
                    usedRigidbodies.Add(rb);
                }
            }

            //Apply properties, we dont wanna update mass since it should not override if set by destructableObject
            ApplyRigidbodyProperties(false, true);
        }

        private void OnDestroy()
        {
#pragma warning disable CS0162 // Unreachable code detected
            if (FracGlobalSettings.canAutomaticallyRemoveAddedRigidbodies == false) return;

            if (globalHandler == null)
            {
                globalHandler = DestructionHandler.TryGetDestructionHandler(gameObject, null, false);
                if (globalHandler == null) return;
            }

            foreach (Rigidbody rb in usedRigidbodies)
            {
                if (rb == null)
                {
                    globalHandler.RemoveNullRigidbodies();
                    break;
                }

                globalHandler.OnRemoveRigidbody(rb);
            }
#pragma warning restore CS0162 // Unreachable code detected
        }

        /// <summary>
        /// Should be called if you have changed the customRigidbodyProperties or the mass of the rigidbody
        /// </summary>
        public void ApplyRigidbodyProperties(bool onlyUpdateMass = true, bool onlyUpdateOther = true)
        {
            foreach (Rigidbody rb in usedRigidbodies)
            {
                if (rb == null) continue;

                var rbData = customRigidbodyProperties.ShallowCopy();
                rbData.rb = rb;
                rbData.rbMass = rb.mass;
                rbData.desMass = rbData.rbMass * desMassMultiplier;
                globalHandler.OnAddOrUpdateRb(rbData, onlyUpdateMass, onlyUpdateOther);
            }
        }

        /// <summary>
        /// Sets and updates the buoyancy of the rigidbody
        /// </summary>
        public void SetRbBuoyancy(float newBuoyancy)
        {
            customRigidbodyProperties.buoyancy = newBuoyancy;
            ApplyRigidbodyProperties(false, true);//Example custom rb property
        }
    }
}
