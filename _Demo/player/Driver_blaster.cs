using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using zombDestruction;

namespace GameLogic
{
    public class Driver_blaster : MonoBehaviour
    {
        [SerializeField] private float bulletMoveSpeed = 20.0f;
        [SerializeField] private float maxShootRange = 100.0f;
        public Transform camTrans;
        public LayerMask maskGround;
        public Transform gunCenter;
        public float rbMass = 1.0f;
        public float virtualMass = 3.0f;
        private zombDestruction.DestructionHandler globalF;

        private void Start()
        {
            globalF = GameObject.FindAnyObjectByType<zombDestruction.DestructionHandler>(FindObjectsInactive.Include);
        }

        /// <summary>
        /// Shoots a bullet if possible
        /// </summary>
        /// <returns>True if was able to shoot</returns>
        public bool TryToShoot()
        {
            //get bullet shoot dir
            Vector3 endPos = camTrans.transform.position + (camTrans.transform.forward * maxShootRange);
            if (Physics.Linecast(camTrans.transform.position, endPos, out RaycastHit nHit, maskGround, QueryTriggerInteraction.Ignore) == true) endPos = nHit.point;
            Vector3 shootDir = (endPos - gunCenter.position).normalized;

            //create the bullet
            GameObject newO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            newO.transform.localScale *= 0.2f;
            newO.transform.position = gunCenter.position;
            newO.layer = 5;
            Rigidbody rb = newO.AddComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
#if UNITY_2023_3_OR_NEWER
            rb.linearVelocity = shootDir * bulletMoveSpeed;
#else
            rb.velocity = shootDir * bulletMoveSpeed;
#endif
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.mass = rbMass;

            globalF.OnAddOrUpdateRb(new() { desMass = virtualMass, rbMass = rbMass, rb = rb });

            return true;
        }

        Collider grabbedCol = null;
        Vector3 gPosL;

        private void Update()
        {
            // Get mouse wheel input
            float scrollWheelInput = Input.GetAxis("Mouse ScrollWheel") * Mathf.Lerp(0.032f, 1.6f, Time.timeScale);

            // Adjust speed based on mouse wheel input
            Time.timeScale = Mathf.Clamp(Time.timeScale + scrollWheelInput, 0.0f, 1.0f);

            if (Input.GetKeyDown(KeyCode.Mouse1) == true) TryToShoot();

            if (Input.GetKeyDown(KeyCode.Mouse0) == true)
            {
                if (Physics.Raycast(camTrans.position, camTrans.forward, out RaycastHit nHit, 100.0f, maskGround, QueryTriggerInteraction.Ignore) == true)
                {
                    grabbedCol = nHit.collider;
                    if (grabbedCol.attachedRigidbody != null) gPosL = grabbedCol.attachedRigidbody.transform.InverseTransformPoint(nHit.point);
                }
            }

            if (Input.GetKeyUp(KeyCode.Mouse0) == true) grabbedCol = null;

            if (Input.GetKeyDown(KeyCode.X) == true)
            {
                if (Physics.Raycast(camTrans.position, camTrans.forward, out RaycastHit nHit, 100.0f, maskGround, QueryTriggerInteraction.Ignore) == true)
                {
                    DestructionHandler.TryGetDestructionHandler(gameObject).RegisterExplosion(
                        nHit.point + (nHit.normal * 0.51f), nHit.normal, explosionForce, explosionSpeed, maskGround, out _, out _, 0.5f, explosionRadius, 32);
                }
            }
        }

        public float holdDis = 3.0f;
        public float explosionForce = 3200.0f;
        public float explosionSpeed = 8.0f;
        public float explosionRadius = 5.0f;

        private void FixedUpdate()
        {
            if (grabbedCol == null) return;
            Rigidbody grabbedRb = grabbedCol.attachedRigidbody;
            if (grabbedRb == null) return;

            //move grabbed object
            //get current target hold position
            Vector3 objPos = grabbedRb.transform.TransformPoint(gPosL);
            float currentHDis = holdDis;

            //Physics.Raycast(camTrans.position, camTrans.forward, out RaycastHit nHit, currentHDis, maskGround, QueryTriggerInteraction.Ignore);
            //Vector3 holdPos = nHit.collider != null ? nHit.point : (camTrans.position + (camTrans.forward * currentHDis));
            Vector3 holdPos = camTrans.position + (camTrans.forward * currentHDis);

            Vector3 velToGive = Vector3.ClampMagnitude(holdPos - objPos, 20.0f) * 8.0f;
            zombDestruction.FracHelpFunc.SetVelocityUsingForce(velToGive, grabbedRb);
            zombDestruction.FracHelpFunc.SetAngularVelocityUsingTorque(grabbedRb.angularVelocity * 0.5f, grabbedRb);
        }
    }
}


