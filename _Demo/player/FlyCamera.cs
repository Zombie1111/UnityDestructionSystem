using UnityEngine;
using System.Collections;
     
namespace zombDestruction
{
    public class FlyCamera : MonoBehaviour
    {
        bool isPaused = false;
        bool isPaused2 = false;

        void OnApplicationFocus(bool hasFocus)
        {
            isPaused = !hasFocus;
        }

        void OnApplicationPause(bool pauseStatus)
        {
            isPaused = pauseStatus;
        }
        /*
        Writen by Windexglow 11-13-10.  Use it, edit it, steal it I don't care.  
        Converted to C# 27-02-13 - no credit wanted.
        Simple flycam I made, since I couldn't find any others made public.  
        Made simple to use (drag and drop, done) for regular keyboard layout  
        wasd : basic movement
        shift : Makes camera accelerate
        space : Moves camera on X and Z axis only.  So camera doesn't gain any height*/

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        //I made a small change so that I can turn off movement by pressing t
        float mainSpeed = 1.0f; //regular speed
        float shiftAdd = 25.0f; //multiplied by how long shift is held.  Basically running
        float maxShift = 1000.0f; //Maximum speed when holdin gshift
        public float camSens = 2.5f; //How sensitive it with mouse
        private Vector3 lastMouse = new Vector3(255, 255, 255); //kind of in the middle of the screen, rather than at the top (play)
        private float totalRun= 1.0f;
        private bool StopMovement = true;
        void Update ()
        {
            StopMovement = false;


            if(!isPaused2) lastMouse = new Vector3(-Input.GetAxis("Mouse Y") * camSens, Input.GetAxis("Mouse X") * camSens, 0 );
            else lastMouse = Vector3.zero;
            lastMouse = new Vector3(transform.eulerAngles.x + lastMouse.x , transform.eulerAngles.y + lastMouse.y, 0);
            if(lastMouse.x < 280) {
                if(lastMouse.x < 95) {
                    lastMouse.x = Mathf.Min(lastMouse.x, 88);
                } else {
                    lastMouse.x = Mathf.Max(lastMouse.x, 273);
                }
            }
            if(!StopMovement) {
                    transform.eulerAngles = lastMouse;
               
                Vector3 p = GetBaseInput();
                if (Input.GetKey (KeyCode.LeftShift)){
                    totalRun += Time.unscaledDeltaTime;
                    p  = p * totalRun * shiftAdd;
                    p.x = Mathf.Clamp(p.x, -maxShift, maxShift);
                    p.y = Mathf.Clamp(p.y, -maxShift, maxShift);
                    p.z = Mathf.Clamp(p.z, -maxShift, maxShift);
                }
                else{
                    totalRun = Mathf.Clamp(totalRun * 0.5f, 1f, 1000f);
                    p = p * mainSpeed;
                }
           
                p = p * Time.unscaledDeltaTime;
               Vector3 newPosition = transform.position;
                if (Input.GetKey(KeyCode.Space)){ //If player wants to move on X and Z axis only
                    transform.Translate(p);
                    newPosition.x = transform.position.x;
                    newPosition.z = transform.position.z;
                    transform.position = newPosition;
                }
                else{
                    transform.Translate(p);
                }
            }
            isPaused2 = isPaused;
        }
         
        private Vector3 GetBaseInput() { //returns the basic values, if it's 0 than it's not active.
            Vector3 p_Velocity = new Vector3();
            if (Input.GetKey (KeyCode.W)){
                p_Velocity += new Vector3(0, 0 , 1);
            }
            if (Input.GetKey (KeyCode.S)){
                p_Velocity += new Vector3(0, 0, -1);
            }
            if (Input.GetKey (KeyCode.A)){
                p_Velocity += new Vector3(-1, 0, 0);
            }
            if (Input.GetKey (KeyCode.D)){
                p_Velocity += new Vector3(1, 0, 0);
            }
            return p_Velocity;
        }
    }
}