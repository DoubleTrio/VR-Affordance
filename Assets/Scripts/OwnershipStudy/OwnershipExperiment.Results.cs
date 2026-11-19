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
            workbook.Trials.Add(new object[] { "session_id", "handedness", "hand_used", "block", "layout", "trial",
                "target_mug", "target_owner", "target_position", "starting_distance_m", "initiation_ms", "cue_to_grab_ms",
                "miss_count", "wrong_count", "premature_count", "invalid_placement_count", "tracking_interruption_count",
                "status" });
        }

        private string OwnershipCode => Target == participantMug ? "participant" : Target == agentMug ? "agent" : "unowned";

        private void RecordTrial(bool success)
        {
            var row = new object[] { sessionId, handedness, taskHand == 0 ? "left" : "right", blockIndex + 1,
                Layout, trialIndex + 1, MugNames[Target], OwnershipCode, PositionNames[Trial.Position],
                success ? (object)startingDistance : null,
                success ? Measured(Delta(cueTime, movementTime)) : null,
                success ? Measured(Delta(cueTime, grabTime)) : null,
                failures[0], failures[1], failures[2], failures[3], failures[4],
                success ? "complete" : "awaiting_retry" };
            int rowIndex = 1 + blockIndex * repetitions * 3 + trialIndex;
            if (rowIndex == workbook.Trials.Count) workbook.Trials.Add(row);
            else workbook.Trials[rowIndex] = row;
        }

        private bool SaveProgress()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(workbookPath));
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
