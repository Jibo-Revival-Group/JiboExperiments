# Hub Service Design Document

## Overview

The Hub Service is the central orchestrator of the Jibo cloud system. It coordinates all communication between the robot and cloud services, managing speech recognition, natural language understanding, skill routing, and proactive behaviors. The Hub exposes WebSocket endpoints for real-time bidirectional communication with the robot.

## Location

`packages/hub/src/`

## Key Components

### HubService (`HubService.ts`)

Main service class extending `BaseService` from `@jibo/utils`. Initializes and manages all hub components.

**HubComponents** (dependency injection container):
- `parser: ParserClient` - NLU service client
- `skillConfigManager: SkillConfigManager` - Manages skill configurations
- `intentRouter: IntentRouter` - Routes intents to skills
- `skillRequestMaker: SkillRequestMaker` - Makes HTTP requests to skills
- `history: HistoryServiceClient` - History service client
- `hubSettings: HubSettings` - Hub configuration
- `settingsClient: SettingsClient` - Settings service client

### WebSocket Handlers

- **ListenHandler** (`listen/ListenHandler.ts`) - Handles `/listen` and `/v1/listen` endpoints
- **ProactiveSocketRequestHandler** (`proactive/ProactiveSocketRequestHandler.ts`) - Handles `/proactive` and `/v1/proactive` endpoints

### Transaction Handlers

- **ListenTransactionHandler** (`listen/ListenTransactionHandler.ts`) - State machine for listen transactions
- **ProactiveTransactionHandler** (`proactive/ProactiveTransactionHandler.ts`) - Handles proactive action selection

## WebSocket Endpoints

### Listen Endpoint

**URL:** `ws://hub:9000/listen` or `ws://hub:9000/v1/listen`

**Authentication:** Bearer JWT token in Authorization header

**Headers:**
- `x-jibo-transid` - Transaction ID
- `x-jibo-robotid` - Robot ID
- `x-jibo-logging-config` - Log level configuration

### Proactive Endpoint

**URL:** `ws://hub:9000/proactive` or `ws://hub:9000/v1/proactive`

**Authentication:** Same as listen endpoint

## Listen Transaction Flow

The listen transaction follows a state machine with the following states:

```
WAIT_LISTEN → ASR → NLU → ROUTE → DONE
WAIT_LISTEN → WAIT_CLIENT_ASR → NLU → ROUTE → DONE
WAIT_LISTEN → WAIT_CLIENT_NLU → ROUTE → DONE
```

### State Machine Implementation

**File:** `packages/hub/src/listen/ListenTransactionHandler.ts`

**States:**
- `WAIT_LISTEN` - Waiting for LISTEN message from robot
- `WAIT_CLIENT_ASR` - Waiting for client-provided ASR result
- `WAIT_CLIENT_NLU` - Waiting for client-provided NLU result
- `ASR` - Performing speech recognition
- `NLU` - Performing natural language understanding
- `ROUTE` - Routing to appropriate skill
- `DONE` - Transaction complete
- `STOP` - Transaction stopped

**Timeouts:**
- ASR: 40 seconds (configurable via sosTimeout, maxSpeechTimeout)
- Parser: 10 seconds
- Context: 5 seconds
- Skill: 10 seconds
- Transaction: 60 seconds (default)

### Robot-to-Hub Messages (Listen Flow)

1. **LISTEN** - Initiates listen transaction
   ```typescript
   {
     type: "LISTEN",
     msgID: "uuid",
     ts: 1234567890,
     data: {
       mode: "default" | "CLIENT_ASR" | "CLIENT_NLU",
       lang: "en-US",
       hotphrase: boolean,
       rules: string[],
       asr: {
         sosTimeout: number,
         maxSpeechTimeout: number,
         hints: string[],
         earlyEOS: string[]
       },
       agents: ExternalAgentRequest[]
     }
   }
   ```

2. **Audio Packets** - Binary audio data streamed after LISTEN

3. **CONTEXT** - Runtime context from robot
   ```typescript
   {
     type: "CONTEXT",
     msgID: "uuid",
     ts: 1234567890,
     data: {
       general: {
         accountID: string,
         robotID: string,
         lang: string,
         release: string
       },
       runtime: {
         character: { emotion, motivation },
         location: { city, state, country, lat, lng },
         loop: { users, jibo, owner, loopId },
         perception: { speaker, peoplePresent },
         dialog: { referent }
       },
       skill: {
         id: string,
         session: { id, nodeID, data, trace }
       }
     }
   }
   ```

4. **CLIENT_ASR** - Client-provided ASR result (for menu clicks, etc.)
   ```typescript
   {
     type: "CLIENT_ASR",
     msgID: "uuid",
     ts: 1234567890,
     data: {
       text: string
     }
   }
   ```

5. **CLIENT_NLU** - Client-provided NLU result
   ```typescript
   {
     type: "CLIENT_NLU",
     msgID: "uuid",
     ts: 1234567890,
     data: {
       intent: string,
       entities: {},
       rules: []
     }
   }
   ```

### Hub-to-Robot Messages (Listen Flow)

#### 1. SOS (Start of Speech)

**Emitted when:** Speech is detected during ASR

**Location:** `ListenTransactionHandler.emitSOS()`

```typescript
{
  type: "SOS",
  msgID: "uuid",
  ts: 1234567890,
  data: null,
  timings: {
    total: number
  }
}
```

**Trigger conditions:**
- Google Cloud Speech API detects start of speech
- ASRSession calls `onStartOfSpeech` callback
- Clears SOS timeout timer

#### 2. EOS (End of Speech)

**Emitted when:** Speech ends during ASR

**Location:** `ListenTransactionHandler.emitEOS()`

```typescript
{
  type: "EOS",
  msgID: "uuid",
  ts: 1234567890,
  data: null,
  timings: {
    total: number
  }
}
```

**Trigger conditions:**
- Google Cloud Speech API detects end of speech
- ASRSession calls `onEndOfSpeech` callback
- Clears max speech timeout timer

#### 3. LISTEN Response (ASR/NLU Result)

**Emitted when:** ASR and NLU processing complete

**Location:** `ListenTransactionHandler.emitListenResult()`

```typescript
{
  type: "LISTEN",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    asr: {
      text: string,
      confidence: number,
      annotation: "GARBAGE" | "SOS_TIMEOUT" | "MAX_SPEECH_TIMEOUT"
    },
    nlu: {
      intent: string,
      entities: {},
      rules: []
    },
    match: {
      skillID: string,
      launch: boolean,
      onRobot: boolean
    } | null
  },
  final: boolean,
  timings: {
    total: number,
    asr: number,
    nlu: number
  }
}
```

**Emission scenarios:**
- **No match:** `match: null, final: true` - No skill matched the NLU result
- **On-robot skill:** `match.onRobot: true, final: true` - Skill runs on robot, Hub done
- **Cloud skill:** `match.onRobot: false, final: false` - Skill runs in cloud, Hub will send skill actions

#### 4. SKILL_ACTION

**Emitted when:** Cloud skill returns an action to execute

**Location:** `TransactionHandler.emitSkillResult()`

```typescript
{
  type: "SKILL_ACTION",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    action: {
      type: "JCP",
      config: {
        version: "1.0.0",
        jcp: SupportedBehaviors  // SLIM, Sequence, Parallel, SetPresentPerson, ImpactEmotion
      }
    },
    analytics?: AnalyticsData,
    fireAndForget?: boolean
  },
  final: boolean,
  timings: {
    total: number,
    skill: number
  }
}
```

**JCP Behavior Types:**
- `SLIM` - Single behavior execution
- `Sequence` - Sequential behavior execution
- `Parallel` - Parallel behavior execution
- `SetPresentPerson` - Set focused person
- `ImpactEmotion` - Modify Jibo's emotional state

**Emission scenarios:**
- **Non-final:** `final: false` - Robot should execute action and send CMD_RESULT back
- **Final:** `final: true` - Transaction complete, no more actions expected

#### 5. SKILL_REDIRECT

**Emitted when:** Skill redirects to another skill

**Location:** `TransactionHandler.emitSkillRedirectNotification()`

```typescript
{
  type: "SKILL_REDIRECT",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    match: {
      skillID: string,
      launch: boolean,
      onRobot: boolean
    },
    nlu: NLUResult,
    asr: ASRResult,
    memo: any
  },
  final: boolean
}
```

**Emission scenarios:**
- Skill returns `SKILL_REDIRECT` response
- Hub launches new skill with provided context
- Only one level of redirect supported (error on second redirect)

#### 6. ERROR

**Emitted when:** An error occurs during transaction

**Location:** `TransactionHandler.emitSkillResult()` (error case)

```typescript
{
  type: "ERROR",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    message: string
  },
  final: true,
  timings: {
    total: number
  }
}
```

### Listen Transaction State Transitions

#### WAIT_LISTEN → ASR

**Trigger:** LISTEN message received with mode="default"

**Actions:**
- Initialize ASRSession with Google Cloud Speech API
- Start audio streaming
- Set up SOS timeout (if configured)
- Set up max speech timeout (if configured)

#### WAIT_LISTEN → WAIT_CLIENT_ASR

**Trigger:** LISTEN message received with mode="CLIENT_ASR"

**Actions:**
- Emit fake SOS (immediate)
- Wait for CLIENT_ASR message from robot

#### WAIT_LISTEN → WAIT_CLIENT_NLU

**Trigger:** LISTEN message received with mode="CLIENT_NLU"

**Actions:**
- Emit fake SOS (immediate)
- Wait for CLIENT_NLU message from robot

#### ASR → NLU

**Trigger:** ASR completes successfully

**Actions:**
- Stop ASR session
- Normalize ASR text
- Check for garbage annotation (skip NLU if garbage)
- Wait for CONTEXT message (5 second timeout)
- Send ASR text to Parser service

#### WAIT_CLIENT_ASR → NLU

**Trigger:** CLIENT_ASR message received

**Actions:**
- Use provided ASR text
- Emit fake EOS
- Proceed to NLU

#### WAIT_CLIENT_NLU → ROUTE

**Trigger:** CLIENT_NLU message received

**Actions:**
- Use provided NLU result
- Emit fake EOS
- Skip NLU, proceed to routing

#### NLU → ROUTE

**Trigger:** Parser returns NLU result

**Actions:**
- Wait for CONTEXT message (5 second timeout)
- Call IntentRouter to match skill
- Apply DecisionMediator for external factors
- Route to matched skill or context skill

#### ROUTE → DONE

**Trigger:** Routing complete

**Actions:**
- For on-robot skills: Emit LISTEN with match, transaction done
- For cloud skills: Get skill response, emit SKILL_ACTION, transaction done
- For no match: Emit LISTEN with match=null, transaction done

## Intent Routing

### IntentRouter (`intent/IntentRouter.ts`)

Matches NLU results to registered cloud skills.

**Routing Logic:**
1. Check if NLU has intent and 'launch' rule
2. Query all skill configurations
3. Match intent against skill intent configurations
4. Match entities against skill entity configurations
5. Return first matching skill decision

**DecisionMediator** (`intent/DecisionMediator.ts`):
- Can alter routing decisions based on external factors
- Considers robot release version
- May redirect to different skill based on context

**IRDecisionMaker** (`intent/IRDecisionMaker.ts`):
- Core matching algorithm
- Compares intent names and entity values
- Supports exact match and NOT match rules

### Skill Request Maker (`skill/SkillRequestMaker.ts`)

Makes HTTP requests to cloud skills.

**Methods:**
- `skillLaunch(skillID, data, jiboHeaders, log)` - Launch new skill
- `skillLaunchOrUpdate(skillID, data, jiboHeaders, log, update)` - Launch or update skill
- `proactiveLaunch(skillID, data, jiboHeaders, log)` - Proactive launch

**Request Format:**
```typescript
{
  type: "LISTEN_LAUNCH" | "LISTEN_UPDATE" | "PROACTIVE_LAUNCH",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    general: { accountID, robotID, lang, release },
    runtime: { character, location, loop, perception, dialog },
    skill: { id, session? },
    result?: any,  // For UPDATE
    nlu: NLUResult,
    asr: ASRResult,
    memo?: any
  }
}
```

**Timeout:** 10 seconds (configurable)

**Error Handling:**
- `SKILL_NOT_FOUND` - Skill does not exist or is on-robot
- `TIMEOUT` - Skill request timeout

## Proactive Flow

### Proactive Transaction Handler (`proactive/ProactiveTransactionHandler.ts`)

Handles proactive action selection based on context, history, and settings.

### Robot-to-Hub Messages (Proactive Flow)

1. **TRIGGER** - Initiates proactive selection
   ```typescript
   {
     type: "TRIGGER",
     msgID: "uuid",
     ts: 1234567890,
     data: {
       triggerData: {
         triggerType: string,
         looperID?: string
       },
       triggerSource: "SURPRISE" | "OTHER"
     }
   }
   ```

2. **CONTEXT** - Runtime context (same as listen flow)

### Hub-to-Robot Messages (Proactive Flow)

#### PROACTIVE Match Response

**Emitted when:** Proactive action selected

**Location:** `ProactiveTransactionHandler.emitMatchResponse()`

```typescript
{
  type: "PROACTIVE",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    match: {
      skillID: string,
      onRobot: boolean,
      isProactive: true,
      launch: true,
      skipSurprises: boolean
    }
  },
  final: boolean
}
```

**Emission scenarios:**
- **On-robot skill:** `final: true` - Robot handles skill, Hub done
- **Cloud skill:** `final: false` - Hub will send skill actions

#### PROACTIVE No-Action Response

**Emitted when:** No eligible proactive action found

**Location:** `ProactiveTransactionHandler.emitNoActionResponse()`

```typescript
{
  type: "PROACTIVE",
  msgID: "uuid",
  ts: 1234567890,
  data: {},
  final: true
}
```

### Proactive Action Selection Algorithm

**File:** `ProactiveTransactionHandler.getEligibleActions()`

**Steps:**

1. **Get all proactive skill configurations**
   - Query SkillConfigManager for skills with proactive registrations

2. **Gather transaction data**
   - Extract focused person, present people, loop ID, robot ID
   - Use ContextTools to extract context fields

3. **Fetch user settings** (if focused person)
   - Batch request to SettingsClient for all skill settings
   - Consolidate into skill settings map

4. **Filter by context rules**
   - Check time-based rules (time of day, day of week)
   - Check location rules
   - Check people present rules
   - Check robot state rules

5. **Filter by interaction history rules**
   - Query History service for past interactions
   - Check frequency rules (e.g., "at most once per hour")
   - Check recency rules (e.g., "not in last 10 minutes")
   - Check sequence rules (e.g., "after greeting skill")

6. **Filter by settings rules**
   - Check user preferences for each skill
   - Check enabled/disabled status
   - Check custom parameters

7. **Select action**
   - Currently: Random selection from eligible actions
   - Future: Heuristics based on context, engagement, topics

### Context Tools (`proactive/tools/ContextTools.ts`)

Helper functions for context rule evaluation:

- `extractContextData(field, context, requestData, log)` - Extract specific context field
- `checkContextRules(registration, context, requestData, log)` - Evaluate all context rules

### History Rules Checker (`proactive/tools/IHRulesChecker.ts`)

Evaluates interaction history rules:

- `checkIHRules(registrations, IHQueries, data, log)` - Filter by history rules
- Queries History service for past skill launches
- Applies frequency, recency, and sequence constraints

### Settings Rules Checker (`proactive/tools/SettingsRulesChecker.ts`)

Evaluates user settings:

- `getSkillSettingsMap(skillConfigs, accountID, loopID, transID)` - Batch fetch settings
- `checkSettingsRegistrations(registrations, skillSettingsMap)` - Filter by settings

## Skill Interaction Flow (Cloud Skills)

### Initial Launch

1. Hub sends LISTEN_LAUNCH request to skill
2. Skill processes request, returns SKILL_ACTION
3. Hub sends SKILL_ACTION to robot
4. Robot executes action, sends CMD_RESULT to Hub
5. Hub sends LISTEN_UPDATE request to skill with action result
6. Skill processes result, returns next SKILL_ACTION or final=true
7. Repeat steps 3-6 until skill returns final=true

### Skill Redirect

1. Skill returns SKILL_REDIRECT response
2. Hub emits SKILL_REDIRECT notification to robot
3. Hub sends launch request to new skill
4. New skill proceeds with normal flow
5. Error if second redirect attempted

## Message Timing

### Listen Transaction Timing

**Timings tracked:**
- `total` - Total transaction time
- `asr` - ASR processing time
- `nlu` - NLU processing time
- `skill` - Skill processing time

**Timing emission:**
- SOS/EOS include timing from start
- LISTEN response includes ASR and NLU timings
- SKILL_ACTION includes skill timing

### Proactive Transaction Timing

**Timings tracked:**
- `total` - Total transaction time
- `skill` - Skill processing time

## Error Handling

### Hub Error Codes (`HubErrorCode.ts`)

- `TIMEOUT_ASR` - ASR timeout (40 seconds)
- `TIMEOUT_PARSER` - Parser timeout (10 seconds)
- `TIMEOUT_CONTEXT` - Context timeout (5 seconds)
- `TIMEOUT_SKILL` - Skill timeout (10 seconds)
- `PARSER` - Parser error
- `ASR` - ASR error

### Error Response Format

```typescript
{
  type: "ERROR",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    message: string,
    code?: string
  },
  final: true,
  timings: {
    total: number
  }
}
```

## Speech History Recording

### Optional Features

**Configuration:**
- `ETCO_hub_recordLaunchHistory` - Record skill launches to MongoDB
- `ETCO_hub_recordSpeechHistory` - Record speech interactions to MongoDB
- `ETCO_hub_recordSpeechLogBucket` - Upload speech logs to S3

### Speech History Record

**Data recorded:**
- Robot ID, account ID, transaction ID
- Timestamp
- ASR result
- NLU result
- Match data
- Skill response
- Redirect data
- Error (if any)

### S3 Upload

**Format:** JSON with audio as base64

**Path:** `{robotID}/year={year}/month={month}/day={day}/{timestamp}-{transID}.json`

## Hub Configuration

### Environment Variables

**Hub Settings:**
- `ETCO_hub_recordLaunchHistory` - Enable launch history
- `ETCO_hub_recordSpeechHistory` - Enable speech history
- `ETCO_hub_recordSpeechLogBucket` - S3 bucket for speech logs

**Authentication:**
- `ETCO_server_hubTokenSecret` - JWT secret for token verification

### Skill Configuration

**Sources:**
- `skills-local.json` - Local development configuration
- Environment variables - Production configuration
- Settings service - Dynamic configuration

**Skill Config Structure:**
```typescript
{
  id: string,
  intents: [{
    name: string,
    entities?: [{ name, value, matchRule }],
    memo?: any
  }],
  proactives?: [{
    triggerType: string,
    contextRules?: ContextRule[],
    IHRules?: IHRule[],
    settingsRules?: SettingsRule[],
    memo?: any
  }],
  IHQueries?: IHQueryDefinitions,
  onRobot?: boolean,
  URL: string,
  settings?: ManifestSettings
}
```

## Summary of Server-to-Robot Communication

### Listen Flow

1. **SOS** - Speech detected
2. **EOS** - Speech ended
3. **LISTEN** - ASR/NLU result with match data
4. **SKILL_ACTION** - JCP action to execute (repeated for multi-turn)
5. **SKILL_REDIRECT** - Skill redirect notification
6. **ERROR** - Error occurred

### Proactive Flow

1. **PROACTIVE** - Match or no-action response
2. **SKILL_ACTION** - JCP action to execute (if cloud skill)
3. **SKILL_REDIRECT** - Skill redirect notification
4. **ERROR** - Error occurred

### Key Design Principles

1. **State Machine** - Clear state transitions with validation
2. **Timeouts** - Every operation has a timeout to prevent hanging
3. **Error Handling** - Errors propagate to robot with clear messages
4. **Timing** - All operations are timed for monitoring
5. **History** - All interactions are recorded for analysis
6. **Flexibility** - Supports on-robot and cloud skills
7. **Proactivity** - Context-aware action selection
