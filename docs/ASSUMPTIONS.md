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

**A replay outranks the one-grant-per-customer rule when it is the same request.** Under load, twelve
identical requests reach the customer check before any of them has written, so eleven of them would
be told the customer is already rewarded - by their own grant. When the active grant belongs to this
agent under this idempotency key, the answer is that grant with `Idempotency-Replayed: true`. P-06
promises a repeated key never fails, and this is what makes that true rather than nearly true.
*If wrong:* the branch in `CreateGrant` is removed and a concurrent double click gets a 409.

**Whichever unique index reports the race, the key is asked about first.** Two identical requests
violate both the customer index and the key index at the same moment, and which one SQL Server names
in the error is not something an answer should depend on. *If wrong:* nothing; this only removes a
dependency on an implementation detail of the database.

## API and authorisation

**A policy refusal is `forbidden` and an ownership refusal is `forbidden-agent-scope`.** Both are 403
and both were originally the same type, which made them indistinguishable to a client. The catalogue
now carries a generic `forbidden` for "your role does not cover this endpoint", raised by the
authorisation layer, while the narrower type stays for the case it names: an agent reaching for a
grant that belongs to somebody else, raised by the use case that knows whose it is.

**The development login's account passwords are in the source.** The specification says the seed
accounts have known passwords documented in the README, and that endpoint is removed from the
application outside Development, so the routes do not exist there. The signing key is not in the
source: it comes from User Secrets. *If wrong:* the accounts move to configuration.

**`GET /api/v1/campaigns` exists because nothing else exposed a campaign id.** Every campaign-scoped
route takes the id from the caller, so the agent page had no way to learn one without somebody
pasting a GUID. The endpoint was added to the specification's endpoint table rather than assumed.

**A file larger than the 10 MB limit is refused as `csv-invalid` (400), not 413.** The catalogue
entry already covers a file that is empty, which is the same kind of judgement about size; adding a
second entry for the other end of the range would say nothing new. *If wrong:* a
`payload-too-large` entry is added and the controller maps to 413.

**The parallel P-08 test fires ten simultaneous uploads, not twelve.** The working agreement asks for
at least ten, and the import limiter allows ten a minute. Ten satisfies both; twelve would have two
requests refused by the limiter working correctly, which would prove nothing about P-08.

## Reporting

**`matchedRows` counts the rows the import matched, whatever happened to the grant afterwards.** P-09
keeps a voided grant out of the numerator and the denominator, and it does. Voiding a grant does not
un-happen the purchase that was matched to it, so that row keeps counting - the two figures answer
different questions, which is why the specification reports them side by side. A row imported *after*
the grant was voided finds no active grant and stays `Unmatched`, so it never enters this count.

**The conversion rate is divided outside the view.** The four counts are additive across agents and
days, so the view can carry them at one grain and the endpoint can group them either way. A ratio is
not additive - the rate for an agent is not the sum of their daily rates - so the division happens
once, after grouping, in `GetCampaignResults`. The counting, which is the part that could drift,
stays in SQL where SSRS reads it.

## Customer directory

**The WSDL in `docs/soap/` is an Internet Archive snapshot, not a fresh download.** The public demo
service was refusing connections while this package was built, so the contract came from the
snapshot of 14 October 2025, which has the same content digest as the June 2025 one. Its
`PersonIdentification` fields match what the service returned when it last answered.
[`docs/soap/README.md`](soap/README.md) records the exact source. *If wrong:* the file is replaced
with a fresh download and the client is regenerated; nothing else changes.

**The fixtures in `tests/fixtures/soap/` are synthetic, and this is an agreed deviation.** The
specification asks for two or three recorded real responses. The service was down and the Archive
holds no recorded response for either operation used here, so the envelopes were written by hand
from the contract in the WSDL - element names, order and namespaces taken from it, values invented.
Every file says so in its first comment and carries `synthetic-` in its name. They are still
load-bearing: the adapter tests parse them, so a fixture that drifts from the generated contract
fails the build. *If wrong:* real envelopes are saved as `recorded-*.xml` and the tests point at
them.

**How the catalogue reports an unknown customer could not be verified.** With the service down there
was no way to see whether an unknown id comes back as an empty `Person` or as a SOAP fault. The
adapter treats a missing or nameless `Person` as "not found" and every fault as
`DirectoryUnavailableException`. *If wrong:* if the service answers unknown ids with a fault, one
branch is added that maps that particular fault to "not found" instead of to unavailable.

**A SOAP fault is not retried, a dropped connection and a timeout are.** A fault means the service
answered, so asking the same question twice more only wastes the caller's five seconds. *If wrong:*
the predicate in `SoapCustomerDirectory` gains a case.

**`System.Security.Cryptography.Xml` is pinned directly at 10.0.10.** It is not a new dependency:
`System.ServiceModel.Primitives` pulls it transitively at 10.0.0, which carries eight high severity
advisories. Three are fixed in 10.0.6 and five stay open through 10.0.9, so 10.0.10 is the first
release outside all eight ranges. `dotnet list package` with the vulnerable switch reports nothing
for any project. *If wrong:* the pin moves to whatever version the advisories name next.

## Persistence

**Enumerations are stored as text.** This is not a matter of taste: the specification writes the
filtered index as `WHERE Status='Active'` and the check constraint as `MatchStatus = 'Invalid'`, and
both compare a value rather than an ordinal. Storing numbers would also make the report view and any
SSRS query depend on a lookup nobody wrote down.

**`Agents.ExternalUserId` carries a unique index that is not in the specification's index table.**
The subject claim of a token has to identify exactly one agent, otherwise the lookup that turns a
token into a record owner has no single answer. *If wrong:* the index is dropped and the lookup has
to decide what two matching agents mean.

**No foreign key cascades; every relationship is `Restrict`.** Business records are never deleted,
so a delete that reaches these tables is a mistake and should fail rather than quietly take history
with it.

**Queries do not track changes by default.** Nothing in this API loads an entity in order to edit
it - a void is a conditional update and a correction is a new record - so tracking would only cost
memory and make a retried transaction harder to reason about.

**Column sizes.** The specification fixes `char(64)` for the file hash, `char(3)` for the currency
and 500 characters for the void reason. The rest are chosen here: 64 for external customer ids, 200
for names, 100 for the idempotency key, 1000 for a row error, and unbounded for the raw CSV line,
which is the only field whose length is decided by somebody else's file.

**The seed writes one campaign and three agents, and nothing else.** The specification also lists an
admin and an integration account, but neither is a row in `Agent` - they are token subjects, so they
arrive with the development token endpoint rather than with the database.

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
