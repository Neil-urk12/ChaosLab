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
