using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR;
using System.Collections.Generic;


/// Manages the demo video portion of the experiment flow.
///
/// Sequence:
///   1. On Start, the "Start Demo Video" button is shown and the Begin button is hidden.
///   2. The participant aims the controller ray at the Start Demo Video button
///      and clicks it with the trigger.
///   3. The video is prepared and played on the virtual projector screen
///      using a RenderTexture applied to the screen's material.
///   4. When the video finishes, the Begin button appears so the participant
///      can proceed to the dot-pattern experiment.
///
/// Requires a VideoPlayer component on the same GameObject.

[RequireComponent(typeof(VideoPlayer))]
public class VideoScreenPlayer : MonoBehaviour
{
    // ── Inspector Fields ───────────────────────────────────────────────────

    [Header("Video Settings")]
    // The video clip to play on the projector screen
    public VideoClip videoClip;
    // Whether the video should loop after finishing (normally false for the experiment)
    public bool loopVideo = false;

    [Header("Screen Settings")]
    // The Renderer component of the virtual projector screen object
    public Renderer screenRenderer;
    // The shader property name for the screen's texture (URP uses "_BaseMap")
    public string materialTextureName = "_BaseMap";

    [Header("Scene References")]
    // The "Start Demo Video" button  shown at startup, hidden when video begins
    public GameObject startDemoButton;
    // The "Begin Experiment" button — hidden at startup, shown when video ends
    public GameObject beginButton;

    [Header("Controller")]
    // The Transform of the right-hand VR controller, used to cast the aim ray
    public Transform controllerTransform;

    // ── Private State ──────────────────────────────────────────────────────

    // The VideoPlayer component attached to this GameObject
    private VideoPlayer videoPlayer;
    // RenderTexture used as the video output target, applied to the screen material
    private RenderTexture renderTexture;
    // The detected right-hand XR input device
    private InputDevice rightDevice;
    // Prevents repeated trigger actions from a single button hold
    private bool isTriggerHeld = false;
    // True once the video has started — stops Update() from checking for button clicks
    private bool videoStarted = false;

    // ── Awake ──────────────────────────────────────────────────────────────
    void Awake()
    {
        // Cache the VideoPlayer component required on this GameObject
        videoPlayer = GetComponent<VideoPlayer>();

        // Create a 1080p RenderTexture for the video output
        // This acts as a live "canvas" that the VideoPlayer writes frames into
        renderTexture = new RenderTexture(1920, 1080, 0);
        renderTexture.Create();

        // Configure the VideoPlayer with the assigned clip and settings
        videoPlayer.clip = videoClip;
        videoPlayer.isLooping = loopVideo;
        videoPlayer.playOnAwake = false; // Video should only play when explicitly triggered

        // Apply the RenderTexture to the projector screen's material so the
        // video frames appear on the virtual screen in the scene
        if (screenRenderer != null)
        {
            Material mat = screenRenderer.material;
            mat.SetTexture(materialTextureName, renderTexture);
            // Scale (1,1) means no flip — adjust to (-1,1) if video appears upside down
            mat.SetTextureScale(materialTextureName, new Vector2(1, 1));
        }
        else
        {
            Debug.LogWarning("VideoScreenPlayer: No screen Renderer assigned!");
        }

        // Subscribe to the video end event so the Begin button appears automatically
        videoPlayer.loopPointReached += OnVideoFinished;
    }

    // ── Start ──────────────────────────────────────────────────────────────
    void Start()
    {
        // Hide the Begin button at startup — it should only appear after the video ends
        if (beginButton != null)
            beginButton.SetActive(false);

        // Show the Start Demo Video button so the participant knows to click it
        if (startDemoButton != null)
            startDemoButton.SetActive(true);

        // Attempt to find the right-hand controller device
        TryGetDevice();
    }


    /// Attempts to find and cache the right-hand VR controller device.
    /// Called once at Start and again each frame if the device is not yet valid.

    void TryGetDevice()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right |
            InputDeviceCharacteristics.Controller, devices);

        if (devices.Count > 0)
            rightDevice = devices[0];
    }

    // ── Update ─────────────────────────────────────────────────────────────
    void Update()
    {
        // Once the video has started, nothing else needs to be checked in Update
        if (videoStarted) return;

        // If the controller hasn't been detected yet, keep trying
        if (!rightDevice.isValid)
        {
            TryGetDevice();
            return;
        }

        // Read the trigger axis — treated as pressed if over 50% depressed
        float triggerValue = 0f;
        rightDevice.TryGetFeatureValue(CommonUsages.trigger, out triggerValue);
        bool triggerPressed = triggerValue > 0.5f;
        if (triggerValue > 0f) Debug.Log("Trigger value: " + triggerValue);

        // Only check for button clicks while the Start Demo Video button is visible
        if (startDemoButton != null && startDemoButton.activeSelf)
        {
            Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 10f))
            {
                Debug.Log("Raycast hit: " + hit.collider.gameObject.name);

                // Accept a click if the ray hits the button or any of its children,
                // and the trigger is freshly pressed (not already held from a prior frame)
                if ((hit.collider.gameObject == startDemoButton ||
                     hit.collider.transform.IsChildOf(startDemoButton.transform))
                     && triggerPressed && !isTriggerHeld)
                {
                    isTriggerHeld = true;
                    StartDemoVideo();
                }
            }
        }

        // Clear the held flag once the trigger is released
        if (!triggerPressed) isTriggerHeld = false;
    }

    // ── Video Playback ─────────────────────────────────────────────────────

    /// Called when the participant clicks the Start Demo Video button.
    /// Hides the button, flags videoStarted to stop further Update checks,
    /// and begins preparing the video for playback.

    void StartDemoVideo()
    {
        videoStarted = true;

        // Hide the button so it can't be clicked again
        if (startDemoButton != null)
            startDemoButton.SetActive(false);

        Debug.Log("Preparing video...");

        // Subscribe to the prepare completed event — video plays only once ready,
        // avoiding a black frame or stuttering start
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.Prepare();
    }

   
    /// Called by the VideoPlayer when buffering is complete and the video is
    /// ready to play. Unsubscribes the event and starts playback.

    void OnVideoPrepared(VideoPlayer vp)
    {
        // Unsubscribe immediately to prevent this from firing more than once
        videoPlayer.prepareCompleted -= OnVideoPrepared;
        Debug.Log("Video prepared, playing now.");
        videoPlayer.Play();
    }

    
    /// Called by the VideoPlayer when the video reaches its end point.
    /// Shows the Begin button so the participant can proceed to the experiment.

    void OnVideoFinished(VideoPlayer vp)
    {
        Debug.Log("Demo video finished. Showing BeginButton.");

        // Reveal the Begin button now that the participant has watched the demo
        if (beginButton != null)
            beginButton.SetActive(true);
    }

    // ── Cleanup ────────────────────────────────────────────────────────────

    /// Releases and destroys the RenderTexture when this GameObject is destroyed,
    /// preventing GPU memory leaks.
  
    void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}