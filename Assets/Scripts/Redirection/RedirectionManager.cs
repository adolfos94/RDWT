using UnityEngine;
using System.Collections;
using Redirection;

[RequireComponent(typeof(Redirector), typeof(Resetter), typeof(ResetTrigger))]
public class RedirectionManager : MonoBehaviour
{
    public enum MovementController
    { Keyboard, AutoPilot, Tracker };

    [Tooltip("How user movement is controlled.")]
    public MovementController MOVEMENT_CONTROLLER = MovementController.Tracker;

    [Tooltip("Maximum translation gain applied")]
    [Range(0, 5)]
    public float MAX_TRANS_GAIN = 0.26F;

    [Tooltip("Minimum translation gain applied")]
    [Range(-0.99F, 0)]
    public float MIN_TRANS_GAIN = -0.14F;

    [Tooltip("Maximum rotation gain applied")]
    [Range(0, 5)]
    public float MAX_ROT_GAIN = 0.49F;

    [Tooltip("Minimum rotation gain applied")]
    [Range(-0.99F, 0)]
    public float MIN_ROT_GAIN = -0.2F;

    [Tooltip("Radius applied by curvature gain")]
    [Range(1, 23)]
    public float CURVATURE_RADIUS = 7.5F;

    [Tooltip("The game object that is being physically tracked (probably user's head)")]
    public Transform headTransform;

    [Tooltip("Use simulated framerate in auto-pilot mode")]
    public bool useManualTime = false;

    [Tooltip("Target simulated framerate in auto-pilot mode")]
    public float targetFPS = 60;

    public Transform body;

    public Transform trackedSpace;

    [HideInInspector]
    public Transform simulatedHead;

    [HideInInspector]
    public Redirector redirector;

    [HideInInspector]
    public Resetter resetter;

    [HideInInspector]
    public ResetTrigger resetTrigger;

    [HideInInspector]
    public TrailDrawer trailDrawer;

    [HideInInspector]
    public SimulationManager simulationManager;

    [HideInInspector]
    public SimulatedWalker simulatedWalker;

    [HideInInspector]
    public KeyboardController keyboardController;

    [HideInInspector]
    public SnapshotGenerator snapshotGenerator;

    [HideInInspector]
    public StatisticsLogger statisticsLogger;

    [HideInInspector]
    public HeadFollower bodyHeadFollower;

    [HideInInspector]
    public Vector3 currPos, currPosReal, prevPos, prevPosReal;

    [HideInInspector]
    public Vector3 currDir, currDirReal, prevDir, prevDirReal;

    [HideInInspector]
    public Vector3 deltaPos;

    [HideInInspector]
    public float deltaDir;

    [HideInInspector]
    public Transform targetWaypoint;

    [HideInInspector]
    public bool runInTestMode = false;

    [HideInInspector]
    public bool inReset = false;

    private float simulatedTime = 0;

    private void Awake()
    {
        GetTrackedSpace();
        GetSimulatedHead();

        GetSimulationManager(); //?
        simulationManager.Initialize(); //?

        GetRedirector();
        GetResetter();
        GetResetTrigger();

        GetTrailDrawer();
        GetKeyboardController();
        GetBodyHeadFollower();

        SetBodyReferenceForResetTrigger();

        if (resetTrigger != null)
            resetTrigger.Initialize();

        if (resetter != null)
            resetter.Initialize();

        headTransform = simulatedHead;
    }

    // Use this for initialization
    private void Start()
    {
        simulatedTime = 0;
        UpdatePreviousUserState();

        if (useManualTime)
            Application.targetFrameRate = (int)targetFPS;
    }

    // Update is called once per frame
    private void Update()
    {
    }

    private void LateUpdate()
    {
        simulatedTime += 1.0f / targetFPS;

        UpdateCurrentUserState();
        CalculateStateChanges();

        // BACK UP IN CASE UNITY TRIGGERS FAILED TO COMMUNICATE RESET (Can happen in high speed simulations)
        if (resetter != null && !inReset && resetter.IsUserOutOfBounds())
        {
            Debug.LogWarning("Reset Aid Helped!");
            OnResetTrigger();
        }

        if (inReset)
        {
            if (resetter != null)
            {
                resetter.ApplyResetting();
            }
        }
        else
        {
            if (redirector != null)
            {
                redirector.ApplyRedirection();
            }
        }

        UpdatePreviousUserState();
        UpdateBodyPose();
    }

    public float GetDeltaTime()
    {
        if (useManualTime)
            return 1.0f / targetFPS;
        else
            return Time.deltaTime;
    }

    public float GetTime()
    {
        if (useManualTime)
            return simulatedTime;
        else
            return Time.time;
    }

    private void UpdateBodyPose()
    {
        body.position = Utilities.FlattenedPos3D(headTransform.position);
        body.rotation = Quaternion.LookRotation(Utilities.FlattenedDir3D(headTransform.forward), Vector3.up);
    }

    /// <summary>
    /// Finds Tracked Space GameObject
    /// </summary>
    private void GetTrackedSpace()
    {
        trackedSpace = transform.Find("Tracked Space");
    }

    /// <summary>
    /// Finds Simulated User Head
    /// </summary>
    private void GetSimulatedHead()
    {
        simulatedHead = transform.Find("Simulated User").Find("Head");
    }

    /// <summary>
    /// Get Redirector Component
    /// </summary>
    private void GetRedirector()
    {
        redirector = this.gameObject.GetComponent<Redirector>();
        redirector.redirectionManager = this;
    }

    /// <summary>
    /// Get Resseter Component
    /// </summary>
    private void GetResetter()
    {
        resetter = this.gameObject.GetComponent<Resetter>();
        resetter.redirectionManager = this;
    }

    /// <summary>
    /// Get Resseter Trigger
    /// </summary>
    private void GetResetTrigger()
    {
        resetTrigger = this.gameObject.GetComponentInChildren<ResetTrigger>();
        resetTrigger.redirectionManager = this;
    }

    /// <summary>
    /// Get Trail Drawer
    /// </summary>
    private void GetTrailDrawer()
    {
        // TODO: See whats this
        trailDrawer = this.gameObject.GetComponent<TrailDrawer>();
        trailDrawer.redirectionManager = this;
    }

    /// <summary>
    /// Get Keyboard Controller for Unity Play Mode
    /// </summary>
    private void GetKeyboardController()
    {
        keyboardController = simulatedHead.GetComponent<KeyboardController>();
        keyboardController.redirectionManager = this;
    }

    /// <summary>
    /// Get Body-Head follower.
    /// </summary>
    private void GetBodyHeadFollower()
    {
        bodyHeadFollower = body.GetComponent<HeadFollower>();
        bodyHeadFollower.redirectionManager = this;
    }

    /// <summary>
    /// Set Body's Capsule Collider reference
    /// </summary>
    private void SetBodyReferenceForResetTrigger()
    {
        if (resetTrigger == null || body == null)
            return;

        resetTrigger.bodyCollider = body.GetComponentInChildren<CapsuleCollider>();
    }

    private void GetSimulationManager()
    {
        simulationManager = this.gameObject.GetComponent<SimulationManager>();
        simulationManager.redirectionManager = this;
    }

    private void UpdateCurrentUserState()
    {
        currPos = Utilities.FlattenedPos3D(headTransform.position);
        currPosReal = Utilities.GetRelativePosition(currPos, this.transform);
        currDir = Utilities.FlattenedDir3D(headTransform.forward);
        currDirReal = Utilities.FlattenedDir3D(Utilities.GetRelativeDirection(currDir, this.transform));
    }

    private void UpdatePreviousUserState()
    {
        prevPos = Utilities.FlattenedPos3D(headTransform.position);
        prevPosReal = Utilities.GetRelativePosition(prevPos, this.transform);
        prevDir = Utilities.FlattenedDir3D(headTransform.forward);
        prevDirReal = Utilities.FlattenedDir3D(Utilities.GetRelativeDirection(prevDir, this.transform));
    }

    private void CalculateStateChanges()
    {
        deltaPos = currPos - prevPos;
        deltaDir = Utilities.GetSignedAngle(prevDir, currDir);
    }

    public void OnResetTrigger()
    {
        //print("RESET TRIGGER");
        if (inReset)
            return;
        //print("NOT IN RESET");
        //print("Is Resetter Null? " + (resetter == null));
        if (resetter != null && resetter.IsResetRequired())
        {
            //print("RESET WAS REQUIRED");
            resetter.InitializeReset();
            inReset = true;
        }
    }

    public void OnResetEnd()
    {
        //print("RESET END");
        resetter.FinalizeReset();
        inReset = false;
    }

    public void RemoveRedirector()
    {
        this.redirector = this.gameObject.GetComponent<Redirector>();
        if (this.redirector != null)
            Destroy(redirector);
        redirector = null;
    }

    public void RemoveResetter()
    {
        this.resetter = this.gameObject.GetComponent<Resetter>();
        if (this.resetter != null)
            Destroy(resetter);
        resetter = null;
    }

    public void UpdateRedirector(System.Type redirectorType)
    {
        RemoveRedirector();
        this.redirector = (Redirector)this.gameObject.AddComponent(redirectorType);
        GetRedirector();
    }

    public void UpdateResetter(System.Type resetterType)
    {
        RemoveResetter();

        this.resetter = (Resetter)this.gameObject.AddComponent(resetterType);
        GetResetter();
        if (this.resetter != null)
            this.resetter.Initialize();
    }

    public void UpdateTrackedSpaceDimensions(float x, float z)
    {
        trackedSpace.localScale = new Vector3(x, 1, z);
        resetTrigger.Initialize();
        if (this.resetter != null)
            this.resetter.Initialize();
    }
}