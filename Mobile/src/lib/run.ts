import type { FindingOutcome, RunDetail, RunFinding, RunFindings } from '@web/api';
import { SEVERITIES } from '@web/findings';

import { useLoad } from './api';

// The findings query answers 404 for a run that has no findings yet.
const NO_FINDINGS: RunFindings = { runs: [], run: null, findings: [] };

const OUTCOME_ORDER: FindingOutcome[] = ['Failed', 'Degraded', 'Inconclusive', 'Passed', 'NotApplicable'];

// Worst outcome first, then highest severity.
export const worstFirst = (a: RunFinding, b: RunFinding) =>
  OUTCOME_ORDER.indexOf(a.outcome) - OUTCOME_ORDER.indexOf(b.outcome) || SEVERITIES.indexOf(a.severity) - SEVERITIES.indexOf(b.severity);

// One run and its findings. The run screen and both detail screens read the same two cached requests.
export function useRun(id: string, runId: string) {
  const run = useLoad<RunDetail>(`/api/projects/${id}/runs/${runId}`);
  const findings = useLoad<RunFindings>(`/api/projects/${id}/findings?run=${runId}`, NO_FINDINGS);
  return {
    run: run.data,
    findings: findings.data?.findings,
    // Every scenario that started or was skipped: the live feed while the run is active, the full list once it is finished.
    scenarios: run.data?.scenarios ?? run.data?.live?.feed ?? [],
    page: {
      loading: !run.data || !findings.data,
      error: run.error ?? findings.error,
      refreshing: run.refreshing || findings.refreshing,
      onRefresh: () => {
        run.refresh();
        findings.refresh();
      },
    },
  };
}
