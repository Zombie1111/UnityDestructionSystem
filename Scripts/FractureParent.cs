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

        private void OnCollisionEnter(Collision collision)
        {
            OnCollision(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            OnCollision(collision);
        }

        private void OnCollision(Collision collision)
        {
            float totalForce = collision.relativeVelocity.magnitude;
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
            fractureDaddy.RequestDestruction(collision.GetContact(0).point, totalForce, collision.relativeVelocity.normalized);
        }
    }
}
