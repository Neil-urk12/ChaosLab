# My TODO

## Run the live feed query against a real Postgres

The two new queries in `Backend/Features/TestRuns/GetRun.cs` (`LiveAsync`: the execution feed and the
execution currently running) were only checked by printing the SQL EF generates. They have never been
executed: the e2e tests mock the API and `Backend.Tests` is not in this repository.

Steps:

1. Start the dev stack (`make up`) and apply migrations (`dotnet ef database update` from `Backend/`).
2. There is no runner yet, so set a run up by hand in the dev database: one `TestRuns` row with status
   Running, and at least one `ScenarioExecutions` row for it that is not Queued (ideally one Running with
   an `HttpObservations` row, and one Completed with a `ResilienceFindings` row).
3. Open the run page, or call `GET /api/projects/{id}/runs/{runId}`.
4. Expect a 200 whose `live.feed` lists the started executions newest first, each with its findings worst
   first, and whose `live.current` is the oldest Running execution with its requests.

Dev database only. Never point this at production data.
