# Skill Framework Design Document

## Overview

The Skill Framework provides the foundation for building cloud-based skills for the Jibo robot. It consists of a base class for all skills, a graph-based state machine for complex conversational flows, and a system for generating JCP (Jibo Command Protocol) actions that are sent to the robot.

## Location

`packages/baseskill/src/`

## Core Components

### BaseSkill (`BaseSkill.ts`)

Abstract base class that all cloud skills must extend.

**Purpose:** Provides common HTTP handling and error handling for all skills.

**Key Features:**
- Extends `BaseHttpHandler` from `@jibo/utils`
- Registers POST handler at `/` endpoint
- Validates request structure
- Tracks timing for each request
- Provides error response builder

**Constructor:**
```typescript
constructor(public name: string)
```

**Abstract Method:**
```typescript
protected abstract handle(request: PegasusRequest<SkillRequest>): Promise<SkillResponse>;
```

**Lifecycle Methods:**
- `init(): Promise<void>` - Override to initialize resources (load files, connect to services)
- `buildErrorResponse(err: Error): ErrorResponse` - Builds standardized error response

**HTTP Handler:**
- Accepts POST requests at `/`
- Logs request type
- Calls `handle()` method
- Adds timing information
- Catches errors and returns error response

### GraphSkill (`GraphSkill.ts`)

Extends BaseSkill with a graph-based state machine for complex conversational flows.

**Purpose:** Enables skills to define their logic as a series of interconnected nodes (states) with transitions.

**Key Features:**
- Implements `GraphFactory` interface
- Manages graph execution via `GraphManager` singleton
- Supports skill redirects
- Tracks analytics events
- Supports supplemental behaviors (parallel/sequence)
- Handles both launch and update requests

**Constructor:**
```typescript
constructor(name: string)
```

**Abstract Method:**
```typescript
abstract createGraph(): Graph<ExitTransition>
```

**Request Handling:**

**Launch Requests** (LISTEN_LAUNCH or PROACTIVE_LAUNCH):
1. Validates request data (accountID, robotID, skill ID)
2. Initializes skill session data
3. Tracks SKILL_ENTRY analytics event
4. Calls `GraphManager.instance.start(graph, data)` to begin graph execution
5. Returns SKILL_ACTION or SKILL_REDIRECT response

**Update Requests** (LISTEN_UPDATE):
1. Validates request data
2. Calls `GraphManager.instance.exitNode(data)` to process action results
3. Returns next SKILL_ACTION or final response

**Response Types:**

1. **SKILL_REDIRECT** - Redirects to another skill
   ```typescript
   {
     type: "SKILL_REDIRECT",
     msgID: "uuid",
     ts: 1234567890,
     data: {
       skillID: string,
       nlu?: NLUResult,
       asr?: ASRResult,
       memo?: any
     }
   }
   ```

2. **SKILL_ACTION** - Returns JCP action for robot to execute
   ```typescript
   {
     type: "SKILL_ACTION",
     msgID: "uuid",
     ts: 1234567890,
     data: {
       action: JCPAction,
       analytics: AnalyticsData,
       final: boolean,
       fireAndForget: boolean
     }
   }
   ```

3. **Final Response** - No action, transaction complete
   ```typescript
   {
     type: "SKILL_ACTION",
     msgID: "uuid",
     ts: 1234567890,
     data: {
       action: null,
       analytics: AnalyticsData,
       final: true,
       fireAndForget: true
     }
   }
   ```

**Convenience Methods:**

- `track(data, event, properties)` - Track analytics event
- `overrideSpeaker(data, id)` - Override current speaker in context
- `addParallelBehavior(data, behavior)` - Add behavior to execute in parallel
- `addSequenceBehavior(data, behavior)` - Add behavior to execute in sequence

**Supplemental Behaviors Injection:**

When a skill returns a JCP action, the framework injects any supplemental behaviors that were added during execution:

1. If sequence behaviors exist, wraps main action in a Sequence
2. If parallel behaviors exist, wraps result in a Parallel
3. Final JCP action is sent to robot

**Example:**
```typescript
// Skill adds parallel behavior
this.addParallelBehavior(data, SetPresentPersonBehavior);

// Skill returns main action
return { action: SayTextBehavior };

// Framework injects: Parallel([SetPresentPersonBehavior, SayTextBehavior])
```

### Graph System

#### Graph (`graph/Graph.ts`)

Represents a directed graph of connected nodes (states).

**Purpose:** Defines the structure of a skill's conversation flow.

**Key Properties:**
- `name: string` - Graph name
- `initial: Node` - Starting node
- `nodes: Set<Node>` - All nodes in graph
- `exitTransitions: Map<ExitTransition, TransitionContainer[]>` - Exit transition mappings

**Constructor:**
```typescript
constructor(name: string, exitTransitionNames: ExitTransition[])
```

**Methods:**

- `setInitialNode(node)` - Sets the starting node
- `addNode(node, transitionMapping)` - Adds a node and connects its transitions
- `addSubGraph(subGraph, transitionMapping)` - Adds a subgraph and connects its exits
- `finalize()` - Validates graph and locks it for execution
- `writeDotFile(filePath)` - Generates GraphViz dot file for visualization

**Transition Mapping:**
```typescript
[
  [TransitionName, DestinationNode],  // Transition to another node
  [TransitionName, ExitTransition]   // Exit from graph
]
```

**Validation (in finalize):**
- All nodes must be reachable from initial node
- All exit transitions must be connected
- All transitions must have valid destinations
- No duplicate transition names

**Subgraphs:**
- Graphs can be nested within other graphs
- Subgraph exit transitions connect to parent graph nodes
- Enables hierarchical organization of complex flows
- Nodes can belong to multiple graphs (for subgraph sharing)

**GraphViz Visualization:**
- Generates .dot files for graph visualization
- Color-codes initial node, regular nodes, and exit states
- Shows hierarchical structure with clusters
- Labels transitions with their names

#### GraphManager (`graph/GraphManager.ts`)

Singleton that manages graph execution and skill sessions.

**Purpose:** Coordinates node execution and maintains session state.

**Singleton Pattern:**
```typescript
GraphManager.instance  // Access singleton
```

**Key Responsibilities:**
- Assigns unique IDs to all nodes
- Maps node IDs to node instances
- Manages skill session lifecycle
- Executes node enter/exit lifecycle
- Handles transitions between nodes

**Session Structure:**
```typescript
{
  id: string,           // Session UUID
  nodeID: number,      // Current node ID
  data: any,           // Skill-specific session data
  trace: [             // History of transitions
    { nodeID: number, transition: string }
  ]
}
```

**Execution Flow:**

**Start Graph** (launch request):
```typescript
start(graph, data)
  → Creates new session
  → Sets initial node
  → Calls enterNode()
```

**Enter Node:**
```typescript
enterNode(data)
  → Fetches current node
  → Calls node.enter(data)
  → Updates trace
  → If action returned: return action
  → Else: call exitNode()
```

**Exit Node:**
```typescript
exitNode(data)
  → Fetches current node
  → Calls node.exit(data)
  → If transition returned: executeTransition()
  → Else: return (terminal)
```

**Execute Transition:**
```typescript
executeTransition(node, result, data)
  → Validates transition exists
  → Updates trace with transition name
  → If terminal: return null
  → Else: update nodeID, call enterNode()
```

**Node ID Assignment:**
- Counter starts at 0, increments for each node
- Bidirectional mapping: node ↔ ID
- Enables serialization of session state

#### Node (`graph/nodes/Node.ts`)

Abstract base class for all graph nodes.

**Purpose:** Defines a state in the skill's conversation flow.

**Key Properties:**
- `id: number` - Unique ID assigned by GraphManager
- `name: string` - Node name
- `transitionNames: Transition[]` - Valid exit transitions
- `graphs: Graph[]` - Graphs this node belongs to
- `transitions: Map<Transition, TransitionContainer>` - Transition destinations

**Constructor:**
```typescript
constructor(name: string, transitionNames: Transition[])
```

**Abstract Methods:**

```typescript
abstract async enter(data: Data): Promise<EnterResponse>
```
- Called when node is entered
- Returns action to execute, redirect, or nothing

```typescript
abstract async exit(data: Data): Promise<ExitResponse>
```
- Called with action results (if action was issued)
- Returns next transition or nothing (terminal)

**Data Structure:**
```typescript
Data = {
  // From request
  general: { accountID, robotID, lang, release },
  runtime: { character, location, loop, perception, dialog },
  skill: { id, session },
  result?: any,  // Action results for UPDATE
  
  // Added by framework
  req: PegasusRequest,
  log: Log,
  local: any,           // Skill-local data
  analytics: {},        // Analytics events
  behaviors: {          // Supplemental behaviors
    parallel: [],
    sequence: []
  }
}
```

**Response Types:**

**EnterResponse:**
```typescript
{
  action?: Action,      // JCP action to execute
  redirect?: RedirectData,  // Redirect to another skill
  final?: boolean       // Is this the final response?
}
```

**ExitResponse:**
```typescript
{
  transition?: string,  // Next transition to take
  result?: any,         // Result to pass to next node
  redirect?: RedirectData
}
```

**Built-in Node Types:**

1. **DefaultNode** - Simple terminal node
   - Returns no action
   - Transitions to Done

2. **NoOpNode** - No operation node
   - Returns no action
   - Can have custom transitions

3. **JCPNode** - Returns a JCP action
   - Returns specified JCP behavior
   - Can be terminal or continue

4. **TrueFalseNode** - Conditional branching
   - Evaluates condition
   - Transitions based on true/false

5. **SetLooperIDNode** - Sets speaker ID
   - Updates perception.speaker in context
   - Useful for multi-turn conversations

**Node Traversal:**
- `forEachDescendent(handler)` - BFS traversal of all descendant nodes
- Used for graph validation and analysis

### Skill Request/Response Protocol

#### Skill Request Types

**Location:** `packages/interfaces/src/skill/request.ts`

**MessageType:**
- `LISTEN_LAUNCH` - Launch skill from listen interaction
- `LISTEN_UPDATE` - Update skill with action results
- `PROACTIVE_LAUNCH` - Launch skill proactively

**Request Structure:**
```typescript
{
  type: MessageType,
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
      session?: {
        id: string,
        nodeID: number,
        data: any,
        trace: [{ nodeID, transition }]
      }
    },
    result?: any,  // Action results for UPDATE
    nlu?: NLUResult,
    asr?: ASRResult,
    memo?: any
  }
}
```

#### Skill Response Types

**Location:** `packages/interfaces/src/skill/response.ts`

**ResponseType:**
- `SKILL_ACTION` - Returns action to execute
- `SKILL_REDIRECT` - Redirects to another skill
- `ERROR` - Error response

**SKILL_ACTION Response:**
```typescript
{
  type: "SKILL_ACTION",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    action: JCPAction | null,
    analytics: AnalyticsData,
    final: boolean,
    fireAndForget: boolean
  }
}
```

**SKILL_REDIRECT Response:**
```typescript
{
  type: "SKILL_REDIRECT",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    skillID: string,
    nlu?: NLUResult,
    asr?: ASRResult,
    memo?: any
  }
}
```

**ERROR Response:**
```typescript
{
  type: "ERROR",
  msgID: "uuid",
  ts: 1234567890,
  data: {
    message: string,
    skill: { id: string }
  }
}
```

### JCP Actions

**Location:** `packages/interfaces/src/skill/action.ts`

**Purpose:** Defines behaviors that the robot should execute.

**ActionType:**
- `JCP` - Jibo Command Protocol action

**JCPAction Structure:**
```typescript
{
  type: "JCP",
  config: {
    version: "1.0.0",
    jcp: SupportedBehaviors
  }
}
```

**SupportedBehaviors:**
- `SLIM` - Single behavior execution
- `Sequence` - Sequential behavior execution
- `Parallel` - Parallel behavior execution
- `SetPresentPerson` - Set focused person
- `ImpactEmotion` - Modify Jibo's emotional state

**Helper Function:**
```typescript
generateJCPAction(behavior): JCPAction
```
Wraps a behavior as a JCP action with version 2.0.

### MIM (Motion Interaction Model) System

**Location:** `packages/baseskill/src/graph/mims/`

**Purpose:** Provides pre-built graph structures for playing MIM animations.

**MIM Files:**
- `.mim` files contain animation definitions
- Organized in directories:
  - `scripted-responses` - Pre-scripted responses
  - `emotion-responses` - Emotion-based responses
  - `core-responses` - Fallback responses

**MIM Factories:**

**ANFactory** - Animation Node Factory
- Creates graph for playing a single MIM
- Supports prompt data injection
- Can be final or continue

**MANFactory** - Multiple Animation Node Factory
- Creates graph for playing multiple MIMs
- Supports random selection
- Can be final or continue

**MIMFactory** - General MIM Factory
- Creates graph for MIM playback
- Supports semi-specific responses
- Handles category-based selection

**QNFactory** - Question Node Factory
- Creates graph for asking questions
- Supports opt-in flows
- Handles user responses

**OptInFactory** - Opt-In Node Factory
- Creates graph for opt-in offers
- Tracks user acceptance/rejection
- Handles analytics

**MIM Factory Options:**
```typescript
{
  mimDataProvider: (data) => string[],  // Function to get MIM paths
  promptDataProvider?: (data) => any,   // Function to get prompt data
  final: boolean                        // Is this the final action?
}
```

**Example Usage (Chitchat Skill):**
```typescript
const doMIMOptions: MimFactoryOptions = {
  mimDataProvider: (data) => data.local.path,
  promptDataProvider: (data) => data.local.promptData,
  final: true
};
const doMIM = new ANFactory('Do MIM', doMIMOptions).createGraph();
```

**Semi-Specific Responses:**
- MIMs with `_SS_` suffix are semi-specific
- Match specific categories (e.g., time, weather)
- CSV files define category members
- Enables context-aware responses

### SkillService (`SkillService.ts`)

Service wrapper that hosts a skill as an HTTP service.

**Purpose:** Provides the service infrastructure for running a skill.

**Constructor:**
```typescript
constructor(private skillV1: BaseSkill)
```

**HTTP Handler:**
- Registers skill at `/v1/main` endpoint
- No authentication required (handled by Hub)

**Initialization:**
```typescript
async init(port: number)
  → Starts HTTP server
  → Calls skill.init()
```

### Analytics

**Location:** `packages/interfaces/src/skill/analytics.ts`

**Purpose:** Track skill events for analysis.

**AnalyticsData Structure:**
```typescript
{
  [skillName: string]: [
    {
      event: string,
      properties: any
    }
  ]
}
```

**Built-in Events:**
- `SKILL_ENTRY` - Skill launched
- `SKILL_OFFER` - Opt-in offer presented

**Skill Entry Analytics:**
```typescript
{
  initial_intent: string,
  domain: string,
  was_hey_jibo_launch: boolean,
  user_initiated: boolean,
  last_skill: string
}
```

**Tracking:**
```typescript
this.track(data, 'CustomEvent', { key: value });
```

Events are automatically included in SKILL_ACTION responses.

## Server-to-Robot Communication Flow

### Skill Response to Hub

When a skill returns a response, the Hub forwards it to the robot:

**SKILL_ACTION Response:**
1. Skill returns SKILL_ACTION with JCP behavior
2. Hub adds timing information
3. Hub sends SKILL_ACTION to robot via WebSocket
4. Robot executes JCP behavior
5. Robot sends CMD_RESULT back to Hub
6. Hub sends LISTEN_UPDATE to skill
7. Skill processes result, returns next action

**Final SKILL_ACTION:**
1. Skill returns SKILL_ACTION with `final: true`
2. Hub sends to robot
3. Robot executes (if action present)
4. Transaction complete

**SKILL_REDIRECT:**
1. Skill returns SKILL_REDIRECT
2. Hub emits SKILL_REDIRECT notification to robot
3. Hub launches new skill
4. New skill proceeds normally

### JCP Action Execution

**Single Behavior (SLIM):**
```typescript
{
  type: "JCP",
  config: {
    version: "1.0.0",
    jcp: SayTextBehavior
  }
}
```
Robot executes single behavior immediately.

**Sequence Behavior:**
```typescript
{
  type: "JCP",
  config: {
    version: "1.0.0",
    jcp: Sequence([
      LookAtBehavior,
      SayTextBehavior,
      GestureBehavior
    ])
  }
}
```
Robot executes behaviors in order.

**Parallel Behavior:**
```typescript
{
  type: "JCP",
  config: {
    version: "1.0.0",
    jcp: Parallel([
      SetPresentPersonBehavior,
      SayTextBehavior
    ])
  }
}
```
Robot executes behaviors simultaneously.

### Supplemental Behaviors

Skills can add behaviors that execute alongside the main action:

**Parallel Supplemental:**
```typescript
this.addParallelBehavior(data, SetPresentPersonBehavior);
// Main action: SayTextBehavior
// Result: Parallel([SetPresentPersonBehavior, SayTextBehavior])
```

**Sequence Supplemental:**
```typescript
this.addSequenceBehavior(data, LookAtBehavior);
// Main action: SayTextBehavior
// Result: Sequence([LookAtBehavior, SayTextBehavior])
```

**Combined:**
```typescript
this.addSequenceBehavior(data, LookAtBehavior);
this.addParallelBehavior(data, SetPresentPersonBehavior);
// Result: Parallel([SetPresentPersonBehavior, Sequence([LookAtBehavior, SayTextBehavior])])
```

## Example Skill Implementation

### Chitchat Skill

**Location:** `packages/chitchat-skill/src/Chitchat.ts`

**Purpose:** Handles conversational interactions with the robot.

**Graph Structure:**
1. **IntentSplitNode** - Splits based on intent type
2. **ProcessQueryNode** - Processes user query, selects response
3. **DoMIM (ANFactory)** - Plays selected MIM animation
4. **Complete (DefaultNode)** - Terminates skill

**Initialization:**
- Loads MIM files from directories
- Builds semi-specific mappings
- Reads category CSV files

**Response Selection:**
- Scripted responses for common queries
- Emotion responses for emotional queries
- Semi-specific responses for context-aware queries
- Fallback responses for unknown queries

**MIM Selection:**
- Based on intent and entities
- Considers semi-specific categories
- Falls back to core responses

## Skill Development Guide

### Creating a Simple Skill

```typescript
import { BaseSkill } from '@jibo/baseskill';
import { skill } from '@jibo/interfaces';

export class MySkill extends BaseSkill {
  constructor() {
    super('my-skill');
  }

  protected async handle(req: PegasusRequest<SkillRequest>): Promise<SkillResponse> {
    const data = req.body.data;
    
    // Process request
    const action = generateJCPAction(SayTextBehavior("Hello!"));
    
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

### Creating a Graph Skill

```typescript
import { GraphSkill, graph } from '@jibo/baseskill';

enum Transition {
  Done = 'Done',
  Retry = 'Retry'
}

export class MyGraphSkill extends GraphSkill<Transition> {
  constructor() {
    super('my-graph-skill');
  }

  createGraph(): graph.Graph<Transition> {
    const g = new graph.Graph('My Skill', generateTransitions(Transition));
    
    const startNode = new MyStartNode('Start');
    const endNode = new graph.nodes.dn.DefaultNode('End');
    
    g.addNode(startNode, [[Transition.Done, endNode]]);
    g.addNode(endNode, [[graph.nodes.dn.Transition.Done, Transition.Done]]);
    
    g.finalize();
    return g;
  }
}
```

### Creating a Custom Node

```typescript
import { Node, Data, EnterResponse, ExitResponse } from '@jibo/baseskill';

enum MyTransition {
  Success = 'Success',
  Failure = 'Failure'
}

class MyNode extends Node<MyTransition> {
  constructor() {
    super('MyNode', [MyTransition.Success, MyTransition.Failure]);
  }

  async enter(data: Data): Promise<EnterResponse> {
    // Perform logic
    const action = generateJCPAction(SayTextBehavior("Processing..."));
    return { action };
  }

  async exit(data: Data): Promise<ExitResponse> {
    // Process action results
    if (data.result.success) {
      return { transition: MyTransition.Success };
    } else {
      return { transition: MyTransition.Failure };
    }
  }
}
```

## Key Design Principles

1. **State Machine** - Graph-based state machine for complex flows
2. **Single Responsibility** - Each node handles one piece of logic
3. **Reusability** - Subgraphs and node types can be reused
4. **Testability** - Nodes can be tested independently
5. **Visualization** - GraphViz generation for debugging
6. **Analytics** - Built-in event tracking
7. **Flexibility** - Supports both simple and complex skills
8. **Supplemental Behaviors** - Easy to add parallel/sequence actions
