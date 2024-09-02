using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using zombDestruction;

public class reckBallRestore : MonoBehaviour
{
    [SerializeField] private Transform ballTrans = null;
    [SerializeField] private DestructableObject desBall = null;
    [SerializeField] private DestructableObject desBuilding = null;
    [SerializeField] private float restoreAfterSec = 5.0f;

    private Vector3 ballOgPos;

    private void Awake()
    {
        ballOgPos = ballTrans.position;
    }

    private float timeSinceLoaded = 1000.0f;

    private void Update()
    {
        timeSinceLoaded += Time.deltaTime;
        if (timeSinceLoaded > restoreAfterSec) RestoreShit();
    }

    private void RestoreShit()
    {
        timeSinceLoaded = 0.0f;

        //ballTrans.position = ballOgPos;
        desBall.TryLoadAssignedSaveState();
        desBuilding.TryLoadAssignedSaveState();
    }
}
