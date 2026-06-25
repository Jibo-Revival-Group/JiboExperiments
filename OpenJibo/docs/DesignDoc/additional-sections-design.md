# Additional Sections Design Document

## Overview

This document covers deployment strategies, data flow examples, error handling, monitoring and logging, and skill development guidelines for the Jibo cloud system.

## Deployment

### Docker Compose (Local Development)

**Location:** `docker-compose.yml`

**Purpose:** Provides local development environment with all services running in Docker containers.

#### Base Configuration

**YAML Anchor:**
```yaml
x-pegasus-defaults: &pegasus-defaults
  image: pegasus_base
  networks:
    - pegasus-nw
  volumes:
    - ./:/pegasus:consistent  # Live code editing
  command: yarn run start:debug
  environment:
    - ETCO_server_logLevel=debug
    - ETCO_server_env=local
    - ETCO_server_structuredLogs=false
```

**Key Features:**
- Shared base image for all services
- Volume mounting for live code editing
- Debug mode enabled
- Consistent environment variables

#### Services

**Hub Service:**
```yaml
hub:
  container_name: hub
  working_dir: /pegasus/packages/hub
  environment:
    - ETCO_hub_skillsConfig=skills-local.json
    - ETCO_server_hubTokenSecret=dev-hub-token-secret
    - NET_parser=parser:8080
    - NET_history=history:8080
  ports:
    - 9000:8080      # HTTP
    - 5850:5850      # Node debugging
  build:
    context: ./
```

**Parser Service:**
```yaml
parser:
  container_name: parser
  working_dir: /pegasus/packages/parser
  environment:
    - ETCO_parser_dialogflow_key=bca3ddc410a54274ac55bd678bff6747
  ports:
    - 9005:8080
    - 5851:5851
```

**History Service:**
```yaml
history:
  container_name: history
  working_dir: /pegasus/packages/history
  environment:
    - ETCO_history_skillLaunch_mongo=history_mongos:27017
    - ETCO_history_speechHistory_mongo=history_mongos:27017
  depends_on:
    - history_cluster
  ports:
    - 9006:8080
    - 5852:5852
```

**Skills:**
```yaml
chitchat-skill:
  container_name: chitchat-skill
  working_dir: /pegasus/packages/chitchat-skill
  ports:
    - 9004:8080
    - 5853:5853

report-skill:
  container_name: report-skill
  working_dir: /pegasus/packages/report-skill
  environment:
    - NET_lasso=lasso:8080
    - NET_settings=settings.jibo.aws
  ports:
    - 9003:8080
    - 5854:5854
```

**Lasso Service:**
```yaml
lasso:
  container_name: lasso
  working_dir: /pegasus/packages/lasso
  environment:
    - ETCO_lasso_darkSkyKey=d87d094ee8b8cec48b69c1149823c0fa
    - ETCO_lasso_googleMapsKey=Ri2CIo95Sa7dlwft5tQPixUtnPo=
    - ETCO_lasso_apNewsKey=@Pwf$$103103
    - NET_redis=redis:6379
    - ETCO_lasso_credentials_mongo=mongo_lasso:27017
  depends_on:
    - redis
    - mongo_lasso
  ports:
    - 9007:8080
    - 5855:5855
```

**Infrastructure Services:**
```yaml
redis:
  image: redis:3
  ports:
    - 6379:6379

mongo_lasso:
  image: mongo:3.6.0
  ports:
    - 27017:27017
```

#### Network

```yaml
networks:
  pegasus-nw:
```

All services communicate over the `pegasus-nw` Docker network.

#### Commands

**Start all services:**
```bash
docker-compose up
```

**Start specific service:**
```bash
docker-compose up hub
```

**Build and start:**
```bash
docker-compose up --build
```

**Stop all services:**
```bash
docker-compose down
```

### Dockerfile

**Location:** `Dockerfile`

**Base Image:**
```dockerfile
FROM node:8.16.0-slim
```

**Key Steps:**
1. Configure Debian archive (stretch is old, uses archive.debian.org)
2. Install apt-transport-https
3. Add Yarn repository and install Yarn 1.5.1-1
4. Install git, unzip, gnupg2, python3
5. Link Yarn to /usr/local/bin
6. Set PATH for node_modules/.bin
7. Set WORKDIR to /pegasus

**Environment Variables:**
```dockerfile
ENV PATH="/pegasus/node_modules/.bin:${PATH}"
WORKDIR /pegasus
```

### Build Process

**Location:** `scripts/quickbuild.sh`

**Purpose:** Parallel build of packages using Lerna.

**Build Scopes:**
- `core` - interfaces, utils, test-utils, history-client, hub-client
- `skills` - baseskill, chitchat-skill, example-skill, report-skill
- `services` - hub, lasso, parser, history, hub-client-cli
- `all` - All packages

**Parallelism:**
```bash
CONCURRENCY="$(get-lerna-concurrency.sh)"
PARALLEL="--parallel --concurrency=$CONCURRENCY"
```

**Usage:**
```bash
./scripts/quickbuild.sh [scope] [nodocker]
```

**Examples:**
```bash
./scripts/quickbuild.sh all
./scripts/quickbuild.sh core
./scripts/quickbuild.sh skills nodocker
```

### AWS ECS (Production)

**Architecture:**

**ECS (Elastic Container Service):**
- Container orchestration for production
- Task definitions for each service
- Service auto-scaling based on load
- Load balancer for traffic distribution

**ECR (Elastic Container Registry):**
- Docker image storage
- Versioned image tags
- CI/CD pipeline integration

**Application Load Balancer:**
- TLS termination
- Health checks
- Route to ECS tasks
- Sticky sessions if needed

**MongoDB Atlas:**
- Managed MongoDB service
- Automatic backups
- Global distribution
- High availability

**ElastiCache (Redis):**
- Managed Redis service
- Cluster mode for scaling
- Automatic failover
- Persistence options

**CloudWatch:**
- Log aggregation
- Metrics collection
- Alarm configuration
- Dashboard creation

**Deployment Pipeline:**
1. Code pushed to Git
2. CI builds Docker image
3. Image pushed to ECR
4. ECS task definition updated
5. New tasks deployed
6. Load balancer health checks pass
7. Old tasks terminated

**Environment Variables (Production):**
- `ETCO_server_hubTokenSecret` - JWT secret (from Secrets Manager)
- `ETCO_hub_skillsConfig` - S3 URL for skill config
- `ETCO_hub_recordSpeechLogBucket` - S3 bucket for speech logs
- `NET_parser` - Parser service URL
- `NET_history` - History service URL
- `ETCO_parser_dialogflow_key` - Dialogflow API key
- `ETCO_history_skillLaunch_mongo` - MongoDB connection string
- `ETCO_history_speechHistory_mongo` - MongoDB connection string

## Data Flow Examples

### Example 1: User Says "Tell Me a Joke"

**Step 1: Robot Initiates Listen**

**Robot → Hub (WebSocket):**
```typescript
{
  type: "LISTEN",
  msgID: "uuid-1",
  ts: 1234567890,
  data: {
    mode: "default",
    lang: "en-US",
    hotphrase: true,
    rules: ["launch"],
    asr: {
      sosTimeout: 5000,
      maxSpeechTimeout: 60000,
      hints: ["tell me a joke", "say something funny"]
    }
  }
}
```

**Step 2: Audio Streaming**

**Robot → Hub (WebSocket):**
- Binary audio packets streamed
- Hub buffers in AudioBuffer
- Hub streams to Google Cloud Speech API

**Step 3: Speech Detected**

**Hub → Robot (WebSocket):**
```typescript
{
  type: "SOS",
  msgID: "uuid-2",
  ts: 1234567895,
  data: null,
  timings: { total: 5000 }
}
```

**Step 4: Context Sent**

**Robot → Hub (WebSocket):**
```typescript
{
  type: "CONTEXT",
  msgID: "uuid-3",
  ts: 1234567896,
  data: {
    general: {
      accountID: "account-123",
      robotID: "jibo-001",
      lang: "en-US",
      release: "1.2.3"
    },
    runtime: {
      character: { emotion: { name: "happy" } },
      location: { city: "Boston" },
      loop: { users: [], jibo: { id: "jibo-001" } },
      perception: { speaker: null, peoplePresent: [] },
      dialog: {}
    },
    skill: {}
  }
}
```

**Step 5: Speech Ended**

**Hub → Robot (WebSocket):**
```typescript
{
  type: "EOS",
  msgID: "uuid-4",
  ts: 1234567920,
  data: null,
  timings: { total: 25000 }
}
```

**Step 6: ASR Complete**

**Hub internal:**
- Google Cloud Speech API returns: "tell me a joke"
- ASR result: `{ text: "tell me a joke", confidence: 0.95 }`

**Step 7: NLU Processing**

**Hub → Parser (HTTP):**
```typescript
POST http://parser:8080/v1/parse
{
  text: "tell me a joke",
  rules: ["launch"],
  external: [],
  loop: { users: [] }
}
```

**Parser → Hub (HTTP):**
```typescript
{
  intent: "joke_tell",
  entities: {},
  rules: ["launch"]
}
```

**Step 8: Intent Routing**

**Hub internal:**
- IntentRouter matches "joke_tell" to "joke-skill"
- DecisionMediator confirms no external factors
- Selected skill: "joke-skill" (cloud skill)

**Step 9: Listen Result**

**Hub → Robot (WebSocket):**
```typescript
{
  type: "LISTEN",
  msgID: "uuid-5",
  ts: 1234567930,
  data: {
    asr: { text: "tell me a joke", confidence: 0.95 },
    nlu: { intent: "joke_tell", entities: {}, rules: ["launch"] },
    match: { skillID: "joke-skill", launch: true, onRobot: false }
  },
  final: false,
  timings: { total: 40000, asr: 25000, nlu: 10000 }
}
```

**Step 10: Skill Launch**

**Hub → Joke Skill (HTTP):**
```typescript
POST http://joke-skill:8080/
Authorization: Bearer <jwt_token>
x-jibo-transid: uuid-1
x-jibo-robotid: jibo-001
{
  type: "LISTEN_LAUNCH",
  msgID: "uuid-6",
  ts: 1234567930,
  data: {
    general: { accountID: "account-123", robotID: "jibo-001", lang: "en-US", release: "1.2.3" },
    runtime: { character, location, loop, perception, dialog },
    skill: { id: "joke-skill" },
    nlu: { intent: "joke_tell", entities: {}, rules: ["launch"] },
    asr: { text: "tell me a joke", confidence: 0.95 }
  }
}
```

**Step 11: Skill Response**

**Joke Skill → Hub (HTTP):**
```typescript
{
  type: "SKILL_ACTION",
  msgID: "uuid-7",
  ts: 1234567935,
  data: {
    action: {
      type: "JCP",
      config: {
        version: "1.0.0",
        jcp: SayTextBehavior("Why did the chicken cross the road? To get to the other side!")
      }
    },
    analytics: { "joke-skill": [{ event: "JokeSelected", properties: { category: "classic" } }] },
    final: false,
    fireAndForget: false
  }
}
```

**Step 12: Skill Action**

**Hub → Robot (WebSocket):**
```typescript
{
  type: "SKILL_ACTION",
  msgID: "uuid-8",
  ts: 1234567935,
  data: {
    action: {
      type: "JCP",
      config: {
        version: "1.0.0",
        jcp: SayTextBehavior("Why did the chicken cross the road? To get to the other side!")
      }
    },
    analytics: { "joke-skill": [...] },
    final: false,
    fireAndForget: false
  },
  timings: { total: 5000, skill: 5000 }
}
```

**Step 13: Robot Executes**

**Robot internal:**
- Executes SayText behavior
- Speaks the joke
- Sends result back

**Robot → Hub (WebSocket):**
```typescript
{
  type: "CMD_RESULT",
  msgID: "uuid-9",
  ts: 1234567950,
  data: {
    result: { success: true, duration: 3000 }
  }
}
```

**Step 14: Skill Update**

**Hub → Joke Skill (HTTP):**
```typescript
POST http://joke-skill:8080/
{
  type: "LISTEN_UPDATE",
  msgID: "uuid-10",
  ts: 1234567950,
  data: {
    general: { ... },
    runtime: { ... },
    skill: { id: "joke-skill", session: { id: "session-1", nodeID: 2, data: {}, trace: [...] } },
    result: { success: true, duration: 3000 }
  }
}
```

**Step 15: Final Response**

**Joke Skill → Hub (HTTP):**
```typescript
{
  type: "SKILL_ACTION",
  msgID: "uuid-11",
  ts: 1234567952,
  data: {
    action: null,
    analytics: { "joke-skill": [...] },
    final: true,
    fireAndForget: true
  }
}
```

**Hub → Robot (WebSocket):**
```typescript
{
  type: "SKILL_ACTION",
  msgID: "uuid-12",
  ts: 1234567952,
  data: {
    action: null,
    analytics: { "joke-skill": [...] },
    final: true,
    fireAndForget: true
  },
  timings: { total: 22000, skill: 17000 }
}
```

**Transaction Complete**

### Example 2: Proactive Greeting

**Step 1: Robot Detects Person**

**Robot internal:**
- Vision system detects person entering room
- Triggers proactive event

**Step 2: Proactive Trigger**

**Robot → Hub (WebSocket):**
```typescript
{
  type: "TRIGGER",
  msgID: "uuid-1",
  ts: 1234567890,
  data: {
    triggerData: {
      triggerType: "person_entered",
      looperID: "user-123"
    },
    triggerSource: "SURPRISE"
  }
}
```

**Step 3: Context Sent**

**Robot → Hub (WebSocket):**
```typescript
{
  type: "CONTEXT",
  msgID: "uuid-2",
  ts: 1234567891,
  data: {
    general: { accountID: "account-123", robotID: "jibo-001", lang: "en-US", release: "1.2.3" },
    runtime: {
      character: { emotion: { name: "happy" } },
      location: { city: "Boston" },
      loop: { users: [{ id: "user-123", firstName: "John", lastName: "Doe" }], jibo: { id: "jibo-001" } },
      perception: { speaker: "user-123", peoplePresent: [{ id: "user-123", type: "IDENTIFIED" }] },
      dialog: {}
    },
    skill: {}
  }
}
```

**Step 4: Proactive Selection**

**Hub internal:**
- Get all proactive skill configurations
- Filter by context (time, location, people present)
- Filter by history (last greeting time > 1 hour ago)
- Filter by settings (user has greetings enabled)
- Randomly select "greeting-skill"

**Step 5: Proactive Match**

**Hub → Robot (WebSocket):**
```typescript
{
  type: "PROACTIVE",
  msgID: "uuid-3",
  ts: 1234567910,
  data: {
    match: {
      skillID: "greeting-skill",
      onRobot: false,
      isProactive: true,
      launch: true,
      skipSurprises: true
    }
  },
  final: false
}
```

**Step 6: Skill Launch**

**Hub → Greeting Skill (HTTP):**
```typescript
POST http://greeting-skill:8080/
{
  type: "PROACTIVE_LAUNCH",
  msgID: "uuid-4",
  ts: 1234567910,
  data: {
    general: { accountID: "account-123", robotID: "jibo-001", lang: "en-US", release: "1.2.3" },
    runtime: { character, location, loop, perception, dialog },
    skill: { id: "greeting-skill" },
    memo: { triggerType: "person_entered", looperID: "user-123" }
  }
}
```

**Step 7: Skill Response**

**Greeting Skill → Hub (HTTP):**
```typescript
{
  type: "SKILL_ACTION",
  msgID: "uuid-5",
  ts: 1234567915,
  data: {
    action: {
      type: "JCP",
      config: {
        version: "1.0.0",
        jcp: Sequence([
          LookAtBehavior("user-123"),
          SayTextBehavior("Hello John! Good to see you.")
        ])
      }
    },
    analytics: { "greeting-skill": [{ event: "Greeting", properties: { person: "user-123" } }] },
    final: true,
    fireAndForget: false
  }
}
```

**Step 8: Skill Action**

**Hub → Robot (WebSocket):**
```typescript
{
  type: "SKILL_ACTION",
  msgID: "uuid-6",
  ts: 1234567915,
  data: {
    action: {
      type: "JCP",
      config: {
        version: "1.0.0",
        jcp: Sequence([
          LookAtBehavior("user-123"),
          SayTextBehavior("Hello John! Good to see you.")
        ])
      }
    },
    analytics: { "greeting-skill": [...] },
    final: true,
    fireAndForget: false
  },
  timings: { total: 5000, skill: 5000 }
}
```

**Transaction Complete**

## Error Handling and Timeouts

### Timeout Configuration

**Listen Transaction Timeouts:**
```typescript
TIMEOUT_ASR = 40 * 1000;           // 40 seconds
TIMEOUT_PARSER = 10 * 1000;        // 10 seconds
TIMEOUT_CONTEXT = 5 * 1000;        // 5 seconds
TIMEOUT_SKILL = 10 * 1000;         // 10 seconds
DEFAULT_TRANSACTION_TIME = 60 * 1000;  // 60 seconds
```

**WebSocket Timeouts:**
```typescript
TIMEOUT_MAX_DURATION = 3 * 60 * 1000;        // 3 minutes
TIMEOUT_CLOSE_AFTER_FINAL = 2 * 1000;       // 2 seconds
```

**ASR Timeouts:**
```typescript
sosTimeout: number          // Time to wait for speech start (configurable)
maxSpeechTimeout: number    // Maximum speech duration (default 60 seconds)
```

### Error Types

**Hub Error Codes:**
- `TIMEOUT_ASR` - ASR timeout (40 seconds)
- `TIMEOUT_PARSER` - Parser timeout (10 seconds)
- `TIMEOUT_CONTEXT` - Context timeout (5 seconds)
- `TIMEOUT_SKILL` - Skill timeout (10 seconds)
- `PARSER` - Parser error
- `ASR` - ASR error

**Skill Request Errors:**
- `SKILL_NOT_FOUND` - Skill does not exist or is on-robot
- `TIMEOUT` - Skill request timeout

### Error Response Format

**Standard Error Response:**
```typescript
{
  type: "ERROR",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    message: "Error description",
    code?: "ERROR_CODE"
  },
  final: true,
  timings: {
    total: number
  }
}
```

### Error Handling Flow

**WebSocket Errors:**
1. Error occurs in handler
2. Handler catches error
3. If `error.hasBeenHandled`, log and continue
4. Otherwise, send ERROR message to robot
5. Close WebSocket connection

**HTTP Errors:**
1. Error occurs in handler
2. Express error middleware catches
3. Returns 500 status with ERROR JSON
4. Logs error with details

**Skill Errors:**
1. Skill throws error
2. BaseSkill catches in POST handler
3. Calls `buildErrorResponse()`
4. Returns ERROR response to Hub
5. Hub forwards to robot

### Timeout Handling

**ASR Timeout:**
- If SOS timeout reached: Returns empty text with SOS_TIMEOUT annotation
- If max speech timeout reached: Returns last incremental with MAX_SPEECH_TIMEOUT annotation
- Hub skips NLU and returns no match

**Parser Timeout:**
- Hub waits 10 seconds for Parser response
- If timeout: Throws HubError with TIMEOUT_PARSER code
- Hub returns ERROR to robot

**Context Timeout:**
- Hub waits 5 seconds for CONTEXT message
- If timeout: Throws HubError with TIMEOUT_CONTEXT code
- Hub returns ERROR to robot

**Skill Timeout:**
- Hub waits 10 seconds for Skill response
- If timeout: Throws HubError with TIMEOUT_SKILL code
- Hub returns ERROR to robot

**Transaction Timeout:**
- Overall transaction timeout of 60 seconds
- If exceeded: Transaction rejected
- WebSocket closed

## Monitoring and Logging

### New Relic Integration

**Location:** `packages/utils/src/service/NewRelic.ts`

**Purpose:** Application performance monitoring and error tracking.

**Initialization:**
- New Relic agent loaded via `@jibo/utils/common/init-newrelic.js`
- Global flag `_newRelicLoaded` set when loaded
- Lazy loading of newrelic module

**Web Transaction Wrapping:**
```typescript
NewRelic.wrapWebTransaction<T>(name: string, handler: PromiseFunction<T>): Promise<T>
```

**Usage:**
```typescript
NewRelic.wrapWebTransaction<void>(`ws:${req.url}`, () => handler.handler.handleSocket(ws))
```

**Error Tracking:**
```typescript
NewRelic.newrelic.noticeError(error, error.nrAttributes);
```

**Custom Attributes:**
- `transID` - Transaction ID
- `robotID` - Robot ID

**Transaction Names:**
- WebSocket: `ws:/listen`, `ws:/proactive`
- HTTP: Based on endpoint path

### jibo-log Integration

**Location:** `packages/utils/src/logging/`

**Purpose:** Structured logging with namespace support and dynamic configuration.

**Log Instance Creation:**
```typescript
req.log = new Log(this.logNamespace);
req.log.transID = req.jibo.transID;
req.log.robotID = req.jibo.robotID;
req.log.outputPerNamespace = parseLoggingConfigHeader(req.jibo.loggingConfig);
```

**Log Levels:**
- `debug`
- `info`
- `warn`
- `error`

**Dynamic Configuration:**
- Per-namespace log levels via `x-jibo-logging-config` header
- Format: `{ "Hub": "debug", "Parser": "info" }`
- Converted to `{ "Hub": { pegasus: "debug" }, "Parser": { pegasus: "info" } }`

**Log Methods:**
```typescript
log.debug(message, ...args)
log.info(message, ...args)
log.warn(message, ...args)
log.error(message, ...args)
```

**Child Loggers:**
```typescript
const childLog = parentLog.createChild('ChildName');
```

**Structured Logs:**
- JSON format when `ETCO_server_structuredLogs=true`
- Plain text when false

### CloudWatch Integration

**Log Aggregation:**
- All service logs sent to CloudWatch Logs
- Log groups per service
- Log streams per container instance

**Metrics:**
- Custom metrics via CloudWatch Metrics
- HTTP request counts and latencies
- WebSocket connection counts
- Error rates

**Alarms:**
- High error rate
- High latency
- Low connection count
- Service health check failures

### Health Checks

**Endpoint:** `/healthcheck`

**Method:** GET

**Response:**
```
200 OK
"ok"
```

**Custom Health Checks:**
Services can override `getHealthcheckResponse()` to return custom health data:

```typescript
protected async getHealthcheckResponse(): Promise<HttpResponse> {
  // Check database connections
  // Check external service availability
  return { statusCode: 200, body: 'ok' };
}
```

## Skill Development Guide

### Creating a Simple Skill

**Step 1: Create Package**

```bash
cd packages
mkdir my-skill
cd my-skill
yarn init
```

**Step 2: Add Dependencies**

```json
{
  "dependencies": {
    "@jibo/baseskill": "^1.0.0",
    "@jibo/interfaces": "^1.0.0",
    "@jibo/utils": "^1.0.0"
  }
}
```

**Step 3: Create Skill Class**

```typescript
import { BaseSkill } from '@jibo/baseskill';
import { skill } from '@jibo/interfaces';
import { generateJCPAction } from '@jibo/baseskill/src/graph/Utils';

export class MySkill extends BaseSkill {
  constructor() {
    super('my-skill');
  }

  protected async handle(req: PegasusRequest<SkillRequest>): Promise<SkillResponse> {
    const data = req.body.data;
    const text = data.nlu.entities.text || "Hello!";
    
    const action = generateJCPAction(SayTextBehavior(text));
    
    return {
      type: skill.response.ResponseType.SKILL_ACTION,
      data: {
        action: action,
        final: true,
        fireAndForget: true
      },
      ts: Date.now(),
      msgID: getUUID()
    };
  }
}
```

**Step 4: Create Service Entry Point**

```typescript
import { SkillService } from '@jibo/baseskill';
import { MySkill } from './MySkill';

const skill = new MySkill();
const service = new SkillService(skill);

service.init(8080).catch(err => {
  console.error(err);
  process.exit(1);
});
```

**Step 5: Build and Run**

```bash
yarn build
yarn start
```

### Creating a Graph Skill

**Step 1: Define Transitions**

```typescript
enum Transition {
  Done = 'Done',
  Retry = 'Retry'
}
```

**Step 2: Create Custom Nodes**

```typescript
import { Node, Data, EnterResponse, ExitResponse } from '@jibo/baseskill';

class StartNode extends Node<Transition> {
  constructor() {
    super('Start', [Transition.Done]);
  }

  async enter(data: Data): Promise<EnterResponse> {
    const action = generateJCPAction(SayTextBehavior("Hello!"));
    return { action };
  }

  async exit(data: Data): Promise<ExitResponse> {
    return { transition: Transition.Done };
  }
}
```

**Step 3: Create Graph Skill**

```typescript
import { GraphSkill, graph } from '@jibo/baseskill';

export class MyGraphSkill extends GraphSkill<Transition> {
  constructor() {
    super('my-graph-skill');
  }

  createGraph(): graph.Graph<Transition> {
    const g = new graph.Graph('My Skill', graph.utils.generateTransitions(Transition));
    
    const startNode = new StartNode();
    const endNode = new graph.nodes.dn.DefaultNode('End');
    
    g.addNode(startNode, [[Transition.Done, endNode]]);
    g.addNode(endNode, [[graph.nodes.dn.Transition.Done, Transition.Done]]);
    
    g.finalize();
    return g;
  }
}
```

### Skill Configuration

**Create Manifest:**

```json
{
  "id": "my-skill",
  "intents": [
    {
      "name": "my_intent",
      "entities": [],
      "memo": null
    }
  ],
  "proactives": [],
  "onRobot": false,
  "settings": {}
}
```

**Register with Hub:**

Add to `skills-local.json` or environment configuration:

```json
{
  "my-skill": {
    "id": "my-skill",
    "URL": "http://my-skill:8080",
    "intents": [...],
    "onRobot": false
  }
}
```

### Skill Best Practices

**Error Handling:**
```typescript
try {
  // Skill logic
} catch (error) {
  this.track(data, 'Error', { error: error.message });
  throw error;
}
```

**Analytics Tracking:**
```typescript
this.track(data, 'CustomEvent', { key: value });
```

**Supplemental Behaviors:**
```typescript
this.addParallelBehavior(data, SetPresentPersonBehavior);
this.addSequenceBehavior(data, LookAtBehavior);
```

**Speaker Override:**
```typescript
this.overrideSpeaker(data, userId);
```

**Session Data:**
```typescript
// Store data in session
data.skill.session.data.myKey = myValue;

// Retrieve data
const myValue = data.skill.session.data.myKey;
```

**Local Data:**
```typescript
// Store temporary data
data.local.myTemp = tempValue;
```

### Testing Skills

**Unit Tests:**
```typescript
import { MySkill } from './MySkill';

describe('MySkill', () => {
  it('should return action', async () => {
    const skill = new MySkill();
    const req = createMockRequest();
    const response = await skill.handle(req);
    expect(response.type).toBe('SKILL_ACTION');
  });
});
```

**Integration Tests:**
```typescript
import axios from 'axios';

describe('MySkill Integration', () => {
  it('should handle launch request', async () => {
    const response = await axios.post('http://localhost:8080/', {
      type: 'LISTEN_LAUNCH',
      data: { ... }
    });
    expect(response.data.type).toBe('SKILL_ACTION');
  });
});
```

### Debugging

**Node Debugging:**
```bash
# Start with debug port
node --inspect=5850 dist/index.js

# Connect with Chrome DevTools
# chrome://inspect
```

**Docker Debugging:**
```yaml
ports:
  - 5850:5850  # Debug port
```

**Logging:**
```typescript
req.log.debug('Debug message');
req.log.info('Info message');
req.log.warn('Warning message');
req.log.error('Error message');
```

**Graph Visualization:**
```typescript
g.writeDotFile('/tmp/my-graph.dot');
# Convert to PNG
dot -Tpng /tmp/my-graph.dot -o /tmp/my-graph.png
```

### Deployment

**Dockerfile:**
```dockerfile
FROM pegasus_base:latest
WORKDIR /pegasus/packages/my-skill
COPY package.json yarn.lock ./
RUN yarn install
COPY . .
RUN yarn build
CMD ["yarn", "start"]
```

**Docker Compose:**
```yaml
my-skill:
  image: my-skill:latest
  ports:
    - 9008:8080
  environment:
    - ETCO_server_logLevel=debug
```

**ECS Task Definition:**
```json
{
  "containerDefinitions": [
    {
      "name": "my-skill",
      "image": "my-ecr-repo/my-skill:latest",
      "portMappings": [{ "containerPort": 8080 }],
      "environment": [...]
    }
  ]
}
```
