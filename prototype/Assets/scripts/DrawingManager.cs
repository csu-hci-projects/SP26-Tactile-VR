using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System.IO;

public class DrawingManager : MonoBehaviour
{
    [Header("References")]
    public Transform controllerTransform;
    public Material lineMaterial;
    public float lineWidth = 0.05f;
    public TextMesh timerDisplay;
    public TextMesh statsDisplay;
    public GameObject beginButton;

    private GameObject currentDot = null;
    private GameObject hoveredDot = null;
    private List<LineRenderer> drawnLines = new List<LineRenderer>();
    private List<GameObject> visitedDots = new List<GameObject>();
    private bool isTriggerHeld = false;
    private LineRenderer previewLine;
    private InputDevice rightDevice;

    private bool isResetting = false;
    private float resetTimer = 0f;

    private enum ExperimentState { WaitingToStart, Countdown, Running, Complete }
    private ExperimentState state = ExperimentState.WaitingToStart;
    private float countdownTimer = 3f;
    private float experimentStartTime = 0f;
    private int correctCount = 0;
    private int incorrectCount = 0;
    private float totalTime = 0f;

    private string csvPath = @"C:\Users\RLNel\Documents\Spring2026\prototype\Assets\scripts\Data.csv";

    private string[] correctPattern = new string[]
    {
        "Dot_2_0", "Dot_2_1", "Dot_2_2", "Dot_1_1", "Dot_0_0",
        "Dot_0_1", "Dot_0_2", "Dot_1_0", "Dot_1_1", "Dot_1_2"
    };

    void Start()
    {
        GameObject previewObj = new GameObject("PreviewLine");
        previewLine = previewObj.AddComponent<LineRenderer>();

        Material previewMat = new Material(lineMaterial);
        previewMat.color = new Color(1f, 0.4f, 0.7f);

        previewLine.material = previewMat;
        previewLine.startColor = new Color(1f, 0.4f, 0.7f);
        previewLine.endColor = new Color(1f, 0.4f, 0.7f);
        previewLine.widthCurve = new AnimationCurve(
            new Keyframe(0, 0.02f),
            new Keyframe(1, 0.02f)
        );
        previewLine.widthMultiplier = 1f;
        previewLine.positionCount = 2;
        previewLine.useWorldSpace = true;
        previewLine.enabled = true;

        // Create CSV with headers if it doesn't exist
        if (!File.Exists(csvPath))
        {
            File.WriteAllText(csvPath, "Date,Time,TotalTime,Correct,Incorrect\n");
            Debug.Log("Created new CSV at: " + csvPath);
        }

        UpdateTimerDisplay("", 50);
        UpdateStatsDisplay("");

        TryGetDevice();
    }

    void TryGetDevice()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right |
            InputDeviceCharacteristics.Controller, devices);

        if (devices.Count > 0)
        {
            rightDevice = devices[0];
            Debug.Log("Got device: " + rightDevice.name);
        }
    }

    public void StartExperiment()
    {
        state = ExperimentState.Countdown;
        countdownTimer = 3f;
        UpdateTimerDisplay("3", 80);
    }

    void Update()
    {
        if (!rightDevice.isValid)
        {
            TryGetDevice();
            return;
        }

        previewLine.SetPosition(0, controllerTransform.position);
        previewLine.SetPosition(1, controllerTransform.position + controllerTransform.forward * 5f);

        float triggerValue = 0f;
        rightDevice.TryGetFeatureValue(CommonUsages.trigger, out triggerValue);
        bool triggerPressed = triggerValue > 0.5f;

        switch (state)
        {
            case ExperimentState.WaitingToStart:
                HandleButtonRaycast(triggerPressed);
                break;

            case ExperimentState.Countdown:
                HandleCountdown();
                break;

            case ExperimentState.Running:
                HandleRunning(triggerPressed);
                break;

            case ExperimentState.Complete:
                break;
        }
    }

    void HandleButtonRaycast(bool triggerPressed)
    {
        Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 10f))
        {
            if (hit.collider.gameObject.name == "BeginButton" && triggerPressed && !isTriggerHeld)
            {
                isTriggerHeld = true;
                beginButton.SetActive(false);
                StartExperiment();
            }
        }

        if (!triggerPressed) isTriggerHeld = false;
    }

    void HandleCountdown()
    {
        countdownTimer -= Time.deltaTime;

        if (countdownTimer > 2f)
            UpdateTimerDisplay("3", 80);
        else if (countdownTimer > 1f)
            UpdateTimerDisplay("2", 80);
        else if (countdownTimer > 0f)
            UpdateTimerDisplay("1", 80);
        else
        {
            UpdateTimerDisplay("GO!", 80);
            state = ExperimentState.Running;
            experimentStartTime = Time.time;
        }
    }

    void HandleRunning(bool triggerPressed)
    {
        float elapsed = Time.time - experimentStartTime;

        if (!isResetting)
        {
            UpdateTimerDisplay(elapsed.ToString("F1") + "s", 60);
            UpdateStatsDisplay(
                "Correct:   " + correctCount + "\n" +
                "Incorrect: " + incorrectCount
            );
        }

        if (isResetting)
        {
            resetTimer -= Time.deltaTime;
            if (resetTimer <= 0f)
            {
                isResetting = false;
                ResetPattern();
            }
            return;
        }

        HandleHover();

        if (triggerPressed)
        {
            if (!isTriggerHeld)
            {
                isTriggerHeld = true;
                TrySelectStartDot();
            }
            else
            {
                if (currentDot != null && hoveredDot != null && hoveredDot != currentDot)
                {
                    DrawLine(currentDot.transform.position, hoveredDot.transform.position);
                    SetDotColor(currentDot, Color.green);
                    currentDot = hoveredDot;
                    visitedDots.Add(currentDot);
                    SetDotColor(currentDot, Color.yellow);
                }
            }
        }
        else
        {
            if (isTriggerHeld)
            {
                isTriggerHeld = false;
                if (currentDot != null)
                {
                    SetDotColor(currentDot, Color.yellow);
                    currentDot = null;
                }
                CheckPattern();
            }
        }

        if (isTriggerHeld && currentDot != null)
        {
            previewLine.SetPosition(0, controllerTransform.position);
            if (hoveredDot != null && hoveredDot != currentDot)
                previewLine.SetPosition(1, hoveredDot.transform.position);
            else
                previewLine.SetPosition(1, controllerTransform.position + controllerTransform.forward * 5f);
        }
    }

    void CheckPattern()
    {
        bool correct = false;

        if (visitedDots.Count == correctPattern.Length)
        {
            correct = true;
            for (int i = 0; i < correctPattern.Length; i++)
            {
                if (visitedDots[i].name != correctPattern[i])
                {
                    correct = false;
                    break;
                }
            }
        }

        Color flashColor = correct ? Color.green : Color.red;

        foreach (GameObject dot in visitedDots)
            SetDotColor(dot, flashColor);
        foreach (LineRenderer lr in drawnLines)
        {
            lr.startColor = flashColor;
            lr.endColor = flashColor;
        }

        if (correct)
        {
            correctCount++;
            Debug.Log("Correct! " + correctCount + "/3");

            if (correctCount >= 3)
            {
                totalTime = Time.time - experimentStartTime;
                state = ExperimentState.Complete;
                UpdateTimerDisplay("Done!", 60);
                UpdateStatsDisplay(
                    "Complete!\n" +
                    "Total time: " + totalTime.ToString("F2") + "s\n" +
                    "Correct:    " + correctCount + "\n" +
                    "Incorrect:  " + incorrectCount
                );
                SaveToCSV();
                isResetting = true;
                resetTimer = 0.5f;
                return;
            }
        }
        else
        {
            incorrectCount++;
            Debug.Log("Incorrect! " + incorrectCount + " mistakes so far.");
        }

        isResetting = true;
        resetTimer = 0.5f;
    }

    void SaveToCSV()
    {
        try
        {
            // Create file with headers if it doesn't exist
            if (!File.Exists(csvPath))
            {
                string header = "Date,Time (HH:MM:SS),Total Time (s),Successful Attempts,Missed Attempts\n";
                File.WriteAllText(csvPath, header);
            }

            string date = System.DateTime.Now.ToString("yyyy-MM-dd");
            string time = System.DateTime.Now.ToString("HH:mm:ss");
            string newLine = date + "," + time + "," + totalTime.ToString("F2") + "," + correctCount + "," + incorrectCount + "\n";
            File.AppendAllText(csvPath, newLine);
            Debug.Log("Data saved to CSV: " + newLine);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to save CSV: " + e.Message);
        }
    }

    void ResetPattern()
    {
        foreach (LineRenderer lr in drawnLines)
            Destroy(lr.gameObject);
        drawnLines.Clear();

        foreach (GameObject dot in visitedDots)
            SetDotColor(dot, Color.black);
        visitedDots.Clear();

        currentDot = null;
        hoveredDot = null;
    }

    void HandleHover()
    {
        Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);
        RaycastHit hit;
        GameObject newHover = null;

        if (Physics.Raycast(ray, out hit, 10f))
        {
            if (hit.collider.gameObject.name.StartsWith("Dot_"))
                newHover = hit.collider.gameObject;
        }

        if (newHover != hoveredDot)
        {
            if (hoveredDot != null && hoveredDot != currentDot)
                SetDotColor(hoveredDot, Color.black);
            if (newHover != null && newHover != currentDot)
                SetDotColor(newHover, Color.red);
            hoveredDot = newHover;
        }
    }

    void TrySelectStartDot()
    {
        Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 10f))
        {
            if (hit.collider.gameObject.name.StartsWith("Dot_"))
            {
                currentDot = hit.collider.gameObject;
                visitedDots.Add(currentDot);
                SetDotColor(currentDot, Color.yellow);
            }
        }
    }

    void DrawLine(Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject("Line");
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.material = lineMaterial;
        lr.widthCurve = new AnimationCurve(
            new Keyframe(0, 0.05f),
            new Keyframe(1, 0.05f)
        );
        lr.widthMultiplier = 1f;
        lr.startWidth = 0.05f;
        lr.endWidth = 0.05f;
        lr.startColor = Color.black;
        lr.endColor = Color.black;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.useWorldSpace = true;
        drawnLines.Add(lr);
    }

    void SetDotColor(GameObject dot, Color color)
    {
        Renderer r = dot.GetComponent<Renderer>();
        if (r != null)
            r.material.color = color;
    }

    void UpdateTimerDisplay(string text, int fontSize)
    {
        if (timerDisplay != null)
        {
            timerDisplay.text = text;
            timerDisplay.fontSize = fontSize;
        }
        Debug.Log("[Timer] " + text);
    }

    void UpdateStatsDisplay(string text)
    {
        if (statsDisplay != null)
            statsDisplay.text = text;
        Debug.Log("[Stats] " + text);
    }

    public void ClearLines()
    {
        ResetPattern();
    }
}