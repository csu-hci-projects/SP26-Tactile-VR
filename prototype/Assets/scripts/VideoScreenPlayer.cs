using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR;
using System.Collections.Generic;

/// <summary>
/// Manages the study flow:
/// 1. "Start Demo Video" button appears on start
/// 2. Participant rays into it with the controller trigger to play the video
/// 3. Video plays on the Projector screen
/// 4. When video finishes, BeginButton spawns so the experiment can start
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VideoScreenPlayer : MonoBehaviour
{
    [Header("Video Settings")]
    public VideoClip videoClip;
    public bool loopVideo = false;

    [Header("Screen Settings")]
    public Renderer screenRenderer;
    public string materialTextureName = "_BaseMap";

    [Header("Scene References")]
    public GameObject startDemoButton;   // A button that says "Start Demo Video"
    public GameObject beginButton;       // Your existing BeginButton in the scene

    [Header("Controller")]
    public Transform controllerTransform;

    private VideoPlayer videoPlayer;
    private RenderTexture renderTexture;
    private InputDevice rightDevice;
    private bool isTriggerHeld = false;
    private bool videoStarted = false;

    void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();

        // Set up RenderTexture
        renderTexture = new RenderTexture(1920, 1080, 0);
        renderTexture.Create();

        videoPlayer.clip = videoClip;
        videoPlayer.isLooping = loopVideo;
        videoPlayer.playOnAwake = false;

        // Apply to screen material, flipped right-side up
        if (screenRenderer != null)
        {
            Material mat = screenRenderer.material;
            mat.SetTexture(materialTextureName, renderTexture);
            mat.SetTextureScale(materialTextureName, new Vector2(1, 1));
        }
        else
        {
            Debug.LogWarning("VideoScreenPlayer: No screen Renderer assigned!");
        }

        // Hook into video end event
        videoPlayer.loopPointReached += OnVideoFinished;
    }

    void Start()
    {
        // Make sure BeginButton is hidden at start
        if (beginButton != null)
            beginButton.SetActive(false);

        // Show the Start Demo Video button
        if (startDemoButton != null)
            startDemoButton.SetActive(true);

        TryGetDevice();
    }

    void TryGetDevice()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right |
            InputDeviceCharacteristics.Controller, devices);

        if (devices.Count > 0)
            rightDevice = devices[0];
    }

    void Update()
    {
        if (videoStarted) return; // Stop checking once video is playing

        if (!rightDevice.isValid)
        {
            TryGetDevice();
            return;
        }

        float triggerValue = 0f;
        rightDevice.TryGetFeatureValue(CommonUsages.trigger, out triggerValue);
        bool triggerPressed = triggerValue > 0.5f;
        if (triggerValue > 0f) Debug.Log("Trigger value: " + triggerValue);

        // Raycast from controller - using -forward to fix backwards controller direction
        if (startDemoButton != null && startDemoButton.activeSelf)
        {
            Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 10f))
            {
                Debug.Log("Raycast hit: " + hit.collider.gameObject.name);
                if ((hit.collider.gameObject == startDemoButton ||
                     hit.collider.transform.IsChildOf(startDemoButton.transform))
                     && triggerPressed && !isTriggerHeld)
                {
                    isTriggerHeld = true;
                    StartDemoVideo();
                }
            }
        }

        if (!triggerPressed) isTriggerHeld = false;
    }

    void StartDemoVideo()
    {
        videoStarted = true;

        if (startDemoButton != null)
            startDemoButton.SetActive(false);

        Debug.Log("Preparing video...");
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.Prepare();
    }

    void OnVideoPrepared(VideoPlayer vp)
    {
        videoPlayer.prepareCompleted -= OnVideoPrepared;
        Debug.Log("Video prepared, playing now.");
        videoPlayer.Play();
    }

    void OnVideoFinished(VideoPlayer vp)
    {
        Debug.Log("Demo video finished. Showing BeginButton.");

        // Show the begin button now that video is done
        if (beginButton != null)
            beginButton.SetActive(true);
    }

    void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}