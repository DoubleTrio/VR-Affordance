# Ownership study

Open `Assets/Scenes/OwnershipExperiment.unity` in Unity 6000.3.21f1. The project’s scene list launches that scene first. Use the Android/OpenXR build target for Meta Quest 3.

## Run a session

1. Seat the participant and introduce the task in person. At the initial setup screen, use a controller’s pointing ray and trigger to select **Agent OFF**, **Agent ON**, or **Both layouts**.
2. Record the participant’s self-reported handedness and choose the controller they will use to reach. Confirm **2 × 3** for the study protocol. The screen also supports other multiples of three.
3. Have the participant choose their mug from the images. Review the ownership assignments on screen. When ready, select **Continue**.
4. On every attempt, put the reaching controller inside the raised start ring with grip released. Aim the other controller at **Start** and press its trigger. Wait for the gold target cue, grab the highlighted mug with the reaching controller’s grip, and release it upright on the placement pad.
5. An early reach, empty grip, wrong mug, invalid release, or tracking interruption calls for a new Start press and a retry with the same target and positions. A successful placement advances the session. There is no response timeout.

In the two-layout mode, each participant completes both layouts. On one headset, the session allocator shuffles six combinations of starting layout and position schedule before repeating the allocation cycle. With the six-trial setting, each mug is targeted twice in each layout, and its two target positions match across layouts. Trial order is shuffled independently. Use the same headset for a single allocation sequence.

The study scene exposes pilot settings for cue delay and movement detection in the **Ownership Experiment** component. The initial values are a 0.8–1.8 s cue delay, 5.5 cm start-zone radius, and onset at 2.5 cm displacement sustained for 45 ms. Finalize these values on the headset before data collection.

## Results

The app writes one `.xlsx` per session to `StudyResults` under Unity’s persistent data directory. On Quest, the current project’s Android application ID places the files under:

```
/sdcard/Android/data/com.DefaultCompany.MixedRealityTemplate/files/StudyResults
```

The workbook updates at setup and after each attempt. The completion screen appears after the final save. **New session** loads a fresh study scene.

| Sheet | Contents |
| --- | --- |
| Session | Session ID and number, handedness, reaching hand, layout order, ownership assignments, pilot settings, elapsed time, and completion status |
| Trials | One row per started scheduled trial, with target and position, cumulative error counts, latest attempt metrics, and elapsed time across retries |
| Attempts | One row per attempt, with error or success outcome, cue and interaction timestamps, starting target distance, and sampled controller path |

`miss_count` is an empty grip. `wrong_count` is a wrong-mug grab. `invalid_placement_count` includes drops and releases outside the pad. Trial total time spans the first Start press to the latest attempt end and includes intervening resets and waits. Times are in milliseconds and distances are in meters. Blank timing fields indicate an event that was unavailable or did not occur. Cue timestamps are Unity event times.
