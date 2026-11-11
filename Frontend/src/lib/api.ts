// Response shapes of the backend dashboard queries (Backend/Features/Projects).

export type TestRunStatus = 'Queued' | 'Running' | 'Completed' | 'PartiallyCompleted' | 'Failed' | 'Cancelled';
export type ResiliencePattern = 'Retry' | 'Timeout' | 'CircuitBreaker' | 'Fallback' | 'Idempotency';
export type ProjectRole = 'Owner' | 'Editor' | 'Viewer';

export type TestRunRow = {
	id: string;
	projectId: string;
	projectName: string;
	environmentName: string;
	status: TestRunStatus;
	completedScenarios: number;
	totalScenarios: number;
	requestedBy: string;
	createdAt: string;
	startedAt: string | null;
	completedAt: string | null;
	score: number | null;
	failedFindings: number;
};

export type FindingRow = {
	id: string;
	projectId: string;
	projectName: string;
	httpMethod: string;
	path: string;
	pattern: ResiliencePattern;
	summary: string;
	createdAt: string;
};

export type Overview = {
	projectCount: number;
	endpointCount: number;
	runsLast7Days: number;
	runsPrevious7Days: number;
	averageScore: number | null;
	previousAverageScore: number | null;
	scoredRunsLast30Days: number;
	failedFindingCount: number;
	scoreTrend: { completedAt: string; score: number }[];
	findings: FindingRow[];
	recentRuns: TestRunRow[];
	recentProjects: {
		id: string;
		name: string;
		description: string | null;
		baseUrl: string | null;
		latestScore: number | null;
		lastRunAt: string | null;
		failedFindings: number;
	}[];
};

export type ProjectListRow = {
	id: string;
	name: string;
	description: string | null;
	createdAt: string;
	updatedAt: string;
	environmentName: string | null;
	baseUrl: string | null;
	scenarioCount: number;
	lastRunAt: string | null;
	latestScore: number | null;
};

export type ProjectDetail = {
	id: string;
	name: string;
	description: string | null;
	defaultEnvironment: { name: string; baseUrl: string } | null;
	endpointCount: number;
	scenarioCount: number;
	completedRunCount: number;
	failedFindingCount: number;
	latest: {
		runId: string;
		completedAt: string;
		score: number;
		previousScore: number | null;
		breakdown: Record<'retry' | 'timeout' | 'circuitBreaker' | 'fallback' | 'idempotency', number | null> | null;
	} | null;
	spec: { title: string | null; openApiVersion: string; importedAt: string } | null;
	members: { userId: string; displayName: string; role: ProjectRole }[];
	findings: FindingRow[];
	recentRuns: TestRunRow[];
};

// Backend/Features/ApiSpecs
export type EndpointTesting = 'Included' | 'Excluded' | 'NeverTest';
export type SpecSource = 'Paste' | 'FileUpload' | 'Url';

export type SpecIssue = { severity: 'Error' | 'Warning'; location: string; message: string };

export type SpecImportReport = {
	filename: string | null;
	openApiVersion: string | null;
	operationCount: number;
	issues: SpecIssue[];
};

export type SpecEndpoint = {
	id: string;
	httpMethod: string;
	path: string;
	operationId: string | null;
	tag: string | null;
	security: string[];
	requestBody: string | null;
	responses: string[];
	scenarioCount: number;
	testing: EndpointTesting;
	change: 'New' | 'Modified' | null;
};

export type ApiSpecDetail = {
	id: string;
	title: string | null;
	openApiVersion: string;
	filename: string | null;
	sourceType: SpecSource;
	sourceUrl: string | null;
	importedAt: string;
	importedBy: string;
	securitySchemes: { name: string; type: string; global: boolean }[];
	endpoints: SpecEndpoint[];
	changes: { added: number; modified: number; removed: string[] } | null;
};

// Backend/Features/Scenarios
export type ScenarioType =
	| 'Baseline'
	| 'Latency'
	| 'ServerErrorStorm'
	| 'Throttle429'
	| 'TimeoutDrop'
	| 'MalformedJson'
	| 'EmptyResponse'
	| 'SlowDrip'
	| 'AuthExpiry'
	| 'DnsFailure'
	| 'DuplicateRequest';

export type InvariantType =
	| 'StatusCodeIn'
	| 'FieldEquals'
	| 'FieldNotBelow'
	| 'MaxRecordsAfterDuplicates'
	| 'MaxDurationMs'
	| 'HeaderPresent';

export type Invariant = {
	type: InvariantType;
	params: { codes?: number[]; path?: string; value?: string; min?: number; max?: number; ms?: number; name?: string };
	readBack?: { method: 'GET'; path: string };
};

export type Scenario = {
	id: string;
	endpointId: string;
	httpMethod: string;
	path: string;
	operationId: string | null;
	type: ScenarioType;
	parameters: Record<string, string | number>;
	reason: string;
	enabled: boolean;
	invariants: Invariant[];
};

export type ScenarioList = { endpointCount: number; scenarios: Scenario[] };

// Backend/Features/Environments
export type EnvironmentKind = 'Development' | 'Test' | 'Staging' | 'ProductionMirror';
export type AuthType = 'None' | 'Bearer' | 'ApiKey' | 'Basic';

export type TargetSettings = {
	id: string;
	name: string;
	baseUrl: string;
	kind: EnvironmentKind;
	requestTimeoutSeconds: number;
	verifyTls: boolean;
	authorizedBy: string | null;
	authorizedAt: string | null;
};

export type Credential = {
	authType: AuthType;
	label: string | null;
	hint: string | null;
	username: string | null;
	validatedAt: string | null;
	valid: boolean | null;
	validation: string | null;
};

export type SecurityScheme = {
	name: string;
	inSpec: boolean;
	type: string;
	httpScheme: string | null;
	in: string | null;
	parameterName: string | null;
	operations: string[];
	credential: Credential | null;
};

export type GlobalHeader = { id: string; name: string; value: string | null; secret: boolean; hint: string | null; appliesTo: string[] };

export type EnvironmentSettings = {
	target: TargetSettings | null;
	specServers: string[];
	schemes: SecurityScheme[];
	headers: GlobalHeader[];
};

// Backend/Features/TestRuns
export type RunHistoryRow = {
	id: string;
	number: number;
	environmentName: string;
	status: TestRunStatus;
	completedScenarios: number;
	totalScenarios: number;
	requestedBy: string;
	createdAt: string;
	startedAt: string | null;
	completedAt: string | null;
	score: number | null;
	previousScore: number | null;
	scoringVersion: string;
	failureMessage: string | null;
	evidenceCoverage: number | null;
};

export type RunHistoryPage = {
	total: number;
	matched: number;
	page: number;
	pageSize: number;
	environments: string[];
	scoringVersions: string[];
	runs: RunHistoryRow[];
};

// GET /projects/{id}/runs/preflight. Blockers stop a run from being created; credential problems are warnings.
export type RunPreflight = {
	target: { name: string; baseUrl: string; kind: EnvironmentKind; authorized: boolean } | null;
	reachability: { reachable: boolean; status: number | null; error: string | null; elapsedMs: number } | null;
	credentials: { scheme: string; configured: boolean; valid: boolean | null; validation: string | null; validatedAt: string | null }[];
	spec: { openApiVersion: string; operationCount: number; neverTestCount: number } | null;
	scoringVersion: string;
	blockers: string[];
};

export type ObservationOutcome =
	| 'Forwarded'
	| 'SyntheticResponse'
	| 'ModifiedResponse'
	| 'Dropped'
	| 'Timeout'
	| 'ConnectionError'
	| 'ProxyError';

export type TimelineEvent = {
	attemptNo: number;
	requestMethod: string;
	requestPath: string;
	startedAt: string;
	durationMs: number | null;
	clientStatusCode: number | null;
	upstreamStatusCode: number | null;
	outcomeType: ObservationOutcome;
	errorType: string | null;
};

export type RunTimeline = {
	executionId: string;
	httpMethod: string;
	path: string;
	scenarioType: ScenarioType;
	outcome: 'Passed' | 'Failed' | 'Inconclusive' | null;
	failureReason: string | null;
	completedAt: string | null;
	events: TimelineEvent[];
};

export type RunDetail = {
	id: string;
	number: number;
	status: TestRunStatus;
	environmentName: string;
	requestedBy: string;
	createdAt: string;
	startedAt: string | null;
	completedAt: string | null;
	completedScenarios: number;
	totalScenarios: number;
	skippedScenarios: number;
	score: number | null;
	scoringVersion: string;
	failureMessage: string | null;
	previous: { id: string; number: number; score: number } | null;
	timelines: RunTimeline[];
	// Set only while the run is Queued or Running.
	live: RunLive | null;
};

export type ScenarioExecutionStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Skipped';

export type FeedRow = {
	executionId: string;
	httpMethod: string;
	path: string;
	scenarioType: ScenarioType;
	status: ScenarioExecutionStatus;
	outcome: 'Passed' | 'Failed' | 'Inconclusive' | null;
	failureReason: string | null;
	startedAt: string | null;
	completedAt: string | null;
	// Worst outcome first; empty until the evaluator has judged the execution.
	findings: { outcome: FindingOutcome; summary: string }[];
};

export type RunLive = {
	feed: FeedRow[];
	current: {
		executionId: string;
		parameters: Record<string, string | number>;
		invariants: Invariant[];
		events: TimelineEvent[];
	} | null;
};

// Backend/Features/Findings
export type FindingOutcome = 'Passed' | 'Degraded' | 'Failed' | 'Inconclusive' | 'NotApplicable';
export type Severity = 'Info' | 'Low' | 'Medium' | 'High' | 'Critical';

// Free-form JSONB written by the evaluator; every key is optional, so read it defensively.
export type FindingEvidence = {
	detail?: string;
	expected?: string;
	observed?: string;
	invariantViolated?: boolean;
	facts?: { label: string; value: string }[];
	interpretation?: { observed?: string; suggests?: string; notProven?: string };
};

export type RunFinding = {
	id: string;
	severity: Severity;
	outcome: FindingOutcome;
	pattern: ResiliencePattern;
	httpMethod: string;
	path: string;
	scenarioType: ScenarioType | null;
	summary: string;
	evidence: FindingEvidence;
	recommendation: { title: string; text: string } | null;
};

export type FindingRunOption = { id: string; number: number; status: TestRunStatus; completedAt: string | null };

export type RunFindings = {
	runs: FindingRunOption[];
	run: FindingRunOption | null;
	findings: RunFinding[];
};

// Backend/Features/Members
export type ProjectMemberRow = {
	userId: string;
	displayName: string;
	email: string | null;
	role: ProjectRole;
	joinedAt: string;
	removedAt: string | null;
	removedBy: string | null;
};

export type ProjectMembers = { yourRole: ProjectRole; members: ProjectMemberRow[]; removed: ProjectMemberRow[] };

export type MemberCandidate = { userId: string; displayName: string; email: string | null; role: ProjectRole | null };
