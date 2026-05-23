# Original Jibo Server (Pegasus) Design Document

## Executive Summary

The original Jibo server, codenamed "Pegasus" (formerly V1.X), is a cloud-based microservices architecture that powers the Jibo social robot's conversational AI capabilities. It is built as a Lerna monorepo using Node.js/TypeScript and deployed via Docker containers. The system processes speech, performs natural language understanding, routes to appropriate skills, and manages proactive behaviors.

## Architecture Overview

### Monorepo Structure

The codebase is organized as a Lerna monorepo with the following main packages:

- **packages/hub** - Central orchestration service
- **packages/parser** - NLU (Natural Language Understanding) service
- **packages/history** - Data persistence service (MongoDB)
- **packages/baseskill** - Base class and framework for cloud skills
- **packages/interfaces** - TypeScript interfaces and API contracts
- **packages/utils** - Shared utility libraries
- **packages/chitchat-skill** - Example conversational skill
- **packages/report-skill** - Reporting skill
- **packages/lasso** - External data integration service
- **packages/hub-client** - Client library for hub communication
- **packages/history-client** - Client library for history service
- **packages/test-utils** - Testing utilities

### Technology Stack

- **Language**: TypeScript 2.5.3
- **Runtime**: Node.js 8.9.4
- **Package Manager**: Yarn 1.7.0
- **Containerization**: Docker
- **Orchestration**: Docker Compose (local), AWS ECS (production)
- **Database**: MongoDB 3.6.0
- **Cache**: Redis 3
- **NLU**: Dialogflow (API.ai)
- **ASR**: Google Cloud Speech API
- **WebSocket**: ws library
- **HTTP**: Express.js
- **Authentication**: JWT (jsonwebtoken)

## Core Services

### 1. Hub Service (`packages/hub`)

The Hub is the central orchestrator that coordinates all interactions between the robot and cloud services.

#### Key Components

**HubService** (`HubService.ts`)
- Main service class extending `BaseService`
- Initializes and manages all hub components
- Registers WebSocket and HTTP handlers

**HubComponents** - Dependency injection container:
- `parser: ParserClient` - NLU service client
- `skillConfigManager: SkillConfigManager` - Manages skill configurations
- `intentRouter: IntentRouter` - Routes intents to skills
- `skillRequestMaker: SkillRequestMaker` - Makes HTTP requests to skills
- `history: HistoryServiceClient` - History service client
- `hubSettings: HubSettings` - Hub configuration
- `settingsClient: SettingsClient` - Settings service client

#### Endpoints

**WebSocket Endpoints:**
- `/listen` and `/v1/listen` - Handles speech recognition and NLU
- `/proactive` and `/v1/proactive` - Handles proactive triggers

**HTTP Endpoints:**
- `/skills` and `/v1/skills` - Lists available skills
- `/healthcheck` - Service health check

#### Listen Flow

The listen transaction follows a state machine implemented in `ListenTransactionHandler`:

```
States:
  WAIT_LISTEN → ASR → NLU → ROUTE → DONE
  WAIT_LISTEN → WAIT_CLIENT_ASR → NLU → ROUTE → DONE
  WAIT_LISTEN → WAIT_CLIENT_NLU → ROUTE → DONE
```

**State Transitions:**

1. **WAIT_LISTEN** - Receives LISTEN message from robot
2. **ASR** - Performs Automatic Speech Recognition using Google Cloud Speech API
   - Streams audio packets
   - Emits SOS (Start of Speech) when speech detected
   - Emits EOS (End of Speech) when speech ends
   - Handles timeouts (SOS timeout, max speech timeout)
3. **NLU** - Sends ASR text to Parser service for intent recognition
   - Includes context (loop users, perception, etc.)
   - Supports external Dialogflow agents
4. **ROUTE** - Intent Router determines which skill to launch
   - Matches NLU result against skill intent configurations
   - Decision Mediator can alter decisions based on external factors
   - Routes to on-robot skills or cloud skills
5. **DONE** - Transaction complete

**Listen Transaction Handler** (`ListenTransactionHandler.ts`):
- Manages audio streaming via `AudioBuffer`
- Creates `ASRSession` for speech recognition
- Handles timeouts (ASR: 40s, Parser: 10s, Context: 5s, Skill: 10s)
- Records speech history to MongoDB and optionally S3
- Supports client-provided ASR/NLU (for menu clicks, etc.)
- Handles skill redirects

#### Proactive Flow

The proactive system allows Jibo to initiate conversations based on context, history, and triggers.

**Proactive Transaction Handler** (`ProactiveTransactionHandler.ts`):

1. Receives TRIGGER message from robot
2. Waits for CONTEXT message (robot state)
3. **Action Selection**:
   - Gets all proactive skill configurations
   - Filters by context rules (time, location, people present, etc.)
   - Filters by interaction history rules (frequency, recency)
   - Filters by user settings
   - Randomly selects from eligible actions
4. Launches selected skill (on-robot or cloud)
5. Returns match response or no-action response

**Proactive Registration**:
Skills register proactive behaviors with:
- Trigger types (time-based, event-based, surprise)
- Context rules (when this can trigger)
- Interaction history rules (how often it can trigger)
- Settings rules (user preferences)

### 2. Parser Service (`packages/parser`)

The Parser service performs Natural Language Understanding using Dialogflow.

**ParserService** (`ParserService.ts`):
- Starts RobustParser process on port 8787 (optional)
- Initializes Dialogflow client
- Initializes Robust Parser client
- Handles POST requests to `/v1/parse`
- Exposes state at `/state` endpoint

**NLU Pipeline:**
1. Receives text, rules, and context
2. Queries Dialogflow with configured agents
3. Optionally queries Robust Parser (custom NLU)
4. Returns intent, entities, and rules

**Configuration:**
- Dialogflow API key
- Robust Parser enable/disable
- Multiple external agents support

### 3. History Service (`packages/history`)

The History service persists interaction data to MongoDB.

**HistoryService** (`HistoryService.ts`):
- Two database clients:
  - `SkillLaunchDBClient` - Records skill launches
  - `SpeechHistoryDBClient` - Records speech interactions (optional)
- HTTP endpoints:
  - `/v1/skill/launch` - Skill launch history
  - `/v1/speech` - Speech history (if enabled)
- Health check endpoint

**Data Stored:**
- Skill launches (skill ID, intent, timestamp, robot ID, account ID)
- Speech interactions (ASR result, NLU result, audio file URL, error tracking)

### 4. Lasso Service (`packages/lasso`)

Lasso provides external data integration for skills.

**Features:**
- OAuth2 credential management
- Calendar client integration
- Weather data (Dark Sky API)
- Maps data (Google Maps API)
- News data (AP News)
- MongoDB for credential storage
- Redis for caching

**LassoService** (`LassoService.ts`):
- Manages OAuth2 flows
- Provides relay endpoints for external APIs
- Caches responses in Redis

## Skill Framework

### BaseSkill (`packages/baseskill`)

**BaseSkill** (`BaseSkill.ts`):
- Abstract base class for all cloud skills
- Extends `BaseHttpHandler`
- Handles POST requests to `/`
- Provides error handling
- Tracks timing

**GraphSkill** (`GraphSkill.ts`):
- Extends BaseSkill with graph-based state machine
- Implements node-based conversation flow
- Supports skill redirects
- Tracks analytics events
- Supports supplemental behaviors (parallel/sequence)

### Graph System

The graph system provides a state machine framework for skills.

**Graph** (`Graph.ts`):
- Directed graph of connected nodes
- Supports subgraphs (hierarchical)
- Exit transitions for graph termination
- Validation (reachability, transition completeness)
- GraphViz dot file generation

**GraphManager** (`GraphManager.ts`):
- Singleton per skill
- Manages node IDs and mappings
- Executes graph:
  - `start()` - Creates session, enters initial node
  - `enterNode()` - Calls node's enter method
  - `exitNode()` - Calls node's exit method with action results
  - `executeTransition()` - Moves to next node
- Maintains session state (node ID, data, trace)

**Node** (`Node.ts`):
- Abstract base class for graph nodes
- Has transition names and destinations
- Two lifecycle methods:
  - `enter(data)` - Called when node is entered, returns action or redirect
  - `exit(data)` - Called with action results, returns next transition
- Supports graph traversal (BFS)

**Built-in Node Types:**
- `DefaultNode` - Simple terminal node
- `JCPNode` - Returns JCP action
- `NoOpNode` - No operation
- `TrueFalseNode` - Conditional branching
- `SetLooperIDNode` - Sets speaker ID

**MIM (Motion Interaction Model) System:**
- `ANFactory` - Creates graph for playing MIM animations
- Supports scripted responses, emotion responses, fallback responses
- Semi-specific responses (context-aware)

### Skill Request/Response Protocol

**Skill Request Types** (`skill/request.ts`):
- `LISTEN_LAUNCH` - Launch skill from listen interaction
- `LISTEN_UPDATE` - Update skill with action results
- `PROACTIVE_LAUNCH` - Launch skill proactively

**Skill Request Data:**
```typescript
{
  type: MessageType,
  msgID: UUID,
  ts: number,
  data: {
    general: { accountID, robotID, lang, release },
    runtime: { character, location, loop, perception, dialog },
    skill: { id, session? },
    result: any,  // Action results for UPDATE
    nlu: NLUResult,
    asr: ASRResult,
    memo?: any
  }
}
```

**Skill Response Types** (`skill/response.ts`):
- `SKILL_ACTION` - Returns action to execute
- `SKILL_REDIRECT` - Redirects to another skill
- `ERROR` - Error response

**Skill Action Data:**
```typescript
{
  action: JCPAction,  // JCP protocol behavior
  analytics?: AnalyticsData,
  final?: boolean,  // Is this the final response?
  fireAndForget?: boolean
}
```

**JCP Action** (`skill/action.ts`):
```typescript
{
  type: ActionType.JCP,
  config: {
    version: "1.0.0",
    jcp: SupportedBehaviors  // SLIM, Sequence, Parallel, SetPresentPerson, ImpactEmotion
  }
}
```

### Skill Configuration

**SkillConfig** (`skill/config.ts`):
```typescript
{
  id: SkillID,
  intents: [{
    name: IntentName,
    entities?: EntityConfig[],
    memo?: any
  }],
  proactives?: ProactiveRegistration[],
  IHQueries?: IHQueryDefinitions,
  onRobot?: boolean,
  URL: string,
  settings?: ManifestSettings
}
```

**Entity Config**:
- `name` - Entity name
- `value` - Expected value
- `matchRule` - 'EXACT' or 'NOT'

**Proactive Registration**:
- Trigger type and conditions
- Context rules
- Interaction history rules
- Settings rules

## Interfaces Package

The `interfaces` package defines all TypeScript interfaces for communication between services.

### Key Interface Modules

**service.ts** - Base message types:
- `BaseMessage<T, D>` - Generic message with type, msgID, timestamp, data
- `BaseResponse<T, D>` - Response with final flag and timings
- `IAuthDetails` - Authentication details (account ID, access keys)

**hub/** - Hub-specific interfaces:
- `request.ts` - LISTEN, CONTEXT, CLIENT_ASR, CLIENT_NLU messages
- `response.ts` - ASR, NLU, LISTEN, SKILL_REDIRECT, ERROR responses
- `MessageType.ts` - Message type enums
- `HubErrorCode.ts` - Error code enums

**skill/** - Skill-specific interfaces:
- `request.ts` - LISTEN_LAUNCH, LISTEN_UPDATE, PROACTIVE_LAUNCH
- `response.ts` - SKILL_ACTION, SKILL_REDIRECT, ERROR
- `action.ts` - JCP action types
- `config.ts` - Skill configuration
- `behaviors.ts` - Supported JCP behaviors
- `analytics.ts` - Analytics event types

**nlu.ts** - NLU interfaces:
- `NLURequestData` - Text, rules, loop users, external agents
- `NLUResult` - Intent, entities, rules
- `ExternalAgentRequest` - External Dialogflow agent config

**asr.ts** - ASR interfaces:
- `ASRResult` - Text, confidence, annotation
- `ASRConfig` - Language, hints, timeouts

**jibo/** - Jibo-specific data:
- `data.ts` - GeneralData (account, robot, language), SkillData (session, trace)
- `runtime.ts` - RuntimeContext (character, location, loop, perception, dialog)

**proactive/** - Proactive interfaces:
- Context field definitions
- History rules
- Settings rules
- Proactive trigger/request/response

**history/** - History interfaces:
- Skill launch data
- Speech history data

## Utils Package

The `utils` package provides shared functionality.

### BaseService (`utils/service/BaseService.ts`)

Base class for all Pegasus services:

**Features:**
- Express.js HTTP server
- WebSocket server (ws library)
- JWT authentication
- Request/response logging with jibo-log
- New Relic monitoring
- Health check endpoint
- Error handling middleware

**Methods:**
- `addSocketHandler(path, handler)` - Register WebSocket handler
- `addHttpHandler(path, handler)` - Register HTTP handler
- `init(port)` - Start server
- `close()` - Stop server

**Authentication:**
- JWT token verification
- Bearer token scheme
- Configurable secret via `ETCO_server_hubTokenSecret`

**Logging:**
- Per-request log instances
- Transaction ID tracking
- Robot ID tracking
- Configurable log levels per namespace

### Other Utils

- `PegasusRequest` - Enhanced Express request with Jibo headers
- `PegasusWebSocket` - Enhanced WebSocket with auth and logging
- `JiboHeaders` - Parses Jibo-specific headers (transID, robotID, logging config)
- `ResponseWrapper` - Wraps WebSocket responses
- `HttpError` - HTTP error with status code

## Communication Protocols

### WebSocket Protocol

**Connection:**
- URL: `ws://hub:9000/listen` or `ws://hub:9000/proactive`
- Authentication: Bearer token in Authorization header
- Headers: `x-jibo-transid`, `x-jibo-robotid`, `x-jibo-logging-config`

**Message Format:**
```json
{
  "type": "MESSAGE_TYPE",
  "msgID": "uuid",
  "ts": 1234567890,
  "data": { ... }
}
```

**Listen Flow Messages:**
1. Robot → Hub: LISTEN (with ASR config, rules, language)
2. Robot → Hub: Audio packets (binary)
3. Hub → Robot: SOS (Start of Speech)
4. Robot → Hub: CONTEXT (runtime context)
5. Hub → Robot: EOS (End of Speech)
6. Hub → Robot: LISTEN (with ASR result, NLU result, match)
7. Hub → Robot: SKILL_ACTION (if cloud skill)
8. Robot → Hub: CMD_RESULT (action results)
9. Hub → Robot: SKILL_ACTION (next action) or final

**Proactive Flow Messages:**
1. Robot → Hub: TRIGGER (trigger data)
2. Robot → Hub: CONTEXT (runtime context)
3. Hub → Robot: PROACTIVE (match or no-action)
4. Hub → Robot: SKILL_ACTION (if cloud skill)

### HTTP Protocol

**Skill Request:**
- Method: POST
- URL: `http://skill-host:port/`
- Headers: Authorization, x-jibo-transid, x-jibo-robotid
- Body: SkillRequest JSON

**Parser Request:**
- Method: POST
- URL: `http://parser:8080/v1/parse`
- Body: NLURequestData JSON

## Authentication & Security

### JWT Authentication

**Token Format:**
```json
{
  "id": "account-id",
  "accessKeyId": "client-id",
  "secretAccessKey": "client-secret",
  "friendlyId": "robot-name"
}
```

**Verification:**
- Secret: `ETCO_server_hubTokenSecret` environment variable
- Scheme: Bearer
- Applied to WebSocket connections and HTTP endpoints

### Network Security

- All services run in Docker containers
- Services communicate via Docker network (pegasus-nw)
- External access via load balancer
- TLS termination at load balancer

## Deployment

### Docker Compose (Local Development)

**Services:**
- `hub` - Hub service (port 9000)
- `parser` - Parser service (port 9005)
- `history` - History service (port 9006)
- `chitchat-skill` - Chitchat skill (port 9004)
- `report-skill` - Report skill (port 9003)
- `lasso` - Lasso service (port 9007)
- `redis` - Redis cache (port 6379)
- `mongo_lasso` - MongoDB for Lasso (port 27017)
- `history_cluster` - MongoDB for History (from docker-compose-history-db.yml)

**Configuration:**
- Environment variables prefixed with `ETCO_` (ETCO = Environment TO Configuration)
- Volume mounting: `./:/pegasus:consistent` for live code editing
- Debug ports: 5850-5855 for Node.js debugging

### Build Process

**Commands:**
```bash
docker build -t pegasus_base:latest .
yarn docker:bootstrap
yarn docker:build
./pegasus.js build-docker-image --services hub
```

**CLI Tool** (`cli/`):
- `bootstrap` - Install dependencies
- `build` - Build TypeScript
- `test` - Run tests
- `docker-run` - Run commands in Docker
- `build-docker-image` - Build Docker images for services

### Production Deployment

- AWS ECS (Elastic Container Service)
- ECR (Elastic Container Registry) for Docker images
- Application Load Balancer
- MongoDB Atlas for production databases
- ElastiCache for Redis
- CloudWatch for logging
- New Relic for monitoring

## Data Flow Examples

### Example 1: User Says "Tell Me a Joke"

1. **Robot → Hub**: LISTEN message with ASR config
2. **Robot → Hub**: Audio stream
3. **Hub**: Detects SOS, emits SOS message
4. **Hub**: Streams audio to Google Cloud Speech API
5. **Hub**: Detects EOS, emits EOS message
6. **Robot → Hub**: CONTEXT message (runtime state)
7. **Hub → Parser**: POST /v1/parse with text "tell me a joke"
8. **Parser → Dialogflow**: Query with "joke" intent rules
9. **Dialogflow → Parser**: Intent="joke_tell", entities={}
10. **Parser → Hub**: NLU result
11. **Hub → IntentRouter**: Match intent to "joke-skill"
12. **Hub → joke-skill**: POST LISTEN_LAUNCH request
13. **joke-skill**: Executes graph, selects joke
14. **joke-skill → Hub**: SKILL_ACTION with JCP behavior (SayText)
15. **Hub → Robot**: SKILL_ACTION message
16. **Robot**: Executes behavior, speaks joke
17. **Robot → Hub**: CMD_RESULT with action result
18. **Hub → joke-skill**: POST LISTEN_UPDATE request
19. **joke-skill**: Returns final=true
20. **Hub → Robot**: Final SKILL_ACTION

### Example 2: Proactive Greeting

1. **Robot**: Detects person entering room
2. **Robot → Hub**: TRIGGER message with trigger data
3. **Robot → Hub**: CONTEXT message (runtime state)
4. **Hub**: Queries all proactive skill configs
5. **Hub**: Filters by context (time, people present)
6. **Hub**: Filters by history (last greeting time)
7. **Hub**: Filters by settings (user greeting preference)
8. **Hub**: Selects "greeting-skill"
9. **Hub → greeting-skill**: POST PROACTIVE_LAUNCH request
10. **greeting-skill → Hub**: SKILL_ACTION with greeting behavior
11. **Hub → Robot**: PROACTIVE response with match
12. **Hub → Robot**: SKILL_ACTION message
13. **Robot**: Executes greeting

## Error Handling

### Error Types

**Hub Error Codes** (`HubErrorCode.ts`):
- `TIMEOUT_ASR` - ASR timeout
- `TIMEOUT_PARSER` - Parser timeout
- `TIMEOUT_CONTEXT` - Context timeout
- `TIMEOUT_SKILL` - Skill timeout
- `PARSER` - Parser error
- `ASR` - ASR error

**Skill Request Errors** (`SkillRequestError`):
- `SKILL_NOT_FOUND` - Skill does not exist
- `TIMEOUT` - Skill request timeout

### Error Response Format

```json
{
  "type": "ERROR",
  "msgID": "uuid",
  "ts": 1234567890,
  "final": true,
  "data": {
    "message": "Error description",
    "code": "ERROR_CODE"
  },
  "timings": {
    "total": 1234
  }
}
```

### Timeout Handling

- ASR: 40 seconds (configurable via sosTimeout, maxSpeechTimeout)
- Parser: 10 seconds
- Context: 5 seconds
- Skill: 10 seconds
- Transaction: 60 seconds (configurable)

## Monitoring & Logging

### Logging

**jibo-log Integration:**
- Per-namespace log levels
- Transaction ID correlation
- Robot ID tracking
- Structured logging support

**Log Levels:**
- Configured via `x-jibo-logging-config` header
- Per-namespace granularity
- Environment variable: `ETCO_server_logLevel`

### Monitoring

**New Relic:**
- HTTP request tracking
- WebSocket transaction tracking
- Error tracking
- Custom attributes (transID, robotID)

**Health Checks:**
- `/healthcheck` endpoint on all services
- Returns service-specific health data
- Database connection status

### Speech History Recording

**Optional Features:**
- Record skill launches to MongoDB
- Record speech interactions to MongoDB
- Upload speech logs to S3 (JSON with audio base64)

**Configuration:**
- `ETCO_hub_recordLaunchHistory` - Enable launch history
- `ETCO_hub_recordSpeechHistory` - Enable speech history
- `ETCO_hub_recordSpeechLogBucket` - S3 bucket for speech logs

## Skill Development Guide

### Creating a New Skill

1. **Extend GraphSkill:**
```typescript
export class MySkill extends GraphSkill<Transition> {
  constructor() {
    super('my-skill');
  }

  createGraph(): Graph<Transition> {
    const g = new Graph('My Skill', generateTransitions<Transition>(Transition));
    // Add nodes and transitions
    g.finalize();
    return g;
  }
}
```

2. **Define Transitions:**
```typescript
enum Transition {
  Done = 'Done',
  Retry = 'Retry'
}
```

3. **Create Nodes:**
```typescript
class MyNode extends Node<Transition> {
  async enter(data: Data): Promise<EnterResponse> {
    // Return action or redirect
    return { action: myJCPAction };
  }

  async exit(data: Data): Promise<ExitResponse> {
    // Return next transition
    return { transition: Transition.Done };
  }
}
```

4. **Create Skill Manifest:**
```json
{
  "id": "my-skill",
  "intents": [
    {
      "name": "my_intent",
      "entities": []
    }
  ],
  "onRobot": false
}
```

5. **Register with Hub:**
- Add skill config to skills-local.json or environment
- Deploy skill service
- Hub will load configuration

### Skill Best Practices

- Use graph for complex flows, direct responses for simple ones
- Track analytics events for monitoring
- Handle errors gracefully with try-catch
- Use supplemental behaviors for parallel actions
- Set appropriate timeouts
- Log important events
- Test with both LISTEN_LAUNCH and PROACTIVE_LAUNCH

## Key Design Decisions

### Why Graph-Based Skills?

- **State Management**: Explicit state machine with session tracking
- **Visualization**: GraphViz generation for debugging
- **Reusability**: Subgraphs for common patterns
- **Testability**: Isolated node testing
- **Maintainability**: Clear flow structure

### Why WebSocket for Robot Communication?

- **Low Latency**: Real-time bidirectional communication
- **Audio Streaming**: Binary message support for audio
- **Stateful**: Single connection per transaction
- **Efficiency**: No HTTP overhead for each message

### Why Separate Services?

- **Scalability**: Scale each service independently
- **Isolation**: Failure in one service doesn't affect others
- **Technology**: Different services can use different tech stacks
- **Deployment**: Independent deployment cycles

### Why Lerna Monorepo?

- **Code Sharing**: Easy to share interfaces and utils
- **Versioning**: Linked versioning for interdependent packages
- **Development**: Single repository for all services
- **Testing**: Integration tests across packages

## Limitations & Known Issues

1. **Single Graph Manager**: Skills cannot have concurrent sessions (singleton pattern)
2. **Sequential Skill Redirects**: Only one level of redirect supported
3. **No Skill-to-Skill Communication**: Skills must go through hub
4. **Fixed Timeouts**: Hardcoded timeouts in some places
5. **No Skill Hot-Reload**: Requires container rebuild for skill changes
6. **Limited NLU**: Dialogflow dependency, no custom model training
7. **No Skill Versioning**: Skills identified by ID only
8. **Synchronous Skill Requests**: Hub waits for skill response (no async)

## Future Considerations

1. **Skill Versioning**: Support multiple versions of same skill
2. **Skill-to-Skill Direct Communication**: Allow skills to call each other
3. **Async Skill Responses**: Long-running skills with callback pattern
4. **Custom NLU Models**: Support for custom trained models
5. **Skill Hot-Reload**: Dynamic skill loading without restart
6. **Multi-Session Skills**: Support concurrent skill sessions
7. **Skill Marketplace**: Third-party skill distribution
8. **A/B Testing**: Framework for testing skill variations

## Conclusion

The original Jibo server (Pegasus) is a well-architected microservices system that provides a robust foundation for conversational AI on the Jibo robot. The graph-based skill framework offers flexibility and maintainability, while the separation of concerns enables independent scaling and development. The system successfully handles real-time speech processing, natural language understanding, skill routing, and proactive behaviors in a distributed cloud environment.
