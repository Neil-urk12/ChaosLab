// Labels and parameter fields for generated scenarios. Field ranges mirror Backend/Features/Scenarios/ScenarioRules.cs.
import type { Invariant, InvariantType, ScenarioType } from './api';

export const faultLabels: Record<ScenarioType, string> = {
	Baseline: 'Baseline',
	Latency: 'Latency',
	ServerErrorStorm: '5xx storm',
	Throttle429: '429 throttle',
	TimeoutDrop: 'Timeout / drop',
	MalformedJson: 'Malformed JSON',
	EmptyResponse: 'Empty response',
	SlowDrip: 'Slow drip',
	AuthExpiry: 'Auth expiry',
	DnsFailure: 'Dependency failure',
	DuplicateRequest: 'Duplicate request'
};

// Scoring category of each fault (PROJECT.md §10.2).
export const categories: Record<ScenarioType, string> = {
	Baseline: 'Baseline',
	Latency: 'Timeout',
	ServerErrorStorm: 'Retry',
	Throttle429: 'Rate-limit',
	TimeoutDrop: 'Timeout',
	MalformedJson: 'Response validation',
	EmptyResponse: 'Response validation',
	SlowDrip: 'Timeout',
	AuthExpiry: 'Auth failure',
	DnsFailure: 'Dependency failure',
	DuplicateRequest: 'Idempotency'
};

// Duplicate request is a behavioral scenario and Baseline is not a fault, so neither is a chaos primitive.
export const PRIMITIVES: ScenarioType[] = [
	'Latency',
	'ServerErrorStorm',
	'Throttle429',
	'TimeoutDrop',
	'MalformedJson',
	'EmptyResponse',
	'SlowDrip',
	'AuthExpiry',
	'DnsFailure'
];

export const isStateChanging = (method: string) => ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method);

export type Field =
	| { key: string; label: string; kind: 'number'; min: number; max: number; step?: number; unit?: string; hint?: string }
	| { key: string; label: string; kind: 'choice'; choices: [string, string][]; hint?: string };

export const parameterFields: Record<ScenarioType, Field[]> = {
	Baseline: [],
	Latency: [{ key: 'delayMs', label: 'Added delay', kind: 'number', min: 0, max: 120000, unit: 'ms' }],
	ServerErrorStorm: [
		{ key: 'status', label: 'Status code', kind: 'number', min: 500, max: 599 },
		{ key: 'count', label: 'Failures in a row', kind: 'number', min: 1, max: 20 },
		{ key: 'probability', label: 'Probability', kind: 'number', min: 0, max: 1, step: 0.1, hint: '1.0 fails every attempt.' }
	],
	Throttle429: [{ key: 'retryAfterSeconds', label: 'Retry-After', kind: 'number', min: 0, max: 3600, unit: 's' }],
	TimeoutDrop: [{ key: 'mode', label: 'Behavior', kind: 'choice', choices: [['drop', 'Drop connection'], ['hold', 'Hold until timeout']] }],
	MalformedJson: [
		{ key: 'variant', label: 'Corruption', kind: 'choice', choices: [['truncated', 'Truncated body'], ['invalidSyntax', 'Invalid syntax'], ['wrongTypes', 'Wrong field types']] }
	],
	EmptyResponse: [],
	SlowDrip: [
		{ key: 'bytes', label: 'Bytes per chunk', kind: 'number', min: 1, max: 65536, unit: 'B' },
		{ key: 'intervalMs', label: 'Interval', kind: 'number', min: 1, max: 10000, unit: 'ms' }
	],
	AuthExpiry: [
		{ key: 'credential', label: 'Credential', kind: 'choice', choices: [['expired', 'Expired token'], ['missing', 'Missing credentials'], ['invalid', 'Invalid token'], ['forbidden', 'Forbidden (403)']] }
	],
	DnsFailure: [
		{ key: 'dependency', label: 'Dependency', kind: 'choice', choices: [['unreachable', 'Processor unreachable'], ['unresolvable', 'Host unresolvable']] }
	],
	DuplicateRequest: [
		{ key: 'copies', label: 'Copies sent', kind: 'number', min: 2, max: 10 },
		{ key: 'key', label: 'Idempotency key', kind: 'choice', choices: [['same', 'Same key on every copy'], ['different', 'Different key per copy'], ['none', 'No key']] },
		{ key: 'delayMs', label: 'Delay between copies', kind: 'number', min: 0, max: 60000, unit: 'ms', hint: '0 ms sends the copies back to back.' }
	]
};

const choice = (type: ScenarioType, key: string, value: unknown) => {
	const field = parameterFields[type].find((f) => f.key === key);
	return field?.kind === 'choice' ? (field.choices.find(([v]) => v === value)?.[1] ?? String(value)) : String(value);
};

// Short table summary, e.g. "503 × 3, p=1.0" or "2 copies, same key".
export function describeParameters(type: ScenarioType, p: Record<string, string | number>): string {
	switch (type) {
		case 'Latency':
			return `+${p.delayMs} ms`;
		case 'ServerErrorStorm':
			return `${p.status} × ${p.count}, p=${Number(p.probability).toFixed(1)}`;
		case 'Throttle429':
			return `Retry-After: ${p.retryAfterSeconds}`;
		case 'EmptyResponse':
			return 'Empty body';
		case 'SlowDrip':
			return `${p.bytes} B / ${p.intervalMs} ms`;
		case 'DuplicateRequest':
			return `${p.copies} copies, ${{ same: 'same key', different: 'different keys', none: 'no key' }[p.key as string] ?? p.key}`;
		case 'Baseline':
			return '';
		default: {
			const [key] = Object.keys(p);
			return key ? choice(type, key, p[key]) : '';
		}
	}
}

export const invariantLabels: Record<InvariantType, string> = {
	StatusCodeIn: 'Status code in set',
	FieldEquals: 'Field equals',
	FieldNotBelow: 'Field not below',
	MaxRecordsAfterDuplicates: 'Record count after duplicates ≤ N',
	MaxDurationMs: 'Maximum duration (ms)',
	HeaderPresent: 'Required header present'
};

// [title, detail] for an invariant card.
export function describeInvariant(invariant: Invariant): [string, string] {
	const p = invariant.params;
	const readBack = invariant.readBack ? `Read-back: GET ${invariant.readBack.path}` : '';
	const detail = (text: string) => [text, readBack].filter(Boolean).join(' · ');
	switch (invariant.type) {
		case 'StatusCodeIn':
			return ['Status code in set', detail(p.codes?.join(', ') ?? '')];
		case 'FieldEquals':
			return ['Field equals', detail(`${p.path} = ${p.value}`)];
		case 'FieldNotBelow':
			return ['Field not below', detail(`${p.path} ≥ ${p.min}`)];
		case 'MaxRecordsAfterDuplicates':
			return [`Record count after duplicates ≤ ${p.max}`, readBack];
		case 'MaxDurationMs':
			return ['Maximum duration', detail(`${p.ms} ms`)];
		case 'HeaderPresent':
			return ['Required header present', detail(p.name ?? '')];
	}
}
