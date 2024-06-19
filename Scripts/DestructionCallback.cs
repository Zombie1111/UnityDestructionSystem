using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DestructionCallback : MonoBehaviour
{
    public bool includeChildren = true;
    public bool canBeMainParent = false;
    public int maxConnectionCount = 5;
}
