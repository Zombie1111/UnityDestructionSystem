using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace zombDestruction
{
    [System.Serializable]
    public class GlobalRbData
    {
        public GlobalRbData ShallowCopy()
        {
            return (GlobalRbData)this.MemberwiseClone();
        }

        [System.NonSerialized] public Rigidbody rb;

        /// <summary>
        /// The mass of the rigidbody as seen by the destruction system
        /// </summary>
        [System.NonSerialized] public float desMass;

        /// <summary>
        /// The mass of the actuall rigidbody
        /// </summary>
        [System.NonSerialized] public float rbMass;

        //Add custom properties here
        //public float buoyancy; //Example
    }


    [System.Serializable]
    public class FracParent
    {
        public Transform parentTrans;

        /// <summary>
        /// The parents rigidbody. mass = (childPartCount * massDensity * phyMainOptions.massMultiplier), isKinematic is updated based on phyMainOptions.MainPhysicsType
        /// </summary>
        public List<FracRb> parentRbs;
        public List<int> partIndexes;

        /// <summary>
        /// The total mass of all parts that uses this parent
        /// </summary>
        public float parentMass;

        /// <summary>
        /// If > 0 the parent is kinematic, usually also the total number of kinematic parts in this parent
        /// </summary>
        public int parentKinematic;

        public float totalTransportCoEfficiency;
        public float totalStiffness;

        [System.Serializable]
        public class FracRb
        {
            public Rigidbody rb;
            public int rbId;
            public float rbDesMass;
            public int rbKinCount;//This can be removed but if we do setPartKinematic() called by user wont work properly
            public int rbPartCount;
            public bool rbIsKinByDefualt;
            public bool rbIsKin;
        }
    }

    [System.Serializable]
    public unsafe struct FracStruct
    {
        /// <summary>
        /// The number of neighbours this part had before anything broke
        /// </summary>
        public byte neighbourPartI_lenght;

        /// <summary>
        /// The part index of each neighbour (Use neighbourPartI_lenght to stays within actuall bounds)
        /// </summary>
        public fixed short neighbourPartI[FracGlobalSettings.maxPartNeighbourCount];

        /// <summary>
        /// The maximum transport capacity this part has ever consumed in % (Example, 0.9 means if it had gotten 10% more force it would have broken)
        /// </summary>
        public float maxTransportUsed;
    }

    [System.Serializable]
    public class FracPart
    {
        /// <summary>
        /// The groupIdInt the part has, used to get what groupData the part uses
        /// </summary>
        public int groupIdInt;

        /// <summary>
        /// All floats that is > 0.0f makes the groupId (If A-B contains all floats in B-A they can be connected, if any float differ they have different groupData)
        /// </summary>
        public List<float> groupId;

        /// <summary>
        /// Each float is a link (If two parts contains the same float they can be connected)
        /// </summary>
        public HashSet<float> groupLinks;

        /// <summary>
        /// The vertex indexes in fracRend.sharedmesh this part uses
        /// </summary>
        public List<int> partColVerts = new();
    }
}
