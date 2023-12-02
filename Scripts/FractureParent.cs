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
            float totalForce = collision.relativeVelocity.magnitude;
            if (totalForce < fractureDaddy.destructionThreshold || fractureDaddy.Run_isTransSelfTransform(collision.transform) == true) return;
            //print(totalForce + " " + collision.GetContact(0).thisCollider.transform.name);
            //if (dontUpdateColData != 1) return;

            ////update col data
            //actualContactCount = collision.GetContacts(contacts);
            //for (int i = 0; i < actualContactCount; i += 1)
            //{
            //    Debug.DrawLine(contacts[i].point, contacts[i].point + contacts[i].normal, Color.red, 0.1f, false);
            //}

            //fractureDaddy.RequestDestruction(contacts.Take(actualContactCount).Select(contact => contact.point).ToArray(), totalForce, collision.relativeVelocity.normalized);
            DoImpactAtPosition(collision.GetContact(0).point, collision.relativeVelocity.normalized, totalForce);
        }

        public void DoImpactAtPosition(Vector3 impPosition, Vector3 impDirection, float impForce)
        {
            fractureDaddy.RequestDestruction(impPosition, impDirection, impForce, thisParentIndex);
        }
    }
}
