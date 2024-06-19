using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace zombDestruction
{
    public class DestructionBody : MonoBehaviour
    {
        [SerializeField] private float desMassMultiplier = 2.0f;
        [SerializeField] private GlobalRbData customRigidbodyProperties = new();
        [SerializeField] private bool includeDisabled = false;
        [SerializeField] private bool includeKinematic = false;
        [SerializeField] private bool includeChildren = true;
        private DestructionHandler globalHandler;
        private HashSet<Rigidbody> usedRigidbodies = new();

        private void Start()
        {
            globalHandler = DestructionHandler.TryGetGlobalHandler(gameObject);
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

            foreach (Rigidbody rb in usedRigidbodies)
            {
                var rbData = customRigidbodyProperties.ShallowCopy();
                rbData.rb = rb;
                rbData.rbMass = rb.mass;
                rbData.desMass = rbData.rbMass * desMassMultiplier;
                globalHandler.OnAddOrUpdateRb(rbData);
            }
        }

        private void OnDestroy()
        {
#pragma warning disable CS0162 // Unreachable code detected
            if (FracGlobalSettings.canAutomaticallyRemoveAddedRigidbodies == false) return;


            if (globalHandler == null)
            {
                globalHandler = DestructionHandler.TryGetGlobalHandler(gameObject, null, false);
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
    }
}
