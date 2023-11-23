using FractureDestruction;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FractureCollisionEnter : MonoBehaviour
{
    public FractureThis fractureDaddy;

    private void OnCollisionEnter(Collision collision)
    {
        fractureDaddy.FN_desImpactCollision(collision, -1);
    }
}
