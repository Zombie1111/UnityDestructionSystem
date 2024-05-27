using UnityEngine;
using System.Collections;
using Unity.Collections;
using Zombie1111_uDestruction;

namespace TrueTrace
{
    public class shootCam : MonoBehaviour
    {

        /*
        Writen by Windexglow 11-13-10.  Use it, edit it, steal it I don't care.  
        Converted to C# 27-02-13 - no credit wanted.
        Simple flycam I made, since I couldn't find any others made public.  
        Made simple to use (drag and drop, done) for regular keyboard layout  
        wasd : basic movement
        shift : Makes camera accelerate
        space : Moves camera on X and Z axis only.  So camera doesn't gain any height*/

        //I made a small change so that I can turn off movement by pressing t
        public bool doMoveCam = true;
        public bool canShoot = true;
        public bool canSlowTime = true;
        float mainSpeed = 1.0f; //regular speed
        float shiftAdd = 25.0f; //multiplied by how long shift is held.  Basically running
        float maxShift = 1000.0f; //Maximum speed when holdin gshift
        public float camSens = 2.5f; //How sensitive it with mouse
        private Vector3 lastMouse = new Vector3(255, 255, 255); //kind of in the middle of the screen, rather than at the top (play)
        private float totalRun = 1.0f;
        private bool StopMovement = true;
        private bool IsPressingT = false;
#if UNITY_2023_1_OR_NEWER
        public PhysicsMaterial phyMat;
#else
        public PhysicMaterial phyMat;
#endif

        //private bool IsLocked = true;

        private FractureGlobalHandler globalF;

        private void Start()
        {
            globalF = GameObject.FindObjectOfType<FractureGlobalHandler>();
        }

        private Rigidbody debugRb;
        private bool gotCod = false;
        private Vector3 plPos;
        private Vector3 plDir;


        private bool doShoot = false;

        private void Update()
        {
            if (doShoot == true && gotCod == false)
            {
                plPos = transform.position;
                plDir = transform.forward;
                gotCod = true;
            }
                //if (debugRb != null) print(debugRb.velocity.magnitude);

            // Get mouse wheel input
            float scrollWheelInput = Input.GetAxis("Mouse ScrollWheel") * Mathf.Lerp(0.032f, 1.6f, Time.timeScale);

            // Adjust speed based on mouse wheel input
            if (canSlowTime == true) Time.timeScale = Mathf.Clamp(Time.timeScale + scrollWheelInput, 0.0f, 1.0f);


            if (Input.GetKeyDown(KeyCode.Mouse0) == true && canShoot == true)
            {
                doShoot = true;
            }

            if (doShoot == true && gotCod == true)
            {
                doShoot = false;
                GameObject newO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                newO.transform.localScale *= 0.2f;
                newO.transform.position = plPos;
                Rigidbody rb = newO.AddComponent<Rigidbody>();
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
#if UNITY_2023_1_OR_NEWER
                rb.linearVelocity = plDir * 20.0f;
#else
                rb.velocity = plDir * 20.0f;
#endif
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.mass = 1.0f;
                debugRb = rb;
                globalF.OnAddOrUpdateRb(rb, 3.0f);
                newO.GetComponent<Collider>().hasModifiableContacts = true;
                newO.GetComponent<Collider>().sharedMaterial = phyMat;
                gotCod = false;
            }

            if (doMoveCam == false) return;
            bool PressedT = Input.GetKey(KeyCode.T);
            if (PressedT && !IsPressingT)
            {
                if (!StopMovement)
                {
                    Cursor.lockState = CursorLockMode.None;
                    StopMovement = true;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    StopMovement = false;
                }
            }
            if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                StopMovement = false;
            }
            if (PressedT)
            {
                IsPressingT = true;
            }
            else { IsPressingT = false; }



            lastMouse = new Vector3(-Input.GetAxisRaw("Mouse Y") * camSens, Input.GetAxisRaw("Mouse X") * camSens, 0);
            lastMouse = new Vector3(transform.eulerAngles.x + lastMouse.x, transform.eulerAngles.y + lastMouse.y, 0);
            if (lastMouse.x < 280)
            {
                if (lastMouse.x < 95)
                {
                    lastMouse.x = Mathf.Min(lastMouse.x, 88);
                }
                else
                {
                    lastMouse.x = Mathf.Max(lastMouse.x, 273);
                }
            }
            if (!StopMovement)
            {
                transform.eulerAngles = lastMouse;

                Vector3 p = GetBaseInput();
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    totalRun += Time.unscaledDeltaTime;
                    p = p * totalRun * shiftAdd;
                    p.x = Mathf.Clamp(p.x, -maxShift, maxShift);
                    p.y = Mathf.Clamp(p.y, -maxShift, maxShift);
                    p.z = Mathf.Clamp(p.z, -maxShift, maxShift);
                }
                else
                {
                    totalRun = Mathf.Clamp(totalRun * 0.5f, 1f, 1000f);
                    p = p * mainSpeed;
                }

                p = p * Time.unscaledDeltaTime;
                Vector3 newPosition = transform.position;
                if (Input.GetKey(KeyCode.Space))
                { //If player wants to move on X and Z axis only
                    transform.Translate(p);
                    newPosition.x = transform.position.x;
                    newPosition.z = transform.position.z;
                    transform.position = newPosition;
                }
                else
                {
                    transform.Translate(p);
                }
            }
        }

        private Vector3 GetBaseInput()
        { //returns the basic values, if it's 0 than it's not active.
            Vector3 p_Velocity = new Vector3();
            if (Input.GetKey(KeyCode.W))
            {
                p_Velocity += new Vector3(0, 0, 1);
            }
            if (Input.GetKey(KeyCode.S))
            {
                p_Velocity += new Vector3(0, 0, -1);
            }
            if (Input.GetKey(KeyCode.A))
            {
                p_Velocity += new Vector3(-1, 0, 0);
            }
            if (Input.GetKey(KeyCode.D))
            {
                p_Velocity += new Vector3(1, 0, 0);
            }
            return p_Velocity;
        }
    }
}