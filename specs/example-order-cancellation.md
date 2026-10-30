# Example only: customer order cancellation

These are fictional sample requirements, not facts about your application.
Adapt and copy into feature.md only if this is the feature you actually want.

## Goal

An authenticated customer can cancel their own pending order from the order page.

## Acceptance criteria

- AC-01: The owner can cancel a Pending order; its state becomes Cancelled.
- AC-02: An unauthenticated request is rejected without changing data.
- AC-03: A customer cannot inspect or cancel another customer's order. Use the
  repository's established non-disclosure response convention.
- AC-04: Shipped and Delivered orders cannot be cancelled; the response follows
  the established conflict convention and the order is unchanged.
- AC-05: Repeating cancellation of an already Cancelled owned order reports the
  cancelled state without duplicate audit or notification side effects.
- AC-06: Competing shipment and cancellation operations cannot both succeed on
  the same Pending state. Use the database's supported atomic/concurrency pattern.
- AC-07: The UI confirms intent, prevents duplicate in-flight submissions and
  handles success, conflict and network failure visibly. Server checks remain
  authoritative even if the UI hides the cancellation button.
- AC-08: Record the actor and timestamp using existing audit conventions without
  logging tokens or sensitive payloads.

## Constraints

Follow existing routing, authentication, error envelopes and data access patterns.
The planner must identify the shipment path too: a cancellation-side check alone
may not enforce the concurrency invariant. Reuse the existing notification mechanism
if one exists; otherwise report the missing dependency rather than inventing one.

## Out of scope

Refunds, payment reversal, stock adjustments and cancellation of paid orders.
If existing business rules require these, flag the conflict before implementation.

## Verification expectations

Unit tests for status rules; endpoint tests for ownership and repeated requests;
real-provider concurrency tests where available; browser verification of the UI.
Report any missing Oracle/test environment or browser capability explicitly.
