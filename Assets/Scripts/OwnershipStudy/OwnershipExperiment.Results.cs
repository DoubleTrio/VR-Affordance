using System;
using System.IO;
using UnityEngine;

namespace OwnershipStudy
{
    public sealed partial class OwnershipExperiment
    {
        private string pendingOutcome;

        private void InitializeWorkbook()
        {
            workbook.Session.Add(new object[] { "session_id", "participant_number", "started_utc", "handedness",
                "hand_used", "layout_order", "position_phase", "trials_per_layout", "participant_mug", "agent_mug",
                "unowned_mug", "cue_delay_min_ms", "cue_delay_max_ms", "start_zone_radius_m", "movement_threshold_m",
                "movement_sustain_ms", "elapsed_ms", "status" });
            workbook.Session.Add(new object[] { sessionId, sessionSequence, DateTime.UtcNow.ToString("O"), handedness,
                taskHand == 0 ? "left" : "right", layouts.Count == 2 ? $"{layouts[0]} -> {layouts[1]}" : layouts[0].ToString(),
                positionPhase, repetitions * 3, MugNames[participantMug], MugNames[agentMug],
                MugNames[3 - participantMug - agentMug], cueDelayMinSeconds * 1000, cueDelayMaxSeconds * 1000,
                startZoneRadiusMeters, movementThresholdMeters, movementSustainSeconds * 1000, 0, "in_progress" });
            workbook.Trials.Add(new object[] { "session_id", "handedness", "hand_used", "block", "layout", "trial",
                "target_mug", "target_owner", "target_position", "left_mug", "center_mug", "right_mug", "attempt_count",
                "miss_count", "wrong_count", "premature_count", "invalid_placement_count", "latest_outcome",
                "latest_initiation_ms", "latest_reach_to_grasp_ms", "latest_grab_to_placement_ms",
                "latest_cue_to_placement_ms", "latest_starting_distance_m", "latest_controller_path_m",
                "latest_mean_reach_speed_mps", "trial_total_ms", "status" });
            workbook.Attempts.Add(new object[] { "session_id", "handedness", "hand_used", "block", "layout", "trial",
                "target_mug", "target_owner", "target_position", "attempt", "outcome", "grabbed_mug",
                "start_session_ms", "cue_session_ms", "cue_frame", "movement_session_ms", "grab_session_ms",
                "release_session_ms", "end_session_ms", "initiation_ms", "reach_to_grasp_ms", "grab_to_placement_ms",
                "cue_to_placement_ms", "cue_to_grab_ms", "attempt_elapsed_ms", "starting_distance_m",
                "controller_path_m", "mean_reach_speed_mps", "tracking_interrupted" });
        }

        private object MeanReachSpeed => hasReachSample && latestTimes[1] > 0 && !trackingInterrupted
            ? (object)(1000 * reachPath / latestTimes[1]) : null;
        private object StartingDistance => cueTime >= 0 ? (object)startingDistance : null;
        private object ReachPath => hasReachSample && !trackingInterrupted ? (object)reachPath : null;
        private string HandUsed => taskHand == 0 ? "left" : "right";
        private string OwnershipCode => Target == participantMug ? "participant" : Target == agentMug ? "agent" : "unowned";

        private void RecordAttempt(string outcome, double end)
        {
            workbook.Attempts.Add(new object[] { sessionId, handedness, HandUsed, blockIndex + 1, Layout, trialIndex + 1,
                MugNames[Target], OwnershipCode, PositionNames[Trial.Position], attemptNumber, outcome,
                actualMug < 0 ? null : MugNames[actualMug], Measured(Delta(sessionStart, attemptStart)),
                Measured(Delta(sessionStart, cueTime)), Measured(cueFrame), Measured(Delta(sessionStart, movementTime)),
                Measured(Delta(sessionStart, grabTime)), Measured(Delta(sessionStart, releaseTime)),
                Delta(sessionStart, end), Measured(latestTimes[0]), Measured(latestTimes[1]), Measured(latestTimes[2]),
                Measured(latestTimes[3]), Measured(Delta(cueTime, grabTime)), Delta(attemptStart, end), StartingDistance,
                ReachPath, MeanReachSpeed, trackingInterrupted ? 1 : 0 });
        }

        private void RecordTrial(string outcome, double end)
        {
            int[] slots = Trial.MugsAtPositions;
            var row = new object[] { sessionId, handedness, HandUsed, blockIndex + 1, Layout, trialIndex + 1,
                MugNames[Target], OwnershipCode, PositionNames[Trial.Position], MugNames[slots[0]], MugNames[slots[1]],
                MugNames[slots[2]], attemptNumber, failures[0], failures[1], failures[2], failures[3], outcome,
                Measured(latestTimes[0]), Measured(latestTimes[1]), Measured(latestTimes[2]), Measured(latestTimes[3]),
                StartingDistance, ReachPath, MeanReachSpeed, Delta(trialStart, end),
                outcome == "success" ? "complete" : "awaiting_retry" };
            int rowIndex = 1 + blockIndex * repetitions * 3 + trialIndex;
            if (rowIndex == workbook.Trials.Count) workbook.Trials.Add(row);
            else workbook.Trials[rowIndex] = row;
        }

        private bool SaveProgress()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(workbookPath));
                workbook.Session[1][16] = Delta(sessionStart, Now);
                workbook.Session[1][17] = completeAfterSave ? "complete" : "in_progress";
                workbook.Save(workbookPath);
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogException(error);
                stage = Stage.SaveError;
                OpenPanel("Excel save failed", "Give the headset to the researcher and retry the save.");
                Button(1, "Retry save", RetrySave);
                return false;
            }
        }

        private void RetrySave()
        {
            if (!SaveProgress()) return;
            if (pendingOutcome == null) ShowReady("Place your reaching hand in the start zone.");
            else ContinueAfterSave(pendingOutcome);
        }
    }
}
