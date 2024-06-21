using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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

    [System.Serializable]
    public class FracCallbackEvent
    {
        [Header("Invoke Configuration")]
        public float invokeWhenBrokenPercentageHasChangedBy = 0.7f;
        public float alwaysInvokeIfOnMessuredSideOf = 0.9f;
        public BrokenMessureType onlyMessure = BrokenMessureType.PositiveChange;
        public bool onlyInvokeOnce = false;
        public bool resetInvokedStatusWhenOppositeChange = true;

        [Space()]
        [Header("Invoke Events")]
        public UnityEvent unityEventToInvoke;
        /// <summary>
        /// Gets invoked if percentageChanged is > invokeWhenBrokenPercentageHasChangedBy and if currentBrokenPercentage </or> alwaysInvokeIfOnMessuredSideOf
        /// </summary>
        /// <param name="percentageChanged">How much it has changed since last invoke (0.0f - 1.0f)</param>
        /// <param name="currentBrokenPercentage">How broken it is (0.0f - 1.0f)</param>
        public delegate void Event_OnPercentageChanged(float percentageChanged, float currentBrokenPercentage);

        /// <summary>
        /// Gets invoked if percentageChanged is > invokeWhenBrokenPercentageHasChangedBy and if currentBrokenPercentage </or> alwaysInvokeIfOnMessuredSideOf
        /// </summary>
        public event Event_OnPercentageChanged OnDestructionCallback;

        [System.NonSerialized] public float lastInvokeValue = 0.0f;
        [System.NonSerialized] public bool hasBeenInvoked = false;

        public enum BrokenMessureType
        {
            PositiveChange,
            NegativeChange,
            AnyChance
        }

        /// <summary>
        /// Invokes all events if not already invoked
        /// </summary>
        public void TryInvokeEvents(float percentageChanged, float currentBrokenPercentage)
        {
            lastInvokeValue = currentBrokenPercentage;
            if (onlyInvokeOnce == true && hasBeenInvoked == true) return;
            
            unityEventToInvoke.Invoke();
            OnDestructionCallback?.Invoke(percentageChanged, currentBrokenPercentage);
            hasBeenInvoked = true;
        }
    }

    public unsafe struct DestructionSource
    {
        /// <summary>
        /// The velocity that can be applied at most to parts that breaks and their parent, the opposite of this should be applied to rbSource(If any)(If == vector.zero, it will be like a chock wave)
        /// </summary>
        public Vector3 impVel;
        public bool isExplosion;

        /// <summary>
        /// The total force applied to this object (Should be equal to all impPoints_force added togehter)
        /// </summary>
        public float impForceTotal;

        public void* desPoints_ptr;
        public int desPoints_lenght;

        /// <summary>
        /// The index of the parent all parts in this source has
        /// </summary>
        public int parentI;

        public Matrix4x4 parentLToW_now;
        public Matrix4x4 parentWToL_prev;

        public Vector3 centerImpPos;
    }

    public struct DestructionPoint
    {
        public Vector3 impPosW;
        public float force;
        public int partI;
        public float disToWall;
    }
}
