# Authentication and authorization mock-data catalogue

Prepared against the current API and SEC-01/SEC-02 requirements. This is a fixture
and test-case specification, not a database seed or an executed test report.
It covers the current role/ownership model; future policies require new cases.

## 1. Shared fixture data

Use a fresh isolated PostgreSQL test database/scenario. Assign fresh GUIDs to the
aliases below and retain an alias-to-ID map for requests. Never use production data.

| Alias | Record | Relationships / purpose |
| --- | --- | --- |
| C-A | Customer: Synthetic Alice | Owns V-A and A-A |
| C-B | Customer: Synthetic Bob | Owns V-B and A-B; foreign-resource target for Alice |
| C-EMPTY | Customer: Synthetic Empty | No vehicles or appointments; distinguishes empty collections from denied access |
| V-A | Vehicle: SYN-A | CustomerId = C-A |
| V-B | Vehicle: SYN-B | CustomerId = C-B |
| D-1 | Dealership: Synthetic Dealer | Asia/Ho_Chi_Minh |
| S-1 | Service: Synthetic Service | DurationMinutes = 60 |
| T-1 | Qualified technician | D-1; explicit qualification for S-1 |
| B-1 | Service bay | D-1 |
| A-A | Confirmed appointment | C-A, V-A, D-1, S-1, T-1, B-1 |
| A-B | Confirmed appointment | C-B, V-B, D-1, S-1, T-1, B-1; different interval |
| MISSING | Absent resource | Fresh nonempty GUID, never inserted; use a separate alias per resource type |

Seed A-A and A-B at nonoverlapping times. Reserve another free future interval for
successful POST cases, fully inside both resource windows and dealership hours.
Use a fresh scenario for each booking case so capacity conflicts cannot mask access
results. With the existing frozen application clock (2030-01-01), 2030-01-02 at
09:00+07:00 is suitable if explicitly made available. JWT lifetime uses real UTC
time, not the application's frozen booking clock.

## 2. Mock identities and trusted mappings

Default issuer I-1 = `https://identity.scheduler.test`; audience = `scheduler-tests`.
All tokens have exactly one nonempty `sub`, one trusted `iss`, valid dates and a
trusted RS256 signature unless a case explicitly mutates them.

| Identity | sub | role claims | CustomerIdentityLink | Purpose |
| --- | --- | --- | --- | --- |
| U-ADMIN | admin-01 | Admin | None | Administrative access without customer mapping |
| U-A | customer-a | Customer | (I-1, customer-a) -> C-A | Own-resource success |
| U-B | customer-b | Customer | (I-1, customer-b) -> C-B | Symmetric foreign-resource denial |
| U-EMPTY | customer-empty | Customer | (I-1, customer-empty) -> C-EMPTY | Empty successful reads |
| U-UNMAPPED | customer-unmapped | Customer | None | Valid identity without customer access |
| U-DUAL | dual-01 | Customer, Admin | None | Admin privileges take precedence |
| U-NOROLE | no-role | No role claims | None | Authenticated but forbidden |
| U-UNKNOWN | unknown-role | Technician | None | Unsupported role forbidden |
| U-LOWER | lower-role | customer | None | Role casing mismatch forbidden |
| U-CASE | Customer-A | Customer | None | Subject differs in case from customer-a |
| U-CLAIMS | claims-only | Customer | None | Add customerId=C-A claim; cannot replace trusted mapping |
| U-ALTISSUER | customer-a | Customer | (I-2, customer-a) -> C-B | Issuer is part of mapping key |

I-2 = `https://other-identity.scheduler.test`. A token from I-2 is **401** on the
default I-1 host. To test mapping isolation, use a separate test host configured
to trust I-2, sign with that host's synthetic key, and retain both mapping rows.
That identity must resolve C-B, never C-A. This does not add multi-issuer support.

No password fixtures are needed: the API validates external tokens and has no
login/token-issuance endpoint. Use the existing `SyntheticJwt` helper and ephemeral
keys in tests; never commit bearer tokens or private keys. In that helper, omitted
`roles` means Admin: explicitly pass `roles: []` for U-NOROLE.

## 3. Authentication token variants

Run each rejection against a protected GET and POST. Use malformed JSON for a
second POST check to prove authentication happens before body validation.

| ID | Token/header mutation | Expected |
| --- | --- | --- |
| AUTH-01 | No Authorization header | 401 unauthenticated |
| AUTH-02 | Empty Bearer value / wrong scheme (Basic) | 401 |
| AUTH-03 | Malformed JWT / truncated JWT | 401 |
| AUTH-04 | Expired by 5 minutes | 401 |
| AUTH-05 | nbf 5 minutes in the future | 401 |
| AUTH-06 | Untrusted issuer | 401 |
| AUTH-07 | Wrong audience | 401 |
| AUTH-08 | Signed by another ephemeral key | 401 |
| AUTH-09 | Unsigned token (alg=none) | 401 |
| AUTH-10 | Change payload after signing (e.g. Customer to Admin) | 401 |
| AUTH-11 | Missing / empty / whitespace-only sub (separate cases) | 401 |
| AUTH-12 | Multiple subject values | 401 |
| AUTH-13 | Missing issuer / audience / expiration (separate cases) | 401 |
| AUTH-14 | Valid U-A and U-ADMIN tokens | Shared reference GET 200 |
| AUTH-15 | Configured subject_id/access_role claim names, matching token | 200 on shared reference GET |
| AUTH-16 | Custom claim configuration but token only has default sub/role | Missing configured subject: 401; valid configured subject but wrong role claim: 403 |
| AUTH-17 | Invalid authority/issuer/audience/claim-name configuration | 401; fail closed, no 500 |

Clock skew is 30 seconds. Add controlled-time boundary cases for expiration and
not-before just inside/outside that allowance. Avoid wall-clock assertions exactly
at 30 seconds. Existing helper defaults expiration, so missing-expiration cases
need a deliberately constructed token; passing null to `Issue` is insufficient.
Repeated headers and non-string/malformed claim payloads should also be rejected
without 500; pin the expected 400/401 behavior in the HTTP host before automating
those transport/parser edge cases.

## 4. Route and role matrix

Use valid IDs/body and available capacity. `Admin` includes U-DUAL.
`Other` means U-NOROLE/U-UNKNOWN/U-LOWER. Anonymous includes invalid tokens for
protected routes. Readiness success additionally requires a ready database.

| Route | Anonymous | Other | U-UNMAPPED | U-A | Admin |
| --- | --- | --- | --- | --- | --- |
| GET /api/dealerships | 401 | 403 | 200 | 200 | 200 |
| GET /api/service-types | 401 | 403 | 200 | 200 | 200 |
| GET /api/availability | 401 | 403 | 200 | 200 | 200 |
| GET /api/customers | 401 | 403 | 403 | 403 | 200 |
| GET /api/customers/C-A/vehicles | 401 | 403 | 403 | 200 | 200 |
| GET /api/appointments?customerId=C-A | 401 | 403 | 403 | 200 | 200 |
| GET /api/appointments/A-A | 401 | 403 | 403 | 200 | 200 |
| POST /api/appointments for C-A/V-A | 401 | 403 | 403 | 201 | 201 |
| GET /api/demo/fixtures, enabled Development fixture | 401 | 403 | 403 | 403 | 200 |
| GET /api/demo/fixtures, disabled or non-Development | 401 | 403 | 403 | 403 | 404 |
| GET /health/live, no credentials | 200 | — | — | — | — |
| GET /health/ready, no credentials | 200 or 503 | — | — | — | — |

Availability 200 can contain `available: false`; it grants no reservation.
Swagger is exposed in Development; check anonymous access there separately from
protected API routes. Do not assume production Swagger exposure.

## 5. Ownership and data-integrity cases

| ID | Actor / request | Expected |
| --- | --- | --- |
| OWN-01 | U-A reads A-A, lists C-A, reads C-A vehicles | 200; only C-A records |
| OWN-02 | U-A reads A-B vs missing appointment | Both 404 not_found; equivalent concealed-resource response |
| OWN-03 | U-A reads C-B vehicles vs missing customer vehicles | Both 404; equivalent concealed-resource response |
| OWN-04 | U-A lists C-B vs missing customer | Both 403 forbidden |
| OWN-05 | U-A books C-B/V-B | 403; no appointment created |
| OWN-06 | U-A books C-A/V-B vs C-A/missing vehicle | Both 404; no appointment created |
| OWN-07 | U-B attempts the corresponding C-A operations | Same denials as OWN-02 through OWN-06 |
| OWN-08 | U-UNMAPPED / U-CASE / U-CLAIMS access customer-scoped data | 403 even when supplied customerId is C-A |
| OWN-09 | U-EMPTY reads own vehicles and appointments | 200 with [] |
| OWN-10 | U-ADMIN / U-DUAL books C-B/V-B and reads B records | 201 / 200 without a customer mapping |
| OWN-11 | Admin books C-A/V-B | 400 invalid_request: selected vehicle belongs to a different customer |
| OWN-12 | Admin accesses absent customer/appointment/vehicle | 404 with otherwise valid request |
| OWN-13 | Same subject under I-2, separately trusted host | Resolves C-B only; C-A requests follow foreign-resource denials |
| OWN-14 | Insert duplicate (I-1, customer-a), targeting C-B | Database unique violation; original mapping remains |
| OWN-15 | Insert identity link targeting nonexistent customer | Database FK violation |
| OWN-16 | Request body/query claims another role/subject/customer mapping | Cannot elevate privilege; server uses validated principal and persisted link |

For OWN-16, a supplied customerId is input, never identity proof. Use concrete
foreign-resource cases above; unknown JSON fields alone do not establish identity.
Admin privilege bypasses customer scope, not vehicle/customer consistency or normal
booking validation. Use mapped and unmapped variants of U-DUAL to confirm this.

## 6. Assertions and execution notes

- Assert HTTP status, ProblemDetails code/content type and traceId. Compare foreign
  and missing responses excluding per-request trace IDs; neither may reveal owner data.
- After denied POSTs, query the isolated database and assert no appointment was
  created. Assert no confirmation event was dispatched where event capture is used.
- Assert tokens, synthetic private-input canaries and database exception details
  do not appear in response bodies or captured logs.
- Invalid tokens return 401 and invalid roles return 403 before malformed-body
  validation. An allowed role with an invalid request can return 400 before customer
  mapping; use valid request shapes when testing ownership itself.
- Clear `HttpClient.DefaultRequestHeaders.Authorization` for anonymous cases:
  `SchedulerFactory` otherwise supplies an Admin token by default.
- Generate dates relative to real UTC for tokens and use the same synthetic signing
  key whose public key is installed in that host's metadata. A token from another
  host is a signature-negative case, not a valid-user fixture.
- Keep ownership/constraint tests on PostgreSQL. Mock read stores are suitable for
  isolated middleware tests but cannot establish database constraints or persistence.

Existing coverage anchors: `BearerTests.cs` (token rejection, role/configuration
checks), `OwnershipHttpTests.cs` (ownership/Admin/identity uniqueness),
`EnvironmentTests.cs` (demo gating and health), and `ApplicationBehaviorTests.cs`
(application policy checks). Rows above include additional proposed cases; the
catalogue does not claim every row already has an automated test.

Current verification limitation: the current solution build fails on incomplete
idempotency transaction implementations. No new executable authentication tests
were run for this catalogue. Once that work is complete, run the SEC01/SEC02 tests
against the isolated database, then add missing rows and run the full suite.
Idempotency replay across identities should be added when its contract is complete;
it must never let one principal retrieve another principal's booking.
