using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class enableOnTrig : MonoBehaviour
{
    public Rigidbody rbToSet;

    private void OnTriggerEnter(Collider other)
    {
        if (rbToSet == null) return;
        rbToSet.useGravity = true;
    }
}
