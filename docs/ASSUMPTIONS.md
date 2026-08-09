# Assumptions

Decisions taken where the specification does not say, written down so they can be challenged
instead of discovered. Each one names what would change if the assumption turns out to be wrong.

## Domain and grant rules

**The order in which a grant is refused.** A request is answered against the rules in this order:
idempotency replay, campaign window (P-01), customer catalogue, one grant per customer (P-03), daily
limit (P-02). A replay is answered first because the caller is asking about a grant that was already
made, so a campaign that has closed in the meantime must not turn a successful call into an error.
The catalogue is asked before the transaction is opened, so no network call happens inside a
serializable transaction. If two rules are broken at once, the customer rule answers before the
limit rule, because it is about the customer the agent picked rather than about the agent's day.
*If wrong:* the order of two checks changes, no rule changes.

**An unknown campaign is answered as `campaign-not-active` (409).** The error catalogue has
`grant-not-found` but no `campaign-not-found`, and a campaign that does not exist is certainly not
one a reward can be granted in. *If wrong:* a `campaign-not-found` entry is added to the catalogue
and the API maps it to 404.

**A valid token that does not identify an active agent is answered as `agent-not-active` (403).**
This covers two cases at once: no agent carries that subject, and the agent exists but has been
deactivated. The domain never raises a 401, because a genuine 401 is the authentication
middleware's answer - the token here is valid, it simply does not belong to somebody who may own
records. The error type was added to the catalogue in the specification for this.

**A deactivated agent may read but may not write.** `/grants` and `/agents/me/quota` still answer,
because the records are theirs and the history does not disappear with the flag. Creating and
voiding grants are refused. Answering an idempotent replay stays allowed as well: it returns a
grant that already exists and creates nothing, so a client retrying a request that succeeded before
the agent was deactivated gets its answer instead of an error.
*If wrong:* the check moves ahead of the replay branch in `CreateGrant`, one line.

**An agent who explicitly asks for another agent's grants gets `forbidden-agent-scope` (403).** The
alternative was to silently narrow the filter to the agent themselves. Refusing is louder and
matches the record ownership rule. Without an `agentId` filter, an agent's list is their own.

**The void reason stays optional.** The model says `VoidReason? (max 500)`, so the column is
nullable, while P-04 says voiding records the reason. The nullable column wins and the length is
enforced; the API can still require the field at the request level. *If wrong:* the use case rejects
an empty reason.

**Timestamps are `DateTimeOffset`.** The clock is a `TimeProvider`, which produces `DateTimeOffset`,
so storing that type avoids converting back and forth and losing the offset by accident. Calendar
dates with business meaning stay `DateOnly`.

## Import

**`CompletedWithErrors` means the file contained rows that could not be read.** The specification
allows exactly two states without saying which is which. Unmatched rows are an expected, reported
outcome and do not make a batch faulty; only invalid rows do. *If wrong:* the condition in
`ImportBatch.Create` changes, nothing else does.

## Customer catalogue

**`CustomerDto` carries the external id and the name only.** These are the two fields the grant
needs, one of which is frozen on the grant. The real SOAP contract is read from the downloaded WSDL
in a later work package and may offer more. *If wrong:* the record gains fields; the port signature
does not change.
