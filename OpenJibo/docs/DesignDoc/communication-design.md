# Communication Design Document

## Overview

The Jibo cloud system uses two primary communication protocols: WebSocket for real-time bidirectional communication between the robot and cloud services, and HTTP for service-to-service communication (Hub to skills, Hub to parser, etc.). All communication is secured using JWT (JSON Web Token) authentication with Bearer tokens.

## Location

- WebSocket implementation: `packages/utils/src/service/BaseService.ts`
- HTTP implementation: `packages/utils/src/service/BaseService.ts`
- Authentication: `packages/utils/src/service/BaseService.ts`
- Headers: `packages/utils/src/service/JiboHeaders.ts`

## WebSocket Protocol

### Connection Establishment

**WebSocket Server Setup:**

The WebSocket server is created within `BaseService.init()`:

```typescript
this.wsServer = new WebSocket.Server({ 
  server: this.server,
  verifyClient: (info, callback) => {
    // Authentication verification
    // Handler existence check
    callback(true, 200, '');
  }
});
```

**Connection Flow:**

1. Robot initiates WebSocket connection to Hub
2. Hub's `verifyClient` callback is invoked before connection is accepted
3. Hub verifies JWT token in Authorization header
4. Hub checks if a handler exists for the requested URL
5. If both checks pass, connection is accepted
6. Hub creates `PegasusWebSocket` instance with enhanced properties
7. Hub calls handler's `handleSocket()` method

### WebSocket URL Format

**Listen Endpoint:**
```
ws://hub:9000/listen
ws://hub:9000/v1/listen
```

**Proactive Endpoint:**
```
ws://hub:9000/proactive
ws://hub:9000/v1/proactive
```

### Authentication

**JWT Token Format:**

The robot sends a Bearer token in the Authorization header:

```
Authorization: Bearer <jwt_token>
```

**Token Payload:**
```typescript
{
  id: string,           // Account ID
  accessKeyId: string,  // Client ID
  secretAccessKey: string,  // Client Secret
  friendlyId?: string  // Robot name
}
```

**Verification Process:**

```typescript
checkAuthentication(headers: any): { error?: string, auth?: IAuthDetails }
```

1. Check for Authorization header
2. Validate Bearer scheme
3. Extract token
4. Verify token using `jsonwebtoken.verify()`
5. Use secret from `ETCO_server_hubTokenSecret` environment variable
6. Return auth details or error

**Error Cases:**
- Missing Authorization header → "Authorization is required"
- Invalid scheme → "Only bearer scheme is supported"
- Missing secret → "No JWT secret set"
- Invalid token → JWT verification error (e.g., "JsonWebTokenError: invalid signature")

**Authentication Storage:**

After verification, auth details are stored on the WebSocket instance:

```typescript
ws.auth = {
  id: string,
  accessKeyId: string,
  secretAccessKey: string,
  friendlyId?: string
}
```

### Jibo Headers

**Location:** `packages/utils/src/service/JiboHeaders.ts`

**Purpose:** Transmit trace information across services for logging and debugging.

**Header Names:**
```typescript
Headers = {
  transID: "x-jibo-transid",
  robotID: "x-jibo-robotid",
  loggingConfig: "x-jibo-logging-config"
}
```

**JiboHeaders Class:**

```typescript
class JiboHeaders {
  transID: string;
  robotID?: string;
  loggingConfig?: string;
}
```

**Parsing:**
```typescript
ws.jibo = new JiboHeaders(req.headers);
// transID defaults to 'unknown'
// robotID defaults to 'unknown'
// loggingConfig defaults to '{}'
```

**Logging Configuration:**

The logging config header allows dynamic log level configuration per namespace:

```json
{
  "Hub": "debug",
  "Parser": "info",
  "Skill": "warn"
}
```

**Format Conversion:**
The framework converts from `{[namespace]: LogLevel}` to `{[namespace]: {pegasus: LogLevel}}` for compatibility with jibo-log.

### PegasusWebSocket

**Location:** `packages/utils/src/service/PegasusWebSocket.ts`

**Purpose:** Enhanced WebSocket class with Jibo-specific properties.

**Properties:**
```typescript
class PegasusWebSocket extends WebSocket {
  jibo: JiboHeaders;           // Parsed Jibo headers
  auth?: IAuthDetails;        // JWT auth details
  remoteAddress?: string;      // Client IP address
  log?: Log;                   // Logger instance
}
```

**Remote Address Detection:**
1. Check `x-forwarded-for` header (from load balancer)
2. Fall back to `connection.remoteAddress`
3. Log warning if neither available

### ResponseWrapper

**Location:** `packages/utils/src/service/handlers/BaseWebsocketHandler.ts`

**Purpose:** Manages WebSocket response lifecycle with timeout enforcement.

**Timeouts:**
- `TIMEOUT_MAX_DURATION` = 3 minutes - Maximum connection duration
- `TIMEOUT_CLOSE_AFTER_FINAL` = 2 seconds - Close after final message

**Methods:**

**write(data):**
- Writes message to WebSocket
- Adds timing if not present
- If `final=true`, marks response as ended
- Closes socket after 2 seconds if final

**writeFinal(data):**
- Sets `final=true` and calls `write()`

**error(error, errorData):**
- Writes ERROR message
- Sets `final=true`

**Lifecycle:**
1. Created when handler starts
2. Max duration timer starts (3 minutes)
3. Messages written via `write()` or `writeFinal()`
4. If final message sent, close timer starts (2 seconds)
5. Socket close triggers cleanup
6. Promise resolves when response ends

### Message Format

**Base Message Structure:**

```typescript
{
  type: string,           // Message type
  msgID: string,          // Unique message ID (UUID)
  ts: number,             // Timestamp (milliseconds since epoch)
  data: any,              // Message-specific data
  final?: boolean,        // Is this the final message?
  timings?: {             // Timing information
    total: number,
    [key: string]: number
  }
}
```

**Message Serialization:**

All messages are serialized to JSON before sending:

```typescript
socket.send(JSON.stringify(data));
```

### Server-to-Robot Messages (WebSocket)

The following messages are sent from the Hub (server) to the robot:

#### SOS (Start of Speech)

**Emitted when:** Speech is detected during ASR

**Purpose:** Notify robot that speech has started

**Format:**
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

#### EOS (End of Speech)

**Emitted when:** Speech ends during ASR

**Purpose:** Notify robot that speech has ended

**Format:**
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

#### LISTEN Response

**Emitted when:** ASR and NLU processing complete

**Purpose:** Send ASR result, NLU result, and skill match to robot

**Format:**
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

**Final Flag:**
- `final: true` - No skill matched or on-robot skill, transaction complete
- `final: false` - Cloud skill matched, more messages coming

#### SKILL_ACTION

**Emitted when:** Cloud skill returns an action to execute

**Purpose:** Send JCP behavior for robot to execute

**Format:**
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
    analytics: AnalyticsData,
    final: boolean,
    fireAndForget: boolean
  },
  timings: {
    total: number,
    skill: number
  }
}
```

**Final Flag:**
- `final: false` - Robot should execute and send CMD_RESULT back
- `final: true` - Transaction complete, no more actions expected

**FireAndForget:**
- `true` - Robot executes but doesn't send result back
- `false` - Robot executes and sends result back

#### SKILL_REDIRECT

**Emitted when:** Skill redirects to another skill

**Purpose:** Notify robot of skill redirection

**Format:**
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

**Final Flag:**
- `final: true` - On-robot skill, robot handles it
- `final: false` - Cloud skill, Hub will send actions

#### PROACTIVE Response

**Emitted when:** Proactive action selected

**Purpose:** Notify robot of proactive skill launch

**Format:**
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
  } | {},
  final: boolean
}
```

**Data:**
- With match data - Action selected
- Empty data - No action selected

#### ERROR

**Emitted when:** An error occurs during transaction

**Purpose:** Notify robot of error

**Format:**
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

### Robot-to-Server Messages (WebSocket)

The following messages are sent from the robot to the Hub:

#### LISTEN

**Purpose:** Initiate listen transaction

**Format:**
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

#### Audio Packets

**Purpose:** Stream audio data for ASR

**Format:** Binary Buffer (not JSON)

#### CONTEXT

**Purpose:** Send runtime context from robot

**Format:**
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

#### CLIENT_ASR

**Purpose:** Provide ASR result (for menu clicks, etc.)

**Format:**
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

#### CLIENT_NLU

**Purpose:** Provide NLU result (for menu clicks, etc.)

**Format:**
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

#### TRIGGER

**Purpose:** Initiate proactive selection

**Format:**
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

## HTTP Protocol

### HTTP Server Setup

**Express.js Application:**

```typescript
this.app = express();
this.app.use(bodyParser.urlencoded({ extended: true }));
this.app.use(bodyParser.json());
```

**HTTP Server Creation:**

```typescript
this.server = http.createServer(this.app);
this.server.listen(port, callback);
```

### HTTP Authentication

**Middleware:**

```typescript
checkRequestAuthentication(req, res, next)
```

**Process:**
1. Check Authorization header
2. Verify JWT token
3. If valid, call `next()`
4. If invalid, return 401 error

**Protected Endpoints:**

Endpoints with `authenticationRequired: true` are protected:

```typescript
this.addHttpHandler('/path', {
  handler: myHandler,
  authenticationRequired: true
});
```

### HTTP Headers

**Jibo Headers (HTTP):**

Same as WebSocket headers:
- `x-jibo-transid` - Transaction ID
- `x-jibo-robotid` - Robot ID
- `x-jibo-logging-config` - Log level configuration

**Authorization Header:**
```
Authorization: Bearer <jwt_token>
```

### Service-to-Service HTTP Requests

#### Hub to Skill

**Purpose:** Send skill launch/update requests

**Method:** POST

**URL:** `http://skill-host:port/` or `http://skill-host:port/v1/main`

**Headers:**
```
Authorization: Bearer <jwt_token>
x-jibo-transid: <uuid>
x-jibo-robotid: <robot-id>
Content-Type: application/json
```

**Request Body:**
```typescript
{
  type: "LISTEN_LAUNCH" | "LISTEN_UPDATE" | "PROACTIVE_LAUNCH",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    general: { accountID, robotID, lang, release },
    runtime: { character, location, loop, perception, dialog },
    skill: { id, session? },
    result?: any,
    nlu?: NLUResult,
    asr?: ASRResult,
    memo?: any
  }
}
```

**Response Body:**
```typescript
{
  type: "SKILL_ACTION" | "SKILL_REDIRECT" | "ERROR",
  msgID: "uuid",
  ts: 1234567890,
  data: { ... },
  final?: boolean,
  timings?: { total: number, skill: number }
}
```

**Timeout:** 10 seconds (configurable)

#### Hub to Parser

**Purpose:** Send NLU request

**Method:** POST

**URL:** `http://parser:8080/v1/parse`

**Headers:**
```
x-jibo-transid: <uuid>
x-jibo-robotid: <robot-id>
Content-Type: application/json
```

**Request Body:**
```typescript
{
  text: string,
  rules: string[],
  external: ExternalAgentRequest[],
  loop: {
    users: [{ firstName, lastName, id }]
  }
}
```

**Response Body:**
```typescript
{
  intent: string,
  entities: {},
  rules: []
}
```

**Timeout:** 10 seconds

#### Hub to History

**Purpose:** Record skill launches or speech history

**Method:** POST

**URL:** 
- `http://history:8080/v1/skill/launch` - Skill launch history
- `http://history:8080/v1/speech` - Speech history

**Headers:**
```
x-jibo-transid: <uuid>
x-jibo-robotid: <robot-id>
Content-Type: application/json
```

**Request Body (Skill Launch):**
```typescript
{
  robotID: string,
  sessionID: string,
  skillID: string,
  intent: string,
  personIDs: string[]
}
```

**Request Body (Speech History):**
```typescript
{
  robotID: string,
  accountID: string,
  transID: string,
  timestamp: number,
  audioFileURL?: string,
  asr?: ASRResult,
  nlu?: NLUResult,
  match?: GlobalMatchResponseData,
  skill?: SkillRequestOutput,
  redirect?: RedirectData,
  error?: Error
}
```

### Health Check Endpoint

**URL:** `/healthcheck`

**Method:** GET

**Purpose:** Service health check

**Response:**
```
200 OK
```

**Body:** `"ok"` (default, can be overridden)

## JWT Authentication

### Token Generation

**Token is generated by the robot (or authentication service) and sent to cloud services.**

**Token Structure:**

```typescript
{
  id: string,           // Account ID
  accessKeyId: string,  // Client ID
  secretAccessKey: string,  // Client Secret
  friendlyId?: string  // Robot name (optional)
}
```

### Token Verification

**Verification Function:**

```typescript
jsonwebtoken.verify(token, secret)
```

**Secret Source:** `ETCO_server_hubTokenSecret` environment variable

**Verification Process:**
1. Decode JWT token
2. Verify signature using secret
3. Check expiration (if present in token)
4. Return decoded payload

### Authentication Flow

**WebSocket Connection:**
1. Robot connects with `Authorization: Bearer <token>`
2. Hub's `verifyClient` callback verifies token
3. If valid, connection accepted and auth stored on WebSocket
4. If invalid, connection rejected with 401

**HTTP Request:**
1. Robot sends request with `Authorization: Bearer <token>`
2. Express middleware verifies token
3. If valid, request proceeds to handler
4. If invalid, returns 401 error

### Authentication Bypass

**Development Mode:**

Services can disable authentication for development:

```typescript
this.disableAuth = true;
```

**When disabled:**
- WebSocket connections accepted without token verification
- HTTP requests proceed without authentication middleware
- Auth details may be missing from request objects

## Error Handling

### WebSocket Errors

**Connection Errors:**
- Authentication failure → 401, connection rejected
- No handler for URL → 404, connection rejected
- Network error → Connection closed

**Message Errors:**
- Invalid JSON → Logged, connection may close
- Missing required fields → Handler-specific error
- Timeout → Socket closed after max duration

**Error Message Format:**
```typescript
{
  type: "ERROR",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    message: string
  },
  final: true
}
```

### HTTP Errors

**Status Codes:**
- 200 - Success
- 401 - Unauthorized (invalid token)
- 404 - Not found (invalid URL)
- 500 - Internal server error

**Error Response Format:**
```typescript
{
  type: "ERROR",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    message: string
  },
  final: true
}
```

## Logging

### Log Instance Creation

**Per-Request Logging:**

Each request (HTTP or WebSocket) gets a dedicated log instance:

```typescript
req.log = new Log(this.logNamespace);
req.log.transID = req.jibo.transID;
req.log.robotID = req.jibo.robotID;
req.log.outputPerNamespace = parseLoggingConfigHeader(req.jibo.loggingConfig);
```

**WebSocket Logging:**

```typescript
ws.log = new Log(this.logNamespace);
ws.log.transID = ws.jibo.transID;
ws.log.robotID = ws.jibo.robotID;
ws.log.outputPerNamespace = parseLoggingConfigHeader(ws.jibo.loggingConfig);
```

### Log Level Configuration

**Dynamic Configuration:**

Log levels can be configured per namespace via the `x-jibo-logging-config` header:

```json
{
  "Hub": "debug",
  "Parser": "info",
  "Skill": "error"
}
```

**Supported Levels:**
- `debug`
- `info`
- `warn`
- `error`

## Monitoring

### New Relic Integration

**WebSocket Transactions:**

```typescript
NewRelic.wrapWebTransaction<void>(`ws:${req.url}`, () => handler.handler.handleSocket(ws))
```

**Error Tracking:**

Errors are tracked with custom attributes:
- `transID` - Transaction ID
- `robotID` - Robot ID

### Timing Information

**All messages include timing:**

```typescript
{
  timings: {
    total: number,      // Total time since start
    asr?: number,       // ASR processing time
    nlu?: number,       // NLU processing time
    skill?: number      // Skill processing time
  }
}
```

## Security Considerations

### TLS/SSL

**Current Implementation:**
- WebSocket connections from load balancer may not be secure
- TLS termination at load balancer
- Services behind load balancer communicate over internal network

**Future Considerations:**
- End-to-end encryption for sensitive data
- Certificate pinning for robot authentication

### Token Security

**Secret Management:**
- JWT secret stored in environment variable
- Secret should be rotated regularly
- Different secrets for different environments

**Token Expiration:**
- Tokens should include expiration (`exp` claim)
- Short-lived tokens recommended
- Refresh token mechanism for long-lived sessions

### IP Filtering

**Remote Address Tracking:**
- Client IP address logged for all connections
- Can be used for IP-based filtering
- Load balancer sets `x-forwarded-for` header

## Summary of Server-to-Robot Communication

### WebSocket Messages (Server → Robot)

1. **SOS** - Speech detected
2. **EOS** - Speech ended
3. **LISTEN** - ASR/NLU result with match
4. **SKILL_ACTION** - JCP behavior to execute
5. **SKILL_REDIRECT** - Skill redirect notification
6. **PROACTIVE** - Proactive match/no-action
7. **ERROR** - Error occurred

### HTTP Messages (Server → Robot)

HTTP is not used for direct server-to-robot communication. All server-to-robot communication happens over WebSocket.

### Key Design Principles

1. **Bidirectional** - WebSocket enables real-time bidirectional communication
2. **Binary Support** - WebSocket supports binary audio streaming
3. **Authentication** - JWT tokens secure all connections
4. **Traceability** - Transaction IDs and robot IDs in all messages
5. **Timeouts** - All operations have timeouts to prevent hanging
6. **Error Handling** - Standardized error format across all protocols
7. **Logging** - Per-request logging with dynamic configuration
8. **Monitoring** - New Relic integration for performance tracking
