# Architecture Decisions

Use this folder for durable architecture notes that would otherwise get lost in chat or scattered release plans.

Preferred contents:

- turn and EOS behavior decisions
- protocol parity notes
- state-machine boundaries
- storage and trust boundary decisions
- any other long-lived implementation contract that should be easy to rediscover later

Current turn-boundary work lives in `turn-boundary-eos-parity.md` and captures the rule that decisive hints or early buffered OGG ASR probes can close a command turn, while OGG EOS and the hard timeout remain safety nets.

When a decision starts as exploration, record the mapping here first, then link the active release plan to it.
