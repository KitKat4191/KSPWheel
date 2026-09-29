using UnityEngine;

namespace KSPWheel
{
    /// <summary>
    /// This class is a wrapper around the KSPWheelCollider class to allow for easier use while debugging in the Unity Editor.<para/>
    /// It will merely instantiate a KSPWheelCollider object and update its internal variables with the ones entered into the Editor Inspector panel.<para/>
    /// Also includes a few display-only variables for debugging in the editor<para/>
    /// Not intended to be useful aside from the intial debugging period, or as a basic example that can be used in the editor; this class has no use in KSP
    /// </summary>
    [AddComponentMenu("Physics/KSPWheel")]
    public class KspWheelComponent : MonoBehaviour
    {
        #region REGION - Unity Editor Inspector Assignable Fields

        // These variables are set onto the KSPWheelCollider object when Start is called,
        // and updated during script OnValidate() to update any changed values from the editor inspector panel

        /// <summary>
        /// The rigidbody that this wheel will apply forces to and sample velocity from
        /// </summary>
        public Rigidbody rigidBody;

        public Transform steeringTransform;

        public Transform suspensionTransform;

        public Transform wheelTransform;

        /// <summary>
        /// The radius of the wheel to simulate; this is the -actual- size to simulate, not a pre-scaled value
        /// </summary>
        public float wheelRadius = 0.5f;

        /// <summary>
        /// The mass of the -wheel- in... kg? tons? NFC
        /// </summary>
        public float wheelMass = 1f; //used to simulate wheel rotational inertia for brakes and friction purposes

        /// <summary>
        /// The length of the suspension travel
        /// </summary>
        public float suspensionLength = 0.5f;

        /// <summary>
        /// The 'target' parameter for suspension; 0 = fully uncompressed, 1 = fully compressed
        /// </summary>
        public float target = 0;

        /// <summary>
        /// The maximum force the suspension will exhert, in newtons
        /// </summary>
        public float spring = 1000;

        /// <summary>
        /// The damping ratio for the suspension spring force
        /// </summary>
        public float damper = 1500;

        /// <summary>
        /// The maximum torque the motor can exhert against the wheel
        /// </summary>
        public float maxMotorTorque = 0;

        /// <summary>
        /// Max RPM limit for wheel; this aids in making sure slips aren't infinite.
        /// Normally the RPM would be limited by the motor redline / max RPM and the current gearing
        /// but simplifying to a singular max-wheel-rpm value for this simple wheel component
        ///   -- yes, even electric motors have a max RPM regardless of power input
        /// </summary>
        public float rpmLimit = 600f;

        /// <summary>
        /// The maximum torque the brakes can exhert against the wheel while attempting to bring its angular velocity to zero
        /// </summary>
        public float maxBrakeTorque = 0;

        /// <summary>
        /// The maximum deflection for the steering of this wheel, in degrees
        /// </summary>
        public float maxSteerAngle = 0;

        /// <summary>
        /// Throttle/motor torque lerp speed
        /// </summary>
        public float throttleResponse = 2;

        /// <summary>
        /// Steering angle lerp speed
        /// </summary>
        public float steeringResponse = 2;

        /// <summary>
        /// Brake torque lerp speed
        /// </summary>
        public float brakeResponse = 2;

        public float springCurve = 0f;

        /// <summary>
        /// The forward friction constant (rolling friction)
        /// </summary>
        public float forwardFrictionCoefficient = 1f;

        /// <summary>
        /// The sideways friction constant
        /// </summary>
        public float sideFrictionCoefficient = 1f;

        /// <summary>
        /// Global surface friction coefficient applied to both forward and sideways friction
        /// </summary>
        public float surfaceFrictionCoefficient = 1f;

        /// <summary>
        /// If should use differential motor input for steering
        /// </summary>
        public bool tankSteer = false;

        /// <summary>
        /// If this wheel should have its steering inverted; to be used on 'right side' wheels
        /// </summary>
        public bool invertSteer = false;

        /// <summary>
        /// If true, this wheel will rotate opposite for torque inputs (e.g. rpm will go negative for positive torque inputs)
        /// </summary>
        public bool invertMotor = false;

        public KspWheelFrictionType frictionModel = KspWheelFrictionType.Standard;

        public KspWheelSweepType sweepType = KspWheelSweepType.Ray;

        public bool debug = false;

        #endregion ENDREGION - Unity Editor Inspector Assignable Fields

        // these variables are updated every fixed-tick after the wheel has been updated
        // used merely to display some info while in the editor for debugging purposes

        #region REGION - Unity Editor Display-Only Variables

        public Vector3 localVelocity;
        public Vector3 localAcceleration;
        public float rpm;
        public float sLong;
        public float sLat;
        public float fSpring;
        public float fDamp;
        public float fLong;
        public float fLat;
        public float comp;

        public bool suspLock = false;

        #endregion ENDREGION - Unity Editor Display Variables

        private KSPWheelCollider _wheelCollider;

        private float _currentMotorTorque;
        private float _currentSteer;
        private float _currentBrakeTorque;

        private GameObject _bumpStopCollider;

        public void Start()
        {
            _wheelCollider = gameObject.AddComponent<KSPWheelCollider>();
            _wheelCollider.Rigid = this.rigidBody;
            _bumpStopCollider = new GameObject("BSC-" + _wheelCollider.name);
            var sc = _bumpStopCollider.AddComponent<SphereCollider>();
            var mat = new PhysicMaterial("TEST");
            mat.bounciness = 0.0f;
            mat.dynamicFriction = 0;
            mat.staticFriction = 0;
            sc.material = mat;
            OnValidate(); //manually call to set all current parameters into wheel collider object
        }

        private void SampleInput()
        {
            float left = Input.GetKey(KeyCode.A) ? -1 : 0;
            float right = Input.GetKey(KeyCode.D) ? 1 : 0;
            float fwd = Input.GetKey(KeyCode.W) ? 1 : 0;
            float rev = Input.GetKey(KeyCode.S) ? -1 : 0;
            float brakeInput = Input.GetKey(KeyCode.Space) ? 1 : 0;
            float forwardInput = fwd + rev;
            float turnInput = left + right;
            if (invertSteer)
            {
                turnInput = -turnInput;
            }

            if (invertMotor)
            {
                forwardInput = -forwardInput;
            }

            if (tankSteer)
            {
                forwardInput = forwardInput + turnInput;
                if (forwardInput > 1)
                {
                    forwardInput = 1;
                }

                if (forwardInput < -1)
                {
                    forwardInput = -1;
                }
            }

            float rpm = _wheelCollider.rpm;
            if (rpm >= rpmLimit && forwardInput > 0)
            {
                forwardInput = 0;
            }
            else if (rpm <= -rpmLimit && forwardInput < 0)
            {
                forwardInput = 0;
            }

            _currentMotorTorque = Mathf.Lerp(_currentMotorTorque, forwardInput * maxMotorTorque,
                throttleResponse * Time.fixedDeltaTime);
            if (forwardInput == 0 && Mathf.Abs(_currentMotorTorque) < 0.25)
            {
                _currentMotorTorque = 0f;
            }

            _currentSteer = Mathf.Lerp(_currentSteer, turnInput * maxSteerAngle, steeringResponse * Time.fixedDeltaTime);
            if (turnInput == 0 && Mathf.Abs(_currentSteer) < 0.25)
            {
                _currentSteer = 0f;
            }

            _currentBrakeTorque = Mathf.Lerp(_currentBrakeTorque, brakeInput * maxBrakeTorque,
                brakeResponse * Time.fixedDeltaTime);
            if (brakeInput == 0 && _currentBrakeTorque < 0.25)
            {
                _currentBrakeTorque = 0f;
            }
        }

        public void FixedUpdate()
        {
            Vector3 targetPos = suspLock ? transform.position - transform.up * suspensionLength : transform.position;
            Vector3 pos = _bumpStopCollider.transform.position;
            Vector3 p = Vector3.Lerp(pos, targetPos, Time.fixedDeltaTime);
            _bumpStopCollider.transform.position = p;

            SampleInput();
            _wheelCollider.motorTorque = _currentMotorTorque;
            _wheelCollider.steeringAngle = _currentSteer;
            _wheelCollider.brakeTorque = _currentBrakeTorque;
            _wheelCollider.updateWheel();
            
            if (steeringTransform)
            {
                steeringTransform.localRotation = Quaternion.AngleAxis(_currentSteer, steeringTransform.up);
            }

            if (suspensionTransform)
            {
                suspensionTransform.position = transform.position -
                                               (suspensionLength - _wheelCollider.compressionDistance) * transform.up;
            }

            if (wheelTransform)
            {
                wheelTransform.Rotate(wheelTransform.right, _wheelCollider.perFrameRotation, Space.World);
            }

            Vector3 prevVel = localVelocity;
            localVelocity = _wheelCollider.wheelLocalVelocity;
            localAcceleration = (prevVel - localVelocity) / Time.fixedDeltaTime;
            fSpring = _wheelCollider.springForce;
            fDamp = _wheelCollider.dampForce;
            rpm = _wheelCollider.rpm;
            sLong = _wheelCollider.longitudinalSlip;
            sLat = _wheelCollider.lateralSlip;
            fLong = _wheelCollider.longitudinalForce;
            fLat = _wheelCollider.lateralForce;
            comp = _wheelCollider.compressionDistance;
            
            //if (debug) print("s/d: " + fSpring + " : " + fDamp);
        }

        public void OnValidate()
        {
            if (!_wheelCollider) return;
            
            _wheelCollider.radius = wheelRadius;
            _wheelCollider.mass = wheelMass;
            _wheelCollider.length = suspensionLength;
            _wheelCollider.spring = spring;
            _wheelCollider.damper = damper;
            _wheelCollider.motorTorque = maxMotorTorque;
            _wheelCollider.brakeTorque = maxBrakeTorque;
            _wheelCollider.forwardFrictionCoefficient = forwardFrictionCoefficient;
            _wheelCollider.sideFrictionCoefficient = sideFrictionCoefficient;
            _wheelCollider.surfaceFrictionCoefficient = surfaceFrictionCoefficient;
            _wheelCollider.sweepType = sweepType;
            _wheelCollider.frictionModel = frictionModel;

            var sc = _bumpStopCollider.GetComponent<SphereCollider>();
            _bumpStopCollider.layer = 26;
            sc.radius = wheelRadius;
            _bumpStopCollider.transform.parent = transform;
            _bumpStopCollider.transform.localPosition = Vector3.zero;
        }

        /// <summary>
        /// Display a visual representation of the wheel in the editor. Unity has no inbuilt gizmo for 
        /// circles, so a sphere is used. Unlike the original WC, I've represented the wheel at top and bottom 
        /// of suspension travel
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, wheelRadius);
            Vector3 pos2 = transform.position + -transform.up * suspensionLength;
            if (_wheelCollider != null)
            {
                pos2 += transform.up * _wheelCollider.compressionDistance;
            }

            Gizmos.DrawWireSphere(pos2, wheelRadius);
            Gizmos.DrawRay(transform.position - transform.up * wheelRadius, -transform.up * suspensionLength);
        }
    }
}
