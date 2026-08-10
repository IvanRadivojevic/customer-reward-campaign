# Decisions

Seven decisions that shaped this solution. Each one names the context, what was chosen, what else was
considered, and what the choice costs — because every one of them costs something.

---

## 1. Layers with ports, and no CQRS, no MediatR, no microservices

**Context.** The task asks for an architecture that answers future needs and is easy to maintain, and
for the same scenario to be reusable from different CRM systems.

**Chosen.** Four projects with one direction of dependency: `Api → Infrastructure → Core`, and
`Api → Core`. `Campaign.Core` holds the entities, the rules and the use cases, and carries **no NuGet
reference at all** — a test in `ArchitectureTests` fails the build if it ever gains one. Everything
the rules need from the outside is a port: `ICustomerDirectory`, `IGrantRepository`,
`IImportRepository`, `IReportRepository`, `IPurchaseFileReader`, `IUnitOfWork`. The web layer reaches
the rules through use case classes, never through a `DbContext`.

**Alternatives considered.** CQRS with separate read and write models — there is one model here and
one database, so the split would add two shapes of every object without a second consumer to justify
it. MediatR — a mediator earns its place when handlers are discovered dynamically or decorated in
layers; here a controller calling `CreateGrant.ExecuteAsync` is shorter, is one click to navigate,
and needs no package. Microservices — one team, one database, one transaction that spans the daily
limit and the grant; splitting it would turn a `SERIALIZABLE` transaction into a distributed protocol
to solve a problem nobody has.

**Cost.** Six port interfaces and their in-memory doubles exist purely for the seam. A one-field read
travels through a use case that adds nothing but a signature. That is the price of being able to run
every business rule in milliseconds with no database, which is what `Campaign.Tests` does 60 times
over.

---

## 2. The daily limit through a serializable transaction plus a lock on the agent's own row

**Context.** P-02 caps an agent at `DailyLimitPerAgent` active grants per business date. Counting and
inserting have to be one decision, or two requests both read four and both insert the fifth.

**Chosen.** One `SERIALIZABLE` transaction, run inside `Database.CreateExecutionStrategy()` because
EF Core refuses a hand-started transaction while retries are enabled. Inside it, in this order:
lock the agent's own row with `SELECT Id FROM Agents WITH (UPDLOCK, HOLDLOCK) WHERE Id = @agentId`,
count the active grants, compare with the limit, insert. A deadlock (SQL error 1205) repeats the
**whole** transaction, including the count, exactly once.

**Alternatives considered.** A separate `AgentDailyQuota` table with a `RowVersion` — a second place
where the truth about a day lives, which has to be kept in step with the grants it counts, and which
a manual correction can desynchronise silently. `UPDLOCK, HOLDLOCK` on the grant count itself — this
was **built and measured**: with twelve simultaneous requests it produced ~43 deadlocks per run,
exactly as many as without it, because the range being locked is the range being written to. Only
moving the lock to a row that already exists and does not move — the agent — removed them: the same
twelve requests now produce **zero** deadlocks, measured on SQL Server's own counter, and the
parallel test passes ten runs in a row.

**Cost.** The requests of one agent are serialised, so a single agent cannot have two grants in
flight at once. Different agents never block each other, and an agent awarding five discounts a day
does not notice. The lock is also one more SQL statement per grant, and it is written by hand rather
than expressed in LINQ.

---

## 3. The customer's name is frozen on the grant, and nothing else is

**Context.** Customers live in somebody else's catalogue. The report is written a month after the
campaign and has to describe what happened, not what the catalogue says today.

**Chosen.** `CustomerNameAtGrant` and `DiscountPercent` are copied onto the grant when it is made
(P-07). The customer id is kept as a reference. Nothing else from the catalogue is stored, and the
report never calls the catalogue at all — it reads only what was written.

**Alternatives considered.** A full `CustomerSnapshot` row per grant — it would carry a social
security number and a date of birth into a database that has no use for either, and every field of it
would need a retention answer. Reading the live catalogue when the report is produced — the numbers
would then change between two runs of the same report, and a campaign whose service is down would
have no report at all.

**Cost.** A customer who changes their name appears under the old one in a grant made earlier. That
is the intended reading: the grant records what the agent saw.

---

## 4. Voiding instead of editing, and why that frees the slot

**Context.** Agents make mistakes and the task says so explicitly. A grant is a business record with
an audit trail.

**Chosen.** A grant is never deleted and its business fields are never changed (P-05). A correction is
a void followed by a new grant. The void writes the reason, the actor and the time, and runs as a
single conditional statement — `UPDATE ... WHERE Status = 'Active'` — so zero rows affected means
somebody else voided it first and the answer is `409 grant-already-voided`.

**Freeing the slot is not a mechanism.** The daily count only counts active grants, and the unique
index that keeps one grant per customer is filtered on `WHERE Status = 'Active'`. A voided grant is
therefore outside both, automatically. There is no code that "releases" anything, which is why there
is no code that can forget to.

**Alternatives considered.** Editing the grant in place — the audit trail would be gone and the
report could not tell a corrected mistake from a grant that never happened. A `RowVersion` on the
grant plus a load-modify-save — the specification rules it out, and the conditional update is both
shorter and free of a second round trip.

**Cost.** A customer who was rewarded by mistake and then correctly rewarded again has two rows, and
anybody reading the table by hand has to filter by status. Every report and every rule in this
solution already does.

---

## 5. Idempotency: a key on the grant, a SHA-256 on the file

**Context.** An agent double-clicks. A network retries. An integration job re-sends yesterday's file.
None of those may create a second record.

**Chosen.** Two mechanisms, because the two things being repeated are different.

A grant carries an `Idempotency-Key` supplied by the caller, unique per agent
(`UX_RewardGrants_Agent_IdempotencyKey`). The same key with the same campaign and customer returns
the existing grant with `200` and `Idempotency-Replayed: true`; the same key with a different request
is refused with `409 idempotency-key-reused`, so a key can never hand back a grant somebody did not
ask for. The agent page makes the key once per button, so a double click is the same request rather
than a second one.

An import carries the SHA-256 of the file bytes, unique per campaign
(`UX_ImportBatches_Campaign_FileSha256`). P-08 is settled by attempting the insert and reading the
winner back — never by asking first, because two uploads that arrive together would both be told the
file is new.

**What happens to a corrected file.** A corrected file has different bytes, so a different hash, so it
imports as a **new batch** beside the first one. That is deliberate: the earlier batch is what was
processed and stays as a record. It also means the campaign then carries rows from both files, and
`matchedRows` counts both. Superseding a batch is not implemented; doing it would need a decision
about what happens to the grants the first batch matched, and that decision belongs to whoever
operates the campaign.

**Alternatives considered.** Deriving the grant key on the server from campaign and customer — then a
retry and a genuine second attempt look identical and the caller loses the ability to say which it
meant. Comparing file names instead of bytes — the same name is sent every month.

**Cost.** The caller has to generate a key, and `Idempotency-Key` is required rather than optional. A
file that differs by one byte of whitespace is a new file.

---

## 6. The CSV is the authority on who bought

**Context.** The purchase report arrives a month after the campaign, from a system this solution does
not own.

**Chosen.** Rows are processed one at a time and tolerantly: a row that cannot be read — or cannot be
stored, which is not the same thing — is written as `Invalid` with its raw line and the reason, and
the file always runs to the end. Matching is by customer against the active grants of that campaign:
found is `Matched`, not found is `Unmatched` and reported, unreadable is `Invalid`.

**No date validation.** There is no check that the purchase happened after the grant. A purchase date
is data, not a rule; the file says what happened and this solution is not entitled to argue with it.

**No duplicate-row detection.** The same customer may appear several times, and every one of those
rows is `Matched`. Without an order identifier in the file there is no way to tell a duplicated line
from a second purchase, so no status is invented for it. Double counting is prevented where it
matters instead: conversion is `COUNT(DISTINCT MatchedGrantId)`, so a customer who bought three times
converts one grant, and a whole file sent twice is caught by P-08.

**Alternatives considered.** Refusing a file with any bad row — one broken line would lose a month of
sales data. Rejecting purchases dated before the grant — it would silently drop rows on a rule
nobody agreed to. Treating a repeated customer as a duplicate — it would under-count real repeat
buyers, who are exactly what a loyalty campaign is looking for.

**Cost.** A genuinely duplicated line inflates `matchedRows`. The report shows it next to
`convertedGrants` rather than hiding it, so the difference is visible instead of guessed at.

---

## 7. Deliberately out of scope

Each of these was considered and left out on purpose. None of them is started, stubbed or half-built.

**Multi-tenancy.** No tenant column, no filter. Adding one later means a column on every table and a
global query filter; adding it now means carrying it through every rule for a second operator who
does not exist yet.

**Background processing of large files.** The import is synchronous and answers `200` with the summary
of the batch, never `202`. `ImportBatch` therefore has two states rather than four: nobody can observe
a batch while it is being processed. A weekly campaign's report is small. Moving it to a queue means
adding `Processing` and `Failed`, a status endpoint that is polled, and a story for a worker that dies
half way through.

**A production identity provider.** Development issues its own tokens from a symmetric key in User
Secrets, and that endpoint is removed from the application outside Development — its routes do not
exist there. Moving to Microsoft Entra ID is `Auth:Authority` and `Auth:Audience` in configuration
instead of a signing key; the handler then fetches the issuer's keys itself and no code changes.

**GDPR erasure.** The grant keeps a name and a customer id. Erasing a customer would have to decide
what happens to a business record that a report depends on, and that is a policy question, not a
coding one. What is done here is narrower and deliberate: the catalogue's social security number and
date of birth are read and dropped — they never enter this database.

**Caching the customer catalogue.** Nothing is cached. A stale name copied onto a grant would quietly
break P-07, which promises the name was true at the moment of the grant. The cost is a network call
per lookup, guarded by a five second timeout and two retries.

**CORS.** There is none. The API serves its own page from `wwwroot`, so the browser and the endpoints
share an origin and nothing needs to be allowed. A separately hosted front end would need a policy
here, and that is the moment to write one.
