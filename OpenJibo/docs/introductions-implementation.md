# Introductions Skill Implementation

## Overview
The introductions skill (`@be/introductions`) has been added to the OpenJibo .NET server to support the "meet someone new" functionality. This skill allows Jibo to learn user identities through face recognition, voice recognition, and name pronunciation learning.

## Skill Characteristics
The introductions skill is unique because:
- **Non-traditional turn approach**: Unlike most skills that follow a simple request-response pattern, introductions uses a multi-phase enrollment process
- **Learn pronunciation function**: Includes `jibo.jetstream.initNameLearning()` and `jibo.jetstream.startNameLearningTurn()` to learn how to pronounce user names
- **Multi-modal enrollment**: Supports face recognition (`FaceEnroller`), voice recognition (`VoiceEnroller`), and name pronunciation (`NameEnroller`)
- **Asset-pack type**: The skill is distributed as an asset pack containing animations, audio files, MIMs (Motion Interaction Modules), and timelines

## Implementation Details

### Intent Routing
Added to `JiboInteractionService.IntentRouting.cs`:
```csharp
if (MatchesAny(
        loweredTranscript,
        "meet someone new",
        "meet someone",
        "introductions",
        "introduce yourself",
        "introduce me"))
    return "introductions";
```

### Launch Decision
Added to `JiboInteractionService.LaunchDecisions.cs`:
```csharp
private static JiboInteractionDecision BuildIntroductionsLaunchDecision()
{
    return new JiboInteractionDecision(
        "introductions",
        "Starting introductions.",
        "@be/introductions",
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["skillId"] = "@be/introductions"
        });
}
```

### Decision Dispatch
Added to `JiboInteractionService.DecisionDispatch.cs`:
```csharp
"introductions" => BuildIntroductionsLaunchDecision(),
```

## Skill Flow
The introductions skill follows this flow:
1. **VoiceFaceTrainingMenu**: Asks who to get to know (supports all/voice/face/name enrollment types)
2. **Face Capture**: If face enrollment, captures and trains face recognition using `jibo.ics.sendTrainingRequest()`
3. **Voice Enrollment**: If voice enrollment, uses `jibo.jetstream.startEnrollmentTurn()` to capture voice samples
4. **Name Learning**: Uses `jibo.jetstream.initNameLearning()` and `jibo.jetstream.startNameLearningTurn()` to learn name pronunciation
5. **Completion**: Sets enrollment status in KB via `jibo.kb.loop.setEnrollmentFace()` and `jibo.kb.loop.setEnrollmentVoice()`

## Key Files in Introductions Skill
- `index.js`: Main skill logic with enrollment classes
- `mims/en-us/VoiceFaceTrainingMenu.mim`: Main menu for selecting enrollment type
- `animations/`: Face and body animations for enrollment process
- `audio/`: Sound effects for enrollment feedback
- `timelines/`: Animation timelines

## Testing
The implementation was verified by:
1. Building the .NET project successfully
2. Following the existing skill integration pattern (similar to radio, gallery, create)
3. Using the standard SKILL_REDIRECT pattern for local skill handoff

## Notes
- The skill runs locally on the robot (`@be/introductions`)
- The cloud server only handles the initial launch routing
- All enrollment processing happens robot-side through the Jibo SDK
- The skill prompt is "Meet someone new" as defined in package.json
