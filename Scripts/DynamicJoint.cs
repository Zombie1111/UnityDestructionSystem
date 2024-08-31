using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace zombDestruction
{
    public class DynamicJoint : MonoBehaviour
    {
        [SerializeField] private Joint sourceJoint = null;
        [SerializeField] private Transform requiredParent = null;
        [SerializeField] private LayerMask requiredLayer = Physics.AllLayers;
        [SerializeField] private bool includeKinematic = false;
        [SerializeField] private bool exactOverlap = true;
        [SerializeField] private float minConnectedRbMass = 0.5f;
        [SerializeField] private Transform connectionAnchor = null;
        private DestructableObject desObject = null;

        private void Start()
        {
            desObject = DestructableObject.TryGetValidDestructableObjectInParent(transform);
            if (desObject == null)
            {
                Debug.LogError("There is no valid DestructableObject in " + transform.name + " or any of its parents");
                return;
            }

            SetupJoints();
            desObject.OnParentUpdated += OnParentUpdated;
            updateJointsNextFrame = true;

            void SetupJoints()
            {
                //Get all colliders that we can connect with
                Rigidbody newThisRb = GetComponentInChildren<Rigidbody>();
                if (newThisRb == null)
                {
                    Debug.LogError("There is no rigidbody attatched to " + transform.name + " or any of its children");
                    return;
                }

                HashSet<Collider> newConnectedCols = new();

                foreach (Collider col in GetComponentsInChildren<Collider>(true))
                {
                    if (col.attachedRigidbody != newThisRb) continue;

                    var colB = col.bounds;
                    foreach (Collider lapCol in Physics.OverlapBox(colB.center, colB.extents, Quaternion.identity, requiredLayer, QueryTriggerInteraction.Ignore))
                    {
                        Rigidbody lapRb = lapCol.attachedRigidbody;
                        if (lapRb == null || newThisRb == lapRb || (lapRb.isKinematic == true && includeKinematic == false)) continue;

                        if (requiredParent != null && FracHelpFunc.GetIfTransformIsAnyParent(requiredParent, lapCol.transform) == false) continue;

                        if (exactOverlap == true && Physics.ComputePenetration(col, col.transform.position, col.transform.rotation,
                            lapCol, lapCol.transform.position, lapCol.transform.rotation, out _, out _) == false) continue;

                        newConnectedCols.Add(lapCol);
                    }
                }

                if (newConnectedCols.Count == 0)
                {
                    Debug.LogError("Found no valid colliders to connect " + transform.name + " with");
                    return;
                }

                connectedCols = newConnectedCols.ToArray();
                thisRb = newThisRb;
            }
        }

        private void OnParentUpdated(int parentI)
        {
            updateJointsNextFrame = true;
        }

        private Dictionary<Rigidbody, Joint> rbsConnectedWith = new();
        private Collider[] connectedCols = null;
        private Rigidbody thisRb = null;
        private bool updateJointsNextFrame = false;

        private void Update()
        {
            if (updateJointsNextFrame == false) return;

            UpdateConnectedJoints();
            updateJointsNextFrame = false;
        }

        private void UpdateConnectedJoints()
        {
            if (connectedCols == null) return;

            //Get old connected rigidbodies
            HashSet<Rigidbody> allConnectedRbs = new(rbsConnectedWith.Count);
            foreach (Rigidbody oldRb in rbsConnectedWith.Keys)
            {
                allConnectedRbs.Add(oldRb);
            }

            //Get rigidbodies to be connected with
            HashSet<Rigidbody> newConnectedRbs = new(1);

            foreach (Collider col in connectedCols)
            {
                Rigidbody rb = col.attachedRigidbody;

                if (newConnectedRbs.Contains(rb) == true) continue;//We dont need to check other shit if its already added
                if (rb == null || rb.mass < minConnectedRbMass ||
                    (rb.isKinematic == true && includeKinematic == false)) continue;

                newConnectedRbs.Add(rb);
                allConnectedRbs.Add(rb);
            }

            //Connect + disconnect from rigidbodies
            foreach (var rb in allConnectedRbs)
            {
                if (newConnectedRbs.Contains(rb) == false)
                {
                    //Remove connection
                    Destroy(rbsConnectedWith[rb]);
                    rbsConnectedWith.Remove(rb);
                    continue;
                }

                if (rbsConnectedWith.ContainsKey(rb) == true) continue;

                //Add connection
                rbsConnectedWith.Add(rb, FracHelpFunc.CopyJoint(sourceJoint, thisRb.gameObject, rb,
                    transform.InverseTransformPoint(connectionAnchor != null ? connectionAnchor.position : transform.position)));
            }
        }

        private void RemoveJoints()
        {
            if (desObject != null) desObject.OnParentUpdated -= OnParentUpdated;

            foreach (var joint in rbsConnectedWith.Values)
            {
                Destroy(joint);
            }

            desObject = null;
            connectedCols = null;
            rbsConnectedWith.Clear();
        }

        private void OnDestroy()
        {
            RemoveJoints();
        }
    }
}

