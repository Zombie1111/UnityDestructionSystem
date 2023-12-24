using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Zombie1111_uDestruction
{
    public class FractureParent : MonoBehaviour
    {
        public int actualContactCount = 0;
        public ContactPoint[] contacts = new ContactPoint[4];
        public FractureThis fractureDaddy = null;
        public int thisParentIndex = -1;

        private void OnCollisionEnter(Collision collision)
        {
            OnCollision(collision);
        }

        //private void OnCollisionStay(Collision collision)
        //{
        //    OnCollision(collision);
        //}

        private void OnCollision(Collision collision)
        {
            float totalForce = collision.relativeVelocity.magnitude
                * (collision.rigidbody == null ? fractureDaddy.allFracParents[thisParentIndex].parentRb.mass : collision.rigidbody.mass);

            if (totalForce < fractureDaddy.destructionThreshold) return;
            totalForce *= fractureDaddy.Run_getDamageMultiplier(collision.rigidbody);
            if (totalForce < fractureDaddy.destructionThreshold) return;

            //print(totalForce + " " + collision.GetContact(0).thisCollider.transform.name);
            //if (dontUpdateColData != 1) return;

            ////update col data
            //actualContactCount = collision.GetContacts(contacts);
            //for (int i = 0; i < actualContactCount; i += 1)
            //{
            //    Debug.DrawLine(contacts[i].point, contacts[i].point + contacts[i].normal, Color.red, 0.1f, false);
            //}

            //fractureDaddy.RequestDestruction(contacts.Take(actualContactCount).Select(contact => contact.point).ToArray(), totalForce, collision.relativeVelocity.normalized);
            DoImpactAtPosition(collision.GetContact(0).point, collision.relativeVelocity.normalized, totalForce, collision.collider);
        }

        public void DoImpactAtPosition(Vector3 impPosition, Vector3 impDirection, float impForce, Collider collidedWith = null)
        {
            fractureDaddy.RequestDestruction(impPosition, impDirection, impForce, thisParentIndex
                , collidedWith == null || (collidedWith.attachedRigidbody != null && collidedWith.attachedRigidbody.isKinematic == false));
        }
    }
}
