using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR;
using Random = System.Random;

namespace OwnershipStudy
{
    public sealed partial class OwnershipExperiment : MonoBehaviour
    {
        private enum Stage { Tracking, Setup, Handedness, Hand, Repetitions, ChooseMug, Ownership,
            Ready, CueDelay, Reach, Carry, Settle, Finished, SaveError }

        private static readonly string[] MugNames = { "Bird", "Colorful", "Flower" };
        private static readonly string[] MugFiles = { "bird_mug", "colorful_mug", "flower_mug_g" };
        private static readonly string[] PositionNames = { "left", "center", "right" };
        private const string ResourceRoot = "OwnershipStudy/";
        private const string AllocationKey = "OwnershipStudy.Allocation";
        private const string SequenceKey = "OwnershipStudy.SessionSequence";

        [SerializeField] private float cueDelayMinSeconds = 0.8f;
        [SerializeField] private float cueDelayMaxSeconds = 1.8f;
        [SerializeField] private float startZoneRadiusMeters = 0.055f;
        [SerializeField] private float movementThresholdMeters = 0.025f;
        [SerializeField] private float movementSustainSeconds = 0.045f;

        private readonly Random random = new Random();
        private readonly StudyWorkbook workbook = new StudyWorkbook();
        private readonly GameObject[] mugs = new GameObject[3];
        private readonly BoxCollider[] grabVolumes = new BoxCollider[3];
        private readonly Transform[] mugSlots = new Transform[3];
        private readonly bool[] lastTrigger = new bool[2];
        private readonly bool[] lastGrip = new bool[2];
        private readonly List<StudyLayout> layouts = new List<StudyLayout>();
        private readonly List<List<StudyTrial>> blocks = new List<List<StudyTrial>>();
        private readonly int[] failures = new int[5];

        private Stage stage;
        private StudyMode mode;
        private string handedness;
        private int taskHand;
        private int repetitions = 2;
        private int participantMug;
        private int agentMug;
        private int blockIndex;
        private int trialIndex;
        private int positionPhase;
        private string sessionId;
        private int sessionSequence;
        private string workbookPath;
        private double cueAt;
        private double cueTime;
        private double movementCandidate;
        private double movementTime;
        private double grabTime;
        private double releaseTime;
        private float startingDistance;
        private Vector3 originAtCue;
        private Vector3 grabbedOffset;
        private Quaternion grabbedRotation;
        private bool completeAfterSave;
        private bool advanceAfterSave;

        private StudyTrial Trial => blocks[blockIndex][trialIndex];
        private int Target => Trial.Target;
        private string Layout => layouts[blockIndex] == StudyLayout.AgentOn ? "agent_on" : "agent_off";
        private static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            stage = Stage.Tracking;
            BuildRoom();
            OpenPanel("Ownership Study", "Waiting for Quest tracking");
        }

        private void Update()
        {
            UpdatePoses();
            ReadControls(out bool leftClick, out bool rightClick, out bool gripPressed, out bool gripReleased);
            if (stage == Stage.Tracking)
            {
                if (headTracked)
                {
                    AlignRoomToParticipant();
                    ShowSetup();
                }
                return;
            }

            if (leftClick) Click(0);
            if (rightClick) Click(1);

            if (stage < Stage.CueDelay || stage > Stage.Settle)
                return;
            if (!handTracked[taskHand] || !headTracked)
            {
                FinishAttempt("tracking_interrupted");
                return;
            }

            Vector3 hand = hands[taskHand].position;
            double now = Now;
            switch (stage)
            {
                case Stage.CueDelay:
                    if (Vector3.Distance(hand, startZone.position) > startZoneRadiusMeters || gripPressed)
                        FinishAttempt("premature");
                    else if (now >= cueAt)
                        ShowCue(hand, now);
                    break;
                case Stage.Reach:
                    SampleReach(hand, now);
                    if (gripPressed)
                        TryGrab(hand, now);
                    break;
                case Stage.Carry:
                    mugs[Target].transform.SetPositionAndRotation(hand + hands[taskHand].rotation * grabbedOffset,
                        hands[taskHand].rotation * grabbedRotation);
                    if (gripReleased)
                        ReleaseMug(now);
                    break;
                case Stage.Settle:
                    VerifyPlacement(now);
                    break;
            }
        }

        private void ShowSetup()
        {
            stage = Stage.Setup;
            OpenPanel("Researcher setup", "Select the session layout");
            Button(0, "Agent OFF", () => SelectMode(StudyMode.AgentOff));
            Button(1, "Agent ON", () => SelectMode(StudyMode.AgentOn));
            Button(2, "Both layouts", () => SelectMode(StudyMode.Both));
        }

        private void SelectMode(StudyMode selected)
        {
            mode = selected;
            stage = Stage.Handedness;
            OpenPanel("Handedness", "Select the participant’s self-reported handedness");
            Button(0, "Left", () => SelectHandedness("left"));
            Button(1, "Right", () => SelectHandedness("right"));
            Button(2, "Mixed / both", () => SelectHandedness("mixed_or_both"));
        }

        private void SelectHandedness(string selected)
        {
            handedness = selected;
            stage = Stage.Hand;
            OpenPanel("Reaching hand", "Select the controller the participant will use to reach");
            Button(0, "Left", () => SelectHand(0));
            Button(2, "Right", () => SelectHand(1));
        }

        private void SelectHand(int selected)
        {
            taskHand = selected;
            ShowRepetitions();
        }

        private void ShowRepetitions()
        {
            stage = Stage.Repetitions;
            OpenPanel("Trials per layout", $"{repetitions} × 3 = {repetitions * 3} scheduled trials");
            Button(0, "Fewer", () => { repetitions = Mathf.Max(1, repetitions - 1); ShowRepetitions(); });
            Button(1, "Confirm", ShowMugSelection);
            Button(2, "More", () => { repetitions++; ShowRepetitions(); });
        }

        private void ShowMugSelection()
        {
            stage = Stage.ChooseMug;
            OpenPanel("Choose your mug", "Your mug will stay yours for the entire session.");
            for (int i = 0; i < MugNames.Length; i++)
            {
                int choice = i;
                Button(i, MugNames[i], () => ChooseMug(choice), previews[i]);
            }
        }

        private void ChooseMug(int selected)
        {
            participantMug = selected;
            agentMug = (selected + random.Next(1, 3)) % 3;
            stage = Stage.Ownership;
            OpenPanel("Mug ownership", "You may pick up any mug when it lights up.");
            for (int i = 0; i < MugNames.Length; i++)
                OwnershipCard(i, Owner(i), previews[i]);
            Button(1, "Continue", BeginSession);
        }

        private string Owner(int mug)
        {
            if (mug == participantMug) return "Your mug";
            return mug == agentMug ? "Virtual human’s mug" : "Unowned mug";
        }

        private void BeginSession()
        {
            int allocation = TakeAllocation();
            positionPhase = allocation % 3;
            if (mode == StudyMode.Both)
            {
                layouts.Add(allocation < 3 ? StudyLayout.AgentOff : StudyLayout.AgentOn);
                layouts.Add(allocation < 3 ? StudyLayout.AgentOn : StudyLayout.AgentOff);
            }
            else
                layouts.Add(mode == StudyMode.AgentOn ? StudyLayout.AgentOn : StudyLayout.AgentOff);

            for (int block = 0; block < layouts.Count; block++)
                blocks.Add(StudySchedule.CreateTrials(repetitions, positionPhase, random));

            sessionSequence = PlayerPrefs.GetInt(SequenceKey, 0) + 1;
            PlayerPrefs.SetInt(SequenceKey, sessionSequence);
            PlayerPrefs.Save();
            sessionId = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Application.persistentDataPath, "StudyResults");
            workbookPath = Path.Combine(directory, $"OwnershipStudy_{sessionSequence:D4}_{sessionId}.xlsx");
            InitializeWorkbook();
            SetupTrial();
            SaveProgress();
        }

        private int TakeAllocation()
        {
            string remaining = PlayerPrefs.GetString(AllocationKey, "");
            if (mode != StudyMode.Both) return random.Next(3);
            if (remaining.Length == 0)
                remaining = string.Concat(Array.ConvertAll(StudySchedule.CreateAllocationCycle(random), n => n.ToString()));
            PlayerPrefs.SetString(AllocationKey, remaining.Substring(1));
            return remaining[0] - '0';
        }

        private void SetupTrial()
        {
            Array.Clear(failures, 0, failures.Length);
            agent.SetActive(layouts[blockIndex] == StudyLayout.AgentOn);
            ResetMugs();
            ShowReady("Place your reaching hand in the start zone.");
        }

        private void ShowReady(string prompt)
        {
            stage = Stage.Ready;
            OpenPanel($"Layout {blockIndex + 1}/{layouts.Count}     Trial {trialIndex + 1}/{blocks[blockIndex].Count}", prompt);
            string ownership = $"Yours: {MugNames[participantMug]}   •   Virtual human: {MugNames[agentMug]}   •   Unowned: {MugNames[3 - participantMug - agentMug]}";
            Text(ownership, new Vector3(0, PanelY + 0.035f, PanelZ - 0.01f), 0.016f, Color.white).transform.SetParent(panel.transform, true);
            Text("Point and press the other controller’s trigger to select Start.",
                new Vector3(0, PanelY - 0.026f, PanelZ - 0.01f), 0.017f, Color.white).transform.SetParent(panel.transform, true);
            Button(1, "Start", StartAttempt);
        }

        private void StartAttempt()
        {
            if (!headTracked || !handTracked[taskHand] || lastGrip[taskHand] ||
                Vector3.Distance(hands[taskHand].position, startZone.position) > startZoneRadiusMeters)
            {
                ShowReady("Hold the reaching controller inside the start zone with grip released.");
                return;
            }
            ClosePanel();
            cueAt = Now + cueDelayMinSeconds + random.NextDouble() * (cueDelayMaxSeconds - cueDelayMinSeconds);
            cueTime = movementTime = grabTime = releaseTime = movementCandidate = -1;
            stage = Stage.CueDelay;
        }

        private void ShowCue(Vector3 hand, double now)
        {
            cueTime = now;
            originAtCue = hand;
            startingDistance = Vector3.Distance(hand, grabVolumes[Target].bounds.center);
            cueMarker.position = mugSlots[Trial.Position].position + Vector3.up * 0.003f;
            cueMarker.gameObject.SetActive(true);
            stage = Stage.Reach;
        }

        private void SampleReach(Vector3 hand, double now)
        {
            if (movementTime >= 0) return;
            if (Vector3.Distance(hand, originAtCue) < movementThresholdMeters)
            {
                movementCandidate = -1;
                return;
            }
            if (movementCandidate < 0)
                movementCandidate = now;

            if (now - movementCandidate >= movementSustainSeconds)
                ConfirmMovement();
        }

        private void ConfirmMovement()
        {
            movementTime = movementCandidate;
        }

        private void TryGrab(Vector3 hand, double now)
        {
            int actualMug = -1;
            float closest = 0.035f;
            for (int mug = 0; mug < mugs.Length; mug++)
            {
                float distance = Vector3.Distance(hand, grabVolumes[mug].ClosestPoint(hand));
                if (distance >= closest) continue;
                closest = distance;
                actualMug = mug;
            }
            if (actualMug < 0)
            {
                FinishAttempt("miss");
                return;
            }
            if (movementTime < 0 && movementCandidate >= 0)
                ConfirmMovement();

            grabTime = now;
            if (actualMug != Target)
            {
                FinishAttempt("wrong_mug");
                return;
            }
            Transform controller = hands[taskHand];
            Transform mugTransform = mugs[Target].transform;
            grabbedOffset = Quaternion.Inverse(controller.rotation) * (mugTransform.position - controller.position);
            grabbedRotation = Quaternion.Inverse(controller.rotation) * mugTransform.rotation;
            cueMarker.gameObject.SetActive(false);
            stage = Stage.Carry;
        }

        private void ReleaseMug(double now)
        {
            releaseTime = now;
            Transform mug = mugs[Target].transform;
            Vector3 offset = mug.position - placementPad.position;
            if (new Vector2(offset.x, offset.z).magnitude > 0.085f || offset.y < -0.01f || offset.y > 0.065f ||
                Vector3.Dot(mug.up, Vector3.up) < 0.87f)
            {
                FinishAttempt("invalid_placement");
                return;
            }
            Vector3 pendingPlacement = new Vector3(mug.position.x, placementPad.position.y, mug.position.z);
            Quaternion pendingRotation = Quaternion.Euler(0, mug.eulerAngles.y, 0);
            mug.SetPositionAndRotation(pendingPlacement, pendingRotation);
            stage = Stage.Settle;
        }

        private void VerifyPlacement(double now)
        {
            if (now - releaseTime >= 0.2)
                FinishAttempt("success");
        }

        private void FinishAttempt(string outcome)
        {
            bool success = outcome == "success";
            if (outcome == "miss") failures[0]++;
            if (outcome == "wrong_mug") failures[1]++;
            if (outcome == "premature") failures[2]++;
            if (outcome == "invalid_placement") failures[3]++;
            if (outcome == "tracking_interrupted") failures[4]++;
            RecordTrial(success);
            cueMarker.gameObject.SetActive(false);

            bool lastTrial = trialIndex + 1 == blocks[blockIndex].Count;
            completeAfterSave = success && lastTrial && blockIndex + 1 == blocks.Count;
            advanceAfterSave = success;
            pendingOutcome = outcome;
            if (SaveProgress()) ContinueAfterSave(outcome);
        }

        private void ContinueAfterSave(string outcome)
        {
            if (completeAfterSave)
            {
                stage = Stage.Finished;
                OpenPanel("Session complete", "Excel results saved. Give the headset to the researcher.");
                SetMugsVisible(false);
                agent.SetActive(false);
                Button(1, "New session", () => UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name));
                Debug.Log($"Ownership Study workbook: {workbookPath}");
                return;
            }
            if (!advanceAfterSave)
            {
                ResetMugs();
                string prompt = outcome == "wrong_mug" ? "Wrong mug. Return to the start zone and retry." :
                    outcome == "premature" ? "Wait for the mug to light up. Return to the start zone and retry." :
                    outcome == "invalid_placement" ? "Place the mug upright on the marked pad. Press Start to retry." :
                    outcome == "tracking_interrupted" ? "Tracking interrupted. Return to the start zone and retry." :
                    "No mug was picked up. Return to the start zone and retry.";
                ShowReady(prompt);
                return;
            }
            trialIndex++;
            if (trialIndex == blocks[blockIndex].Count)
            {
                blockIndex++;
                trialIndex = 0;
            }
            SetupTrial();
        }

        private static double Delta(double start, double end) => start >= 0 && end >= start ? (end - start) * 1000 : -1;
        private static object Measured(double number) => number >= 0 ? (object)number : null;
    }
}
