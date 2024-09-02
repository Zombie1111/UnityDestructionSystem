
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace zombDestruction
{
    public class DestructionJoint : MonoBehaviour
    {
        [SerializeField] private Transform connectedTransform = null;
        [SerializeField] private Joint sourceJoint = null;
        private DestructableObject fracSource = null;
        [SerializeField] private List<Transform> jointAnchors = new();
        [SerializeField] private float maxDisFromAnchor = 2.0f;

#pragma warning disable IDE0044 // Add readonly modifier
        private Dictionary<int, DesJoint> jointIdToDesJoint = new();
        private Dictionary<int, HashSet<int>> partIToConnectionI = new();
        private List<DesConnection> desConnections = new();
#pragma warning restore IDE0044 // Add readonly modifier

        private void Start()
        {
            SetupJoints();

            unsafe void SetupJoints()
            {
                //remove previous joints
                RemoveJoints();

                //Get fracture
                fracSource = DestructableObject.TryGetValidDestructableObjectInParent(transform);
                if (fracSource == null) return;

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

                if (jointAnchors.Count == 0)
                {
                    Debug.LogError(transform.name + " DestructionJoint does not have any jointAnchors");
                    return;
                }

                foreach (Transform trans in jointAnchors)
                {
                    if (trans != null) continue;

                    Debug.LogError("A jointAnchor transform in " + trans.name + " DestructionJoint is null");
                    return;
                }

                if (float.IsInfinity(sourceJoint.breakForce) == false || float.IsInfinity(sourceJoint.breakTorque) == false)
                {
                    Debug.LogError(transform.name + " DestructionJoint sourceJoint must have infinit breakForce and breakTorque");
                    return;
                }

                //Setup destruction joints
                int partCount = fracSource.allParts.Count;
                Vector3[] anchorPoss = jointAnchors.Select(trans => trans.position).ToArray();

                for (int partI = 0; partI < partCount; partI++)
                {
                    Collider partCol = fracSource.allPartsCol[partI];
                    if (partCol.transform.parent != transform) continue;

                    //Get what anchor to use
                    float bestD = maxDisFromAnchor;
                    int bestAI = -1;

                    for (int aI = 0; aI < anchorPoss.Length; aI++)
                    {
                        float dis = (partCol.ClosestPoint(anchorPoss[aI]) - anchorPoss[aI]).magnitude;
                        if (dis > bestD) continue;

                        bestD = dis;
                        bestAI = aI;
                    }

                    if (bestAI < 0) continue;

                    //Loop neighbours and see if any is valid connection
                    var fPart = fracSource.jCDW_job.fStructs[partI];

                    for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                    {
                        int nPI = fPart.neighbourPartI[nI];

                        if (fracSource.allPartsCol[nPI].transform.parent != connectedTransform) continue;

                        AddConnectionIndexLink(partI, desConnections.Count);
                        AddConnectionIndexLink(nPI, desConnections.Count);

                        desConnections.Add(new()
                        {
                            anchorI = bestAI,
                            jointId = 0,
                            partA = partI,
                            partB = nPI,
                            isValid = false
                        });

                        OnPartParentChanged(partI, 0, 0);
                    }
                }

                //Add to event
                fracSource.OnPartParentChanged += OnPartParentChanged;

                void AddConnectionIndexLink(int partI, int connectionI)
                {
                    if (partIToConnectionI.TryGetValue(partI, out HashSet<int> conIndexs) == false)
                    {
                        conIndexs = new();
                    }

                    if (conIndexs.Add(connectionI) == false) return;
                    partIToConnectionI[partI] = conIndexs;
                }
            }
        }

        private class DesJoint
        {
            public Joint phyJoint;
            public int connectionCount;
        }

        private class DesConnection
        {
            public int jointId;
            public int anchorI;
            public int partA;
            public int partB;
            public bool isValid;
        }

        public void RemoveJoints()
        {
            //Remove from event
            if (fracSource != null) fracSource.OnPartParentChanged -= OnPartParentChanged;

            //Remove joints
            foreach (var desJ in jointIdToDesJoint)
            {
                Destroy(desJ.Value.phyJoint);
            }

            //Reset
            jointIdToDesJoint.Clear();
            desConnections.Clear();
            partIToConnectionI.Clear();
            fracSource = null;
        }

        private void OnDestroy()
        {
            RemoveJoints();
        }

        private void OnPartParentChanged(int partI, int oldParentI, int newParentI)
        {
            if (partIToConnectionI.ContainsKey(partI) == false) return;
            foreach (int conI in partIToConnectionI[partI])
            {
                var con = desConnections[conI];
                int parentA = fracSource.allPartsParentI[con.partA];
                int parentB = fracSource.allPartsParentI[con.partB];

                bool isValid = parentA >= 0 && parentA == parentB;
                if (isValid == false)
                {
                    //Remove connection
                    if (con.isValid == false) continue;
                    con.isValid = false;
                    RemoveConnectionFromDesJoint(con.jointId);

                    continue;
                }

                int jointId = HashCode.Combine(parentA, parentB, con.anchorI);

                if (con.isValid == false)
                {
                    //Add connection
                    con.isValid = true;
                    con.jointId = jointId;
                    CreateDesJoint(jointId, con.partA, con.partB, con.anchorI);

                    continue;
                }

                //Update connection
                if (con.jointId == jointId) continue;

                RemoveConnectionFromDesJoint(con.jointId);
                con.jointId = jointId;
                CreateDesJoint(jointId, con.partA, con.partB, con.anchorI);
            }
        }

        /// <summary>
        /// Removes a connection from the given desJointId and destroys the desJoint if this was last connection, throws error if desJId does not exist
        /// </summary>
        private void RemoveConnectionFromDesJoint(int desJId)
        {
            var desJ = jointIdToDesJoint[desJId];

            desJ.connectionCount--;
            if (desJ.connectionCount > 0) return;

            DestroyImmediate(desJ.phyJoint);
            jointIdToDesJoint.Remove(desJId);
        }

        /// <summary>
        /// Adds a connection to the given desJointId, throws error if desJId does not exist
        /// </summary>
        private void AddConnectionToDesJoint(int desJId)
        {
            jointIdToDesJoint[desJId].connectionCount++;
        }

        /// <summary>
        /// Creates a desJoint and adds a connection to it
        /// </summary>
        private void CreateDesJoint(int desJId, int partA, int partB, int anchorI)
        {
            if (jointIdToDesJoint.TryGetValue(desJId, out _) == false)
            {
                Rigidbody rbA = fracSource.allPartsCol[partA].attachedRigidbody;

                DesJoint desJ = new()
                {
                    phyJoint = FracHelpFunc.CopyJoint(
                        sourceJoint,
                        rbA.gameObject,
                        fracSource.allPartsCol[partB].attachedRigidbody,
                        rbA.transform.worldToLocalMatrix.MultiplyPoint3x4(jointAnchors[anchorI].position)),
                    connectionCount = 1
                };

                jointIdToDesJoint.Add(desJId, desJ);
                return;
            }

            AddConnectionToDesJoint(desJId);
        }
    }
}

