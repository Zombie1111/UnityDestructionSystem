using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace FractureDestruction
{
    public class FracturePart : MonoBehaviour
    {
        //create varibels
        public FractureThis fractureDaddy;
        /// <summary>
        /// The meshfilter attatched to the part
        /// </summary>
        public MeshFilter meshF;
        public Rigidbody rb;

        private void OnCollisionEnter(Collision collision)
        {
            if (fractureDaddy.fracturePartsParent != transform.parent) fractureDaddy.FN_desImpactCollision(collision, transform.GetSiblingIndex());
        }
    }
}
