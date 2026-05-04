using UnityEngine;
using UnityEngine.XR;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// VR Tutorial Demo Scene.
/// Participant controls ALL flow - no auto-advancing timers.
///
/// Flow:
///   Phase 1 - Movement + Hover:
///             Button visible. Hover it, then click it to proceed.
///
///   Phase 2 - Dot Practice:
///             Button hidden. Connect any two dots to proceed.
///
///   Phase 3 - Full Pattern:
///             Button hidden. Connect all dots to proceed.
///
///   Phase 4 - Begin Experiment:
///             Button visible labeled "Begin Experiment".
///             Dots reset after each successful pattern — practice as many
///             times as desired. Click the button to load the experiment.
///
/// TO ADD AUDIO LATER:
///   Drag AudioClip assets into Inspector slots. No code changes needed.
///   Audio plays 30 seconds after each phase begins.
/// </summary>
public class DemoManager : MonoBehaviour
{
    [Header("Controller")]
    public Transform controllerTransform;

    [Header("The Button")]
    public GameObject theButton;
    public TMP_Text buttonLabel;
    public Color buttonDefaultColor = Color.white;
    public Color buttonHoverColor = Color.red;
    public Color buttonClickedColor = Color.green;

    [Header("Perimeter Dots (8 dots clockwise)")]
    public GameObject[] perimeterDots;
    public Material lineMaterial;
    public Color dotDefaultColor = Color.black;
    public Color dotHoverColor = Color.red;
    public Color dotSelectedColor = Color.yellow;
    public Color dotCompleteColor = Color.green;

    [Header("Instruction Display")]
    public TMP_Text instructionDisplay;

    [Header("Experiment Scene")]
    public string experimentSceneName = "SampleScene";

    [Header("Audio - drag clips in later, no code changes needed")]
    public AudioClip phase1Audio;
    public AudioClip phase2Audio;
    public AudioClip phase3Audio;
    public AudioClip phase4Audio;
    [Tooltip("Delay in seconds before audio plays after each phase begins.")]
    public float audioDelay = 0.01f;
    private AudioSource audioSource;

    // Phases
    private enum Phase { Phase1_MovementAndHover, Phase2_DotPractice, Phase3_FullPattern, Phase4_Begin }
    private Phase currentPhase = Phase.Phase1_MovementAndHover;

    // Input
    private InputDevice rightDevice;
    private bool isTriggerHeld = false;
    private bool wasHoveringButton = false;

    // Materials (cached instances so color changes always work)
    private Material buttonMat;
    private Dictionary<GameObject, Material> dotMats = new Dictionary<GameObject, Material>();

    // Dot state
    private GameObject currentDot = null;
    private GameObject lastHoveredNewDot = null;
    private List<LineRenderer> drawnLines = new List<LineRenderer>();
    private List<GameObject> visitedDots = new List<GameObject>();

    // Phase 2 fallback: last dot hovered while dragging
    private GameObject phase2LastHoveredTarget = null;

    // Tracks whether the loop has been fully closed in Phase 3
    private bool loopClosed = false;

    // How many times the full pattern has been completed
    private int patternCompletions = 0;

    // Preview line
    private LineRenderer previewLine;

    // ── Start ──────────────────────────────────────────────────────────────
    void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        TryGetDevice();

        // Cache button material
        if (theButton != null)
        {
            var r = theButton.GetComponent<Renderer>();
            if (r != null) { buttonMat = new Material(r.sharedMaterial); r.material = buttonMat; }
        }

        // Cache dot materials
        foreach (var dot in perimeterDots)
        {
            if (dot == null) continue;
            var r = dot.GetComponent<Renderer>();
            if (r != null) { var m = new Material(r.sharedMaterial); r.material = m; dotMats[dot] = m; }
        }

        // Preview line
        var previewObj = new GameObject("PreviewLine");
        previewLine = previewObj.AddComponent<LineRenderer>();
        previewLine.material = lineMaterial;
        previewLine.startWidth = 0.05f;
        previewLine.endWidth = 0.05f;
        previewLine.startColor = Color.yellow;
        previewLine.endColor = Color.yellow;
        previewLine.positionCount = 2;
        previewLine.useWorldSpace = true;
        previewLine.SetPosition(0, Vector3.zero);
        previewLine.SetPosition(1, Vector3.forward);
        previewLine.enabled = false;

        foreach (var dot in perimeterDots)
            SetDotColor(dot, dotDefaultColor);

        GoToPhase(Phase.Phase1_MovementAndHover);
    }

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
        if (!rightDevice.isValid) { TryGetDevice(); return; }

        float triggerValue = 0f;
        rightDevice.TryGetFeatureValue(CommonUsages.trigger, out triggerValue);
        bool triggerPressed = triggerValue > 0.5f;

        Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);
        RaycastHit hit;
        bool didHit = Physics.Raycast(ray, out hit, 10f);
        GameObject hitObj = didHit ? hit.collider.gameObject : null;

        switch (currentPhase)
        {
            case Phase.Phase1_MovementAndHover: UpdatePhase1(hitObj, triggerPressed); break;
            case Phase.Phase2_DotPractice:      UpdatePhase2(hitObj, triggerPressed); break;
            case Phase.Phase3_FullPattern:      UpdatePhase3(hitObj, triggerPressed); break;
            case Phase.Phase4_Begin:            UpdatePhase4(hitObj, triggerPressed); break;
        }

        if (!triggerPressed) isTriggerHeld = false;
    }

    // ── Phase 1: Movement + Hover + Click ─────────────────────────────────
    void UpdatePhase1(GameObject hitObj, bool triggerPressed)
    {
        bool onButton = hitObj == theButton ||
                        (hitObj != null && hitObj.transform.IsChildOf(theButton.transform));

        if (onButton)
        {
            SetButtonColor(buttonHoverColor);

            if (!wasHoveringButton)
            {
                wasHoveringButton = true;
                SetInstruction("Great job! The button changed color.\nThat means your ray is pointing at it!\n\nNow squeeze the RIGHT trigger to click it.");
            }

            if (triggerPressed && !isTriggerHeld)
            {
                isTriggerHeld = true;
                SetButtonColor(buttonClickedColor);
                GoToPhase(Phase.Phase2_DotPractice);
            }
        }
        else
        {
            SetButtonColor(buttonDefaultColor);

            if (wasHoveringButton)
            {
                wasHoveringButton = false;
                SetInstruction("Welcome to the experiment!\n\nUse the LEFT joystick to move around.\nUse the RIGHT joystick to turn.\n\nWhen comfortable, point your pink ray\nat the button labeled Hover Here!");
            }
        }
    }

    // ── Phase 2: Connect ANY two dots ─────────────────────────────────────
    void UpdatePhase2(GameObject hitObj, bool triggerPressed)
    {
        GameObject hoveredDot = GetDot(hitObj);

        // Track last valid target while dragging so a slightly-off release still works
        if (triggerPressed && isTriggerHeld && hoveredDot != null && hoveredDot != currentDot)
            phase2LastHoveredTarget = hoveredDot;

        // Update colors
        foreach (var dot in perimeterDots)
        {
            if (dot == currentDot)      SetDotColor(dot, dotSelectedColor);
            else if (dot == hoveredDot) SetDotColor(dot, dotHoverColor);
            else                        SetDotColor(dot, dotDefaultColor);
        }

        if (triggerPressed)
        {
            if (!isTriggerHeld)
            {
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
                previewLine.SetPosition(0, currentDot.transform.position);
                GameObject previewTarget = (hoveredDot != null && hoveredDot != currentDot)
                    ? hoveredDot
                    : phase2LastHoveredTarget;

                if (previewTarget != null)
                    previewLine.SetPosition(1, previewTarget.transform.position);
                else
                    previewLine.SetPosition(1, controllerTransform.position + controllerTransform.forward * 5f);
            }
        }
        else
        {
            previewLine.enabled = false;

            if (isTriggerHeld && currentDot != null)
            {
                isTriggerHeld = false;

                GameObject targetDot = (hoveredDot != null && hoveredDot != currentDot)
                    ? hoveredDot
                    : phase2LastHoveredTarget;

                if (targetDot != null && targetDot != currentDot)
                {
                    DrawLine(currentDot.transform.position, targetDot.transform.position, dotCompleteColor);
                    SetDotColor(currentDot, dotCompleteColor);
                    SetDotColor(targetDot, dotCompleteColor);
                    currentDot = null;
                    phase2LastHoveredTarget = null;
                    GoToPhase(Phase.Phase3_FullPattern);
                }
                else
                {
                    SetDotColor(currentDot, dotDefaultColor);
                    currentDot = null;
                    phase2LastHoveredTarget = null;
                    SetInstruction("Almost! Point at a dot, hold the trigger,\ndrag to another dot, then release.\nTry again!");
                }
            }
        }
    }

    // ── Phase 3: Connect all dots ──────────────────────────────────────────
    void UpdatePhase3(GameObject hitObj, bool triggerPressed)
    {
        GameObject hoveredDot = GetDot(hitObj);

        // Update colors
        foreach (var dot in perimeterDots)
        {
            if (visitedDots.Contains(dot) && dot != currentDot) SetDotColor(dot, dotCompleteColor);
            else if (dot == currentDot)                          SetDotColor(dot, dotSelectedColor);
            else if (dot == hoveredDot)                          SetDotColor(dot, dotHoverColor);
            else                                                 SetDotColor(dot, dotDefaultColor);
        }

        if (triggerPressed)
        {
            if (!isTriggerHeld)
            {
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
                bool closingLoop = hoveredDot != null && hoveredDot != currentDot
                    && visitedDots.Count >= perimeterDots.Length - 1
                    && hoveredDot == visitedDots[0];
                bool newDot = hoveredDot != null && hoveredDot != currentDot
                    && !visitedDots.Contains(hoveredDot);

                previewLine.startColor = closingLoop ? dotCompleteColor : Color.yellow;
                previewLine.endColor   = closingLoop ? dotCompleteColor : Color.yellow;

                previewLine.SetPosition(0, currentDot.transform.position);
                if (newDot || closingLoop)
                    previewLine.SetPosition(1, hoveredDot.transform.position);
                else
                    previewLine.SetPosition(1, controllerTransform.position + controllerTransform.forward * 5f);

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
            previewLine.enabled = false;
            previewLine.startColor = Color.yellow;
            previewLine.endColor   = Color.yellow;

            if (isTriggerHeld)
            {
                isTriggerHeld = false;

                if (loopClosed)
                {
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

    // ── Phase 4: Begin Experiment (pattern resets on each completion) ──────
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
                SceneManager.LoadScene(experimentSceneName);
            }
        }
        else
        {
            SetButtonColor(buttonDefaultColor);
        }

        // If the player grabs a dot, reset the board and drop back into Phase 3
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

    // ── Helpers for Phase 4 practice reset ────────────────────────────────
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

    // ── Phase transitions ──────────────────────────────────────────────────
    void GoToPhase(Phase phase)
    {
        currentPhase = phase;
        isTriggerHeld = false;
        wasHoveringButton = false;
        loopClosed = false;

        // Cancel any pending audio coroutine so phase transitions don't
        // accidentally play the wrong clip
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
                foreach (var dot in perimeterDots)
                    SetDotColor(dot, dotDefaultColor);
                currentDot = null;
                phase2LastHoveredTarget = null;
                SetInstruction("You clicked it!\n\nNow let's practice connecting dots.\n\nPoint at a dot until it changes color,\nthen hold the trigger and drag to another dot.\nRelease the trigger when you reach it.");
                PlayAudioDelayed(phase2Audio);
                break;

            case Phase.Phase3_FullPattern:
                theButton.SetActive(false);
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
                if (patternCompletions == 1)
                    SetInstruction("Amazing! You completed the pattern!\n\nFeel free to practice again — just grab a dot!\nWhen you are ready, click the button\nto begin the experiment.");
                else
                    SetInstruction("Pattern complete! (" + patternCompletions + "x)\n\nPractice again anytime — just grab a dot!\nWhen you are ready, click the button\nto begin the experiment.");
                PlayAudioDelayed(phase4Audio);
                break;
        }
    }

    // ── Audio ──────────────────────────────────────────────────────────────
void PlayAudioDelayed(AudioClip clip)
{
    if (clip != null && audioSource != null)
    {
        audioSource.Stop();
        audioSource.clip = clip;
        audioSource.Play();
    }
}
    IEnumerator AudioDelayCoroutine(AudioClip clip)
    {
        yield return new WaitForSeconds(audioDelay);

        // Only play if the clip is still relevant (coroutines are stopped on
        // phase change so this is just a safety net)
        if (audioSource != null && clip != null)
        {
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.Play();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    void SetButtonLabel(string text)
    {
        if (buttonLabel != null) buttonLabel.text = text;
    }

    void SetButtonColor(Color color)
    {
        if (buttonMat != null) buttonMat.color = color;
    }

    void SetDotColor(GameObject dot, Color color)
    {
        if (dot == null) return;
        if (dotMats.TryGetValue(dot, out Material m)) m.color = color;
    }

    GameObject GetDot(GameObject obj)
    {
        if (obj == null) return null;
        foreach (var dot in perimeterDots)
            if (obj == dot || obj.transform.IsChildOf(dot.transform))
                return dot;
        return null;
    }

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

    void SetInstruction(string text)
    {
        if (instructionDisplay != null) instructionDisplay.text = text;
        Debug.Log("[Demo] " + text);
    }
}