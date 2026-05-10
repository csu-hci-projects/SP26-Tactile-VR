using UnityEngine;
using UnityEngine.XR;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Collections.Generic;


/// Manages the VR tutorial scene 
///
/// Guides participants through four phases before entering the main experiment:
///   Phase 1 - Movement and Hover:
///             Teaches the participant to move around and aim the ray controller.
///             A button is visible hover it, then click it to proceed.
///
///   Phase 2 - Dot Practice:
///             Button is hidden. Participant selects one dot and drags to another
///             to practice the connection mechanic.
///
///   Phase 3 - Full Pattern:
///             Button is hidden. Participant connects all dots in sequence,
///             closing the loop to complete the pattern.
///
///   Phase 4 - Begin Experiment:
///             Button reappears labeled "Begin Experiment". Participant may
///             practice the full pattern as many times as desired before
///             clicking the button to load the experiment scene.
///
///  also allows for audio to be used for accessability 
public class DemoManager : MonoBehaviour
{
    [Header("Controller")]
    // The Transform of the right-hand VR controller, used to cast the aim ray
    public Transform controllerTransform;

    [Header("The Button")]
    // The button GameObject that participants hover and click
    public GameObject theButton;
    // The TextMeshPro label on the button (e.g. "Hover Here!" or "Begin Experiment")
    public TMP_Text buttonLabel;
    // Button colors for its three states: idle, hovered, and clicked
    public Color buttonDefaultColor = Color.white;
    public Color buttonHoverColor = Color.red;
    public Color buttonClickedColor = Color.green;

    [Header("Perimeter Dots (8 dots clockwise)")]
    // Array of all dot GameObjects arranged around the whiteboard perimeter
    public GameObject[] perimeterDots;
    // Material used to render the lines drawn between dots
    public Material lineMaterial;
    // Dot colors for each interaction state
    public Color dotDefaultColor = Color.black;
    public Color dotHoverColor = Color.red;
    public Color dotSelectedColor = Color.yellow;
    public Color dotCompleteColor = Color.green;

    [Header("Instruction Display")]
    // The TextMeshPro text element that shows on-screen instructions to the participant
    public TMP_Text instructionDisplay;

    [Header("Experiment Scene")]
    // Name of the Unity scene to load when the participant clicks "Begin Experiment"
    public string experimentSceneName = "SampleScene";

    [Header("Audio - drag clips in later, no code changes needed")]
    // Optional audio clips that play at the start of each phase
    public AudioClip phase1Audio;
    public AudioClip phase2Audio;
    public AudioClip phase3Audio;
    public AudioClip phase4Audio;
    [Tooltip("Delay in seconds before audio plays after each phase begins.")]
    public float audioDelay = 0.01f;
    // AudioSource component added at runtime to play the clips
    private AudioSource audioSource;



    // Defines the four tutorial phases in order
    private enum Phase { Phase1_MovementAndHover, Phase2_DotPractice, Phase3_FullPattern, Phase4_Begin }
    // Tracks which phase the participant is currently in
    private Phase currentPhase = Phase.Phase1_MovementAndHover;


    // The detected right-hand XR input device
    private InputDevice rightDevice;
    // Prevents repeated trigger actions from a single button hold
    private bool isTriggerHeld = false;
    // Tracks whether the ray was hovering the button last frame (for Phase 1 instruction changes)
    private bool wasHoveringButton = false;

    // Cached instance material for the button — allows runtime color changes
    private Material buttonMat;
    // Cached instance materials for each dot keyed by GameObject
    private Dictionary<GameObject, Material> dotMats = new Dictionary<GameObject, Material>();

    // ── Dot Drawing State ──────────────────────────────────────────────────

    // The dot currently selected (held) by the participant
    private GameObject currentDot = null;
    // The last valid dot hovered while the trigger was held (used as release fallback)
    private GameObject lastHoveredNewDot = null;
    // All LineRenderer objects drawn so far in this attempt
    private List<LineRenderer> drawnLines = new List<LineRenderer>();
    // Ordered list of dots visited in the current pattern attempt
    private List<GameObject> visitedDots = new List<GameObject>();

    // Phase 2: tracks the last dot hovered while dragging, so a slightly
    // imprecise release still registers as a valid connection
    private GameObject phase2LastHoveredTarget = null;

    // True once the participant has connected back to the first dot, closing the loop
    private bool loopClosed = false;

    // Running count of how many times the participant has completed the full pattern
    private int patternCompletions = 0;


    // A temporary line shown while the participant is dragging between dots
    private LineRenderer previewLine;

    // ── Start ──────────────────────────────────────────────────────────────
    void Start()
    {
        // Add an AudioSource component to this GameObject at runtime
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        // Attempt to find the right-hand controller device
        TryGetDevice();

        // Create a unique material instance for the button so color changes
        // don't affect all objects sharing the same material asset
        if (theButton != null)
        {
            var r = theButton.GetComponent<Renderer>();
            if (r != null) { buttonMat = new Material(r.sharedMaterial); r.material = buttonMat; }
        }

        // Create unique material instances for each dot for the same reason
        foreach (var dot in perimeterDots)
        {
            if (dot == null) continue;
            var r = dot.GetComponent<Renderer>();
            if (r != null) { var m = new Material(r.sharedMaterial); r.material = m; dotMats[dot] = m; }
        }

        // Create the preview line shown while dragging from dot to dot
        var previewObj = new GameObject("PreviewLine");
        previewLine = previewObj.AddComponent<LineRenderer>();
        previewLine.material = lineMaterial;
        previewLine.startWidth = 0.05f;
        previewLine.endWidth = 0.05f;
        previewLine.startColor = Color.yellow;
        previewLine.endColor = Color.yellow;
        previewLine.positionCount = 2;
        previewLine.useWorldSpace = true;
        // Default positions — updated every frame during dragging
        previewLine.SetPosition(0, Vector3.zero);
        previewLine.SetPosition(1, Vector3.forward);
        previewLine.enabled = false; // Hidden until the participant starts dragging

        // Reset all dots to their default color
        foreach (var dot in perimeterDots)
            SetDotColor(dot, dotDefaultColor);

        // Start the tutorial at Phase 1
        GoToPhase(Phase.Phase1_MovementAndHover);
    }

  
    /// Attempts to find and cache the right-hand VR controller device.
    /// Called once at Start and again each frame if the device is not yet valid.

    void TryGetDevice()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, devices);
        if (devices.Count > 0) rightDevice = devices[0];
    }

    // ── Update ─────────────────────────────────────────────────────────────
    void Update()
    {
        // If the controller hasn't been detected yet, keep trying
        if (!rightDevice.isValid) { TryGetDevice(); return; }

        // Read the trigger axis — treated as pressed if over 50% depressed
        float triggerValue = 0f;
        rightDevice.TryGetFeatureValue(CommonUsages.trigger, out triggerValue);
        bool triggerPressed = triggerValue > 0.5f;

        // Cast a ray from the controller tip forward (max 10 metres)
        Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);
        RaycastHit hit;
        bool didHit = Physics.Raycast(ray, out hit, 10f);
        GameObject hitObj = didHit ? hit.collider.gameObject : null;

        // Delegate to the current phase's update method
        switch (currentPhase)
        {
            case Phase.Phase1_MovementAndHover: UpdatePhase1(hitObj, triggerPressed); break;
            case Phase.Phase2_DotPractice:      UpdatePhase2(hitObj, triggerPressed); break;
            case Phase.Phase3_FullPattern:      UpdatePhase3(hitObj, triggerPressed); break;
            case Phase.Phase4_Begin:            UpdatePhase4(hitObj, triggerPressed); break;
        }

        // Clear the held flag once the trigger is released
        if (!triggerPressed) isTriggerHeld = false;
    }


    /// Phase 1: Teaches movement and controller aiming.
    /// The participant must hover the button (changing its color) and then click it.
    /// Advances to Phase 2 on a successful click.
 
    void UpdatePhase1(GameObject hitObj, bool triggerPressed)
    {
        // Check if the ray is pointing at the button or any of its children
        bool onButton = hitObj == theButton ||
                        (hitObj != null && hitObj.transform.IsChildOf(theButton.transform));

        if (onButton)
        {
            // Highlight the button to give visual feedback that it is being aimed at
            SetButtonColor(buttonHoverColor);

            // Show the click instruction the first time the participant hovers the button
            if (!wasHoveringButton)
            {
                wasHoveringButton = true;
                SetInstruction("Great job! The button changed color.\nThat means your ray is pointing at it!\n\nNow squeeze the RIGHT trigger to click it.");
            }

            // Advance to Phase 2 on a fresh trigger press while hovering
            if (triggerPressed && !isTriggerHeld)
            {
                isTriggerHeld = true;
                SetButtonColor(buttonClickedColor);
                GoToPhase(Phase.Phase2_DotPractice);
            }
        }
        else
        {
            // Return button to default color when the ray moves away
            SetButtonColor(buttonDefaultColor);

            // If they moved off the button, remind them what to do
            if (wasHoveringButton)
            {
                wasHoveringButton = false;
                SetInstruction("Welcome to the experiment!\n\nUse the LEFT joystick to move around.\nUse the RIGHT joystick to turn.\n\nWhen comfortable, point your pink ray\nat the button labeled Hover Here!");
            }
        }
    }

  
    /// Phase 2: Teaches the dot-connection mechanic using just two dots.
    /// Participant selects one dot (trigger down), drags to another, and releases.
    /// Advances to Phase 3 on a successful two-dot connection.
  
    void UpdatePhase2(GameObject hitObj, bool triggerPressed)
    {
        GameObject hoveredDot = GetDot(hitObj);

        // While dragging, keep track of the last valid target dot hovered.
        // This prevents a slightly imprecise release from failing the connection.
        if (triggerPressed && isTriggerHeld && hoveredDot != null && hoveredDot != currentDot)
            phase2LastHoveredTarget = hoveredDot;

        // Update dot colors to reflect hover and selection state
        foreach (var dot in perimeterDots)
        {
            if (dot == currentDot)      SetDotColor(dot, dotSelectedColor); // Currently held dot
            else if (dot == hoveredDot) SetDotColor(dot, dotHoverColor);    // Dot being aimed at
            else                        SetDotColor(dot, dotDefaultColor);  // All other dots
        }

        if (triggerPressed)
        {
            if (!isTriggerHeld)
            {
                // Fresh trigger press on a dot select it as the start of the connection
                if (hoveredDot != null)
                {
                    isTriggerHeld = true;
                    currentDot = hoveredDot;
                    phase2LastHoveredTarget = null;
                    SetDotColor(currentDot, dotSelectedColor);
                    previewLine.enabled = true;
                    SetInstruction("Dot selected!\nNow drag to another dot and release the trigger.");
                }
            }
            else if (currentDot != null)
            {
                // While holding trigger, update the preview line from the selected dot
                // toward wherever the participant is pointing
                previewLine.SetPosition(0, currentDot.transform.position);

                // Prefer the currently hovered dot; fall back to the last valid one
                GameObject previewTarget = (hoveredDot != null && hoveredDot != currentDot)
                    ? hoveredDot
                    : phase2LastHoveredTarget;

                if (previewTarget != null)
                    previewLine.SetPosition(1, previewTarget.transform.position);
                else
                    // No valid target extend the line to a point in front of the controller
                    previewLine.SetPosition(1, controllerTransform.position + controllerTransform.forward * 5f);
            }
        }
        else
        {
            // Trigger released hide the preview line
            previewLine.enabled = false;

            if (isTriggerHeld && currentDot != null)
            {
                isTriggerHeld = false;

                // Resolve the release target (hovered dot or last valid fallback)
                GameObject targetDot = (hoveredDot != null && hoveredDot != currentDot)
                    ? hoveredDot
                    : phase2LastHoveredTarget;

                if (targetDot != null && targetDot != currentDot)
                {
                    // Valid connection draw the line and advance to Phase 3
                    DrawLine(currentDot.transform.position, targetDot.transform.position, dotCompleteColor);
                    SetDotColor(currentDot, dotCompleteColor);
                    SetDotColor(targetDot, dotCompleteColor);
                    currentDot = null;
                    phase2LastHoveredTarget = null;
                    GoToPhase(Phase.Phase3_FullPattern);
                }
                else
                {
                    // Invalid release reset and prompt the participant to try again
                    SetDotColor(currentDot, dotDefaultColor);
                    currentDot = null;
                    phase2LastHoveredTarget = null;
                    SetInstruction("Almost! Point at a dot, hold the trigger,\ndrag to another dot, then release.\nTry again!");
                }
            }
        }
    }

    /// Phase 3: Participant connects all dots in sequence, closing the loop
    /// to complete the full pattern. Advances to Phase 4 on completion.

    void UpdatePhase3(GameObject hitObj, bool triggerPressed)
    {
        GameObject hoveredDot = GetDot(hitObj);

        // Update dot colors to reflect visited, selected, and hover states
        foreach (var dot in perimeterDots)
        {
            if (visitedDots.Contains(dot) && dot != currentDot) SetDotColor(dot, dotCompleteColor); // Already visited
            else if (dot == currentDot)                          SetDotColor(dot, dotSelectedColor); // Currently held
            else if (dot == hoveredDot)                          SetDotColor(dot, dotHoverColor);    // Being aimed at
            else                                                 SetDotColor(dot, dotDefaultColor);  // Unvisited
        }

        if (triggerPressed)
        {
            if (!isTriggerHeld)
            {
                // Fresh trigger press on an unvisited dot — select it as the next connection point
                if (hoveredDot != null && !visitedDots.Contains(hoveredDot))
                {
                    isTriggerHeld = true;
                    currentDot = hoveredDot;
                    visitedDots.Add(currentDot);
                    previewLine.enabled = true;
                    SetInstruction("Keep going! Drag to the next dot.\n" + visitedDots.Count + " / " + perimeterDots.Length + " dots.");
                }
            }
            else if (currentDot != null)
            {
                // Check if the participant is hovering the first dot to close the loop
                bool closingLoop = hoveredDot != null && hoveredDot != currentDot
                    && visitedDots.Count >= perimeterDots.Length - 1
                    && hoveredDot == visitedDots[0];

                // Check if they are hovering a new, unvisited dot
                bool newDot = hoveredDot != null && hoveredDot != currentDot
                    && !visitedDots.Contains(hoveredDot);

                // Change preview line color to green when about to close the loop
                previewLine.startColor = closingLoop ? dotCompleteColor : Color.yellow;
                previewLine.endColor   = closingLoop ? dotCompleteColor : Color.yellow;

                // Update preview line endpoints
                previewLine.SetPosition(0, currentDot.transform.position);
                if (newDot || closingLoop)
                    previewLine.SetPosition(1, hoveredDot.transform.position);
                else
                    previewLine.SetPosition(1, controllerTransform.position + controllerTransform.forward * 5f);

                // If hovering a valid next dot or the closing dot, snap the drawn line to it in real time
                if (newDot || closingLoop)
                {
                    lastHoveredNewDot = hoveredDot;
                    DrawLine(currentDot.transform.position, hoveredDot.transform.position, dotDefaultColor);
                    SetDotColor(currentDot, dotCompleteColor);
                    currentDot = hoveredDot;

                    if (closingLoop)
                    {
                        loopClosed = true;
                        SetInstruction("All dots connected!\nRelease the trigger to finish!");
                    }
                    else
                    {
                        // Continue to the next dot
                        if (!visitedDots.Contains(currentDot))
                            visitedDots.Add(currentDot);
                        SetDotColor(currentDot, dotSelectedColor);
                        SetInstruction("Keep going!\n" + visitedDots.Count + " / " + perimeterDots.Length + " dots.");
                    }
                }
            }
        }
        else
        {
            // Trigger released hide the preview line and reset its color
            previewLine.enabled = false;
            previewLine.startColor = Color.yellow;
            previewLine.endColor   = Color.yellow;

            if (isTriggerHeld)
            {
                isTriggerHeld = false;

                if (loopClosed)
                {
                    // Pattern successfully completed colour all dots and lines green
                    loopClosed = false;
                    patternCompletions++;

                    foreach (var dot in perimeterDots)
                        SetDotColor(dot, dotCompleteColor);
                    foreach (var line in drawnLines)
                    {
                        line.startColor = dotCompleteColor;
                        line.endColor   = dotCompleteColor;
                    }

                    GoToPhase(Phase.Phase4_Begin);
                }
                else
                {
                    // Trigger released mid-pattern try to finalize the last connection
                    // using the release position or the last valid dot hovered
                    GameObject releaseDot = hoveredDot != null ? hoveredDot : lastHoveredNewDot;
                    bool canClose = releaseDot != null && releaseDot != currentDot && currentDot != null
                        && ((!visitedDots.Contains(releaseDot))
                            || (visitedDots.Count >= perimeterDots.Length - 1 && releaseDot == visitedDots[0]));

                    if (canClose)
                    {
                        DrawLine(currentDot.transform.position, releaseDot.transform.position, dotDefaultColor);
                        SetDotColor(currentDot, dotCompleteColor);
                        currentDot = releaseDot;
                        if (!visitedDots.Contains(currentDot))
                            visitedDots.Add(currentDot);
                    }

                    lastHoveredNewDot = null;

                    if (currentDot != null)
                        SetDotColor(currentDot, dotCompleteColor);

                    currentDot = null;
                    SetInstruction("Good progress! " + visitedDots.Count + " / " + perimeterDots.Length + " dots.\nSelect the next dot to keep going.");
                }
            }
        }
    }

    /// Phase 4: Participant may practice the full pattern as many times as desired.
    /// Clicking "Begin Experiment" loads the main experiment scene.
    /// Touching any dot resets the board and drops back into Phase 3 for another practice round.

    void UpdatePhase4(GameObject hitObj, bool triggerPressed)
    {
        bool onButton = hitObj == theButton ||
                        (hitObj != null && hitObj.transform.IsChildOf(theButton.transform));

        if (onButton)
        {
            SetButtonColor(buttonHoverColor);
            if (triggerPressed && !isTriggerHeld)
            {
                isTriggerHeld = true;
                // Load the main experiment scene
                SceneManager.LoadScene(experimentSceneName);
            }
        }
        else
        {
            SetButtonColor(buttonDefaultColor);
        }

        // If the participant touches a dot, reset the board and start another practice round
        GameObject hoveredDot = GetDot(hitObj);
        if (triggerPressed && !isTriggerHeld && hoveredDot != null)
        {
            ResetPatternForPractice();
            currentPhase = Phase.Phase3_FullPattern;
            isTriggerHeld = true;
            currentDot = hoveredDot;
            visitedDots.Add(currentDot);
            previewLine.enabled = true;
            SetInstruction("Keep going! Drag to the next dot.\n1 / " + perimeterDots.Length + " dots.");
        }
    }

    /// Clears all drawn lines and resets dot state so the participant
    /// can practice the full pattern again from scratch.

    void ResetPatternForPractice()
    {
        foreach (var line in drawnLines)
            Destroy(line.gameObject);
        drawnLines.Clear();
        visitedDots.Clear();
        currentDot = null;
        lastHoveredNewDot = null;
        loopClosed = false;
        foreach (var dot in perimeterDots)
            SetDotColor(dot, dotDefaultColor);
    }

    /// Transitions to a new phase, resetting shared input state,
    /// updating the button and instructions, and triggering audio.

    void GoToPhase(Phase phase)
    {
        currentPhase = phase;
        isTriggerHeld = false;
        wasHoveringButton = false;
        loopClosed = false;

        // Stop any pending audio coroutine to prevent the wrong clip playing
        // if the participant advances phases quickly
        StopAllCoroutines();

        switch (phase)
        {
            case Phase.Phase1_MovementAndHover:
                theButton.SetActive(true);
                SetButtonLabel("Hover Here!");
                SetButtonColor(buttonDefaultColor);
                SetInstruction("Welcome to the experiment!\n\nUse the LEFT joystick to move around.\nUse the RIGHT joystick to turn.\n\nWhen comfortable, point your pink ray\nat the button labeled Hover Here!");
                PlayAudioDelayed(phase1Audio);
                break;

            case Phase.Phase2_DotPractice:
                theButton.SetActive(false);
                // Reset all dots to default for the practice phase
                foreach (var dot in perimeterDots)
                    SetDotColor(dot, dotDefaultColor);
                currentDot = null;
                phase2LastHoveredTarget = null;
                SetInstruction("You clicked it!\n\nNow let's practice connecting dots.\n\nPoint at a dot until it changes color,\nthen hold the trigger and drag to another dot.\nRelease the trigger when you reach it.");
                PlayAudioDelayed(phase2Audio);
                break;

            case Phase.Phase3_FullPattern:
                theButton.SetActive(false);
                // Destroy all previously drawn lines and reset dot/visit state
                foreach (var line in drawnLines)
                    Destroy(line.gameObject);
                drawnLines.Clear();
                visitedDots.Clear();
                currentDot = null;
                lastHoveredNewDot = null;
                foreach (var dot in perimeterDots)
                    SetDotColor(dot, dotDefaultColor);
                SetInstruction("You connected two dots!\n\nNow connect ALL the dots around the board.\nStart on any dot and connect them all.\n\n0 / " + perimeterDots.Length + " dots.");
                PlayAudioDelayed(phase3Audio);
                break;

            case Phase.Phase4_Begin:
                theButton.SetActive(true);
                SetButtonLabel("Begin Experiment");
                SetButtonColor(buttonDefaultColor);
                // Show different instruction text depending on how many times the pattern has been completed
                if (patternCompletions == 1)
                    SetInstruction("Amazing! You completed the pattern!\n\nFeel free to practice again — just grab a dot!\nWhen you are ready, click the button\nto begin the experiment.");
                else
                    SetInstruction("Pattern complete! (" + patternCompletions + "x)\n\nPractice again anytime — just grab a dot!\nWhen you are ready, click the button\nto begin the experiment.");
                PlayAudioDelayed(phase4Audio);
                break;
        }
    }


    /// Plays an audio clip immediately if one is assigned.
    /// Stops any currently playing clip first to avoid overlap.

    void PlayAudioDelayed(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.Play();
        }
    }

    
    /// Coroutine that waits audioDelay seconds before playing a clip.
    /// Kept for reference currently unused since PlayAudioDelayed plays immediately.
  
    IEnumerator AudioDelayCoroutine(AudioClip clip)
    {
        yield return new WaitForSeconds(audioDelay);

        // Safety check in case the phase changed before the delay elapsed
        if (audioSource != null && clip != null)
        {
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.Play();
        }
    }

    //Sets the text on the button label.</summary>
    void SetButtonLabel(string text)
    {
        if (buttonLabel != null) buttonLabel.text = text;
    }

    /// Sets the color of the button's cached material instance.</summary>
    void SetButtonColor(Color color)
    {
        if (buttonMat != null) buttonMat.color = color;
    }

    /// <summary>Sets the color of a dot's cached material instance.</summary>
    void SetDotColor(GameObject dot, Color color)
    {
        if (dot == null) return;
        if (dotMats.TryGetValue(dot, out Material m)) m.color = color;
    }

    /// Returns the dot GameObject that the given hitObj belongs to,
    /// or null if the hit object is not a dot or a child of one.
       GameObject GetDot(GameObject obj)
    {
        if (obj == null) return null;
        foreach (var dot in perimeterDots)
            if (obj == dot || obj.transform.IsChildOf(dot.transform))
                return dot;
        return null;
    }


    /// Creates a new LineRenderer between two world-space positions
    /// and adds it to the drawnLines list so it can be cleared later.

    void DrawLine(Vector3 start, Vector3 end, Color color)
    {
        var lineObj = new GameObject("DemoLine");
        var lr = lineObj.AddComponent<LineRenderer>();
        lr.material = lineMaterial;
        lr.startWidth = 0.05f;
        lr.endWidth = 0.05f;
        lr.startColor = color;
        lr.endColor = color;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.useWorldSpace = true;
        drawnLines.Add(lr);
    }


    /// Updates the on-screen instruction text and logs it to the Unity console.
  
    void SetInstruction(string text)
    {
        if (instructionDisplay != null) instructionDisplay.text = text;
        Debug.Log("[Demo] " + text);
    }
}