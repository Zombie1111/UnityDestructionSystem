using Autodesk.Fbx;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using UnityEditor.Rendering.LookDev;
using UnityEngine;

namespace Zombie1111_uDestruction
{
    public class destructionJoint : MonoBehaviour
    {
        [SerializeField] private Transform connectedTransform = null;
        [SerializeField] private Joint sourceJoint = null;
        [SerializeField] private FractureThis fracSource = null;
        [SerializeField] private List<Transform> jointAnchors = new();

        public unsafe void SetupJoints(FractureThis fracThis)
        {
            //Verify if valid
            if (connectedTransform == null)
            {
                Debug.LogError(transform.name + " DestructionJoint connectedTransform property has not been assigned!");
                return;
            }

            if (sourceJoint == null)
            {
                Debug.LogError(transform.name + " DestructionJoint sourceJoint property has not been assigned!");
                return;
            }

            if (gameObject.GetComponentInParent<Rigidbody>(false) == null)
            {
                Debug.LogError("There must be a active rigidbody in " + transform.name + " or any of its parents!");
                return;
            }

            //remove previous joints
            RemoveJoints();

            //Get what parts should be connected
            fracSource = fracThis;
            var allPartCols = fracSource.saved_allPartsCol;
            int partCount = allPartCols.Count;

            for (int pI = 0; pI < partCount; pI++)
            {
                if (allPartCols[pI].transform.parent != transform) continue;

                var fPart = fracSource.jCDW_job.fStructs[pI];
                for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                {
                    int nPI = fPart.neighbourPartI[nI];
                    if (allPartCols[nPI].transform.parent != connectedTransform) continue;

                    DesJoint dJ = new()
                    {
                        partA = pI,
                        partB = nPI,
                    };

                    if (desJoints.ContainsKey(pI) == false) desJoints.Add(pI, new());
                    if (desJoints.ContainsKey(nPI) == false) desJoints.Add(nPI, new());
                    desJoints[pI].Add(dJ);
                    desJoints[nPI].Add(dJ);
                    OnPartParentChanged(pI, 0, 0);
                }
            }

            //Add to event
            if (Application.isPlaying == true) fracSource.OnPartParentChanged += OnPartParentChanged;
        }

        public void RemoveJoints()
        {
            //Remove from event
            if (Application.isPlaying == true && fracSource != null) fracSource.OnPartParentChanged -= OnPartParentChanged;

            //Destroy joints
            foreach (List<DesJoint> dJoints in desJoints.Values)
            {
                foreach (DesJoint dJoint in dJoints)
                {
                    if (dJoint.joint == null) continue;

                    Destroy(dJoint.joint);
                }
            }

            //reset other
            desJoints.Clear();
            fracSource = null;
        }

        [SerializeField] private Dictionary<int, List<DesJoint>> desJoints = new();

        [System.Serializable]
        private class DesJoint
        {
            public int partA;
            public int partB;
            public Joint joint;
        }

        private void Awake()
        {
            //Add to event
            if (fracSource != null) fracSource.OnPartParentChanged += OnPartParentChanged;
        }

        private void OnDestroy()
        {
            //Remove from event
            if (fracSource != null) fracSource.OnPartParentChanged -= OnPartParentChanged;
        }

        private void OnPartParentChanged(int partI, int oldParentI, int newParentI)
        {
            if (desJoints.TryGetValue(partI, out List<DesJoint> dJoints) == false) return;

            foreach (DesJoint dJoint in dJoints)
            {
                //Get anchor position
                Collider colA = fracSource.saved_allPartsCol[dJoint.partA];
                Collider colB = fracSource.saved_allPartsCol[dJoint.partB];
                Vector3 anchorWorld;

                if (dJoint.joint == null)
                {
                    anchorWorld = (colA.bounds.center + colB.bounds.center) / 2.0f;
                    Vector3 bestA = anchorWorld;
                    float bestD = float.MaxValue;

                    foreach (Transform anchor in jointAnchors)
                    {
                        float dis = (anchorWorld - anchor.position).sqrMagnitude;
                        if (bestD <= dis) continue;

                        bestD = dis;
                        bestA = anchor.position;
                    }

                    anchorWorld = bestA;
                }
                else
                {
                    anchorWorld = dJoint.joint.transform.localToWorldMatrix.MultiplyPoint3x4(dJoint.joint.anchor);
                }

                Rigidbody rbB = colB.attachedRigidbody;

                if (dJoint.partA == partI)
                {
                    //Since partA parent was changed we must recreate the joint
                    Destroy(dJoint.joint);
                    if (newParentI != 0 || fracSource.jCDW_job.partsParentI[dJoint.partB] != 0) return;

                    Rigidbody rbA = colA.attachedRigidbody;

                    dJoint.joint = FractureHelperFunc.CopyJoint(
                        sourceJoint,
                        rbA.gameObject,
                        rbB,
                        rbA.transform.worldToLocalMatrix.MultiplyPoint3x4(anchorWorld)
                        //rbB.transform.worldToLocalMatrix.MultiplyPoint3x4(anchorWorld)
                        );
                }
                else if (dJoint.joint != null)
                {
                    //PartB parent was changed just change connected body
                    if (newParentI != 0)
                    {
                        Destroy(dJoint.joint);
                        return;
                    }

                    dJoint.joint.connectedBody = rbB;
                    //dJoint.joint.connectedAnchor = rbB.transform.worldToLocalMatrix.MultiplyPoint3x4(anchorWorld);
                }
            }
        }
    }
}

