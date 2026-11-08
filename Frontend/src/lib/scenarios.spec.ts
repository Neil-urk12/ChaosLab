import { describe, expect, it } from 'vitest';
import { describeInvariant, describeParameters } from './scenarios';

describe('scenarios', () => {
	it('summarizes parameters for the table', () => {
		expect(describeParameters('Latency', { delayMs: 3000 })).toBe('+3000 ms');
		expect(describeParameters('ServerErrorStorm', { status: 503, count: 3, probability: 1 })).toBe('503 × 3, p=1.0');
		expect(describeParameters('DuplicateRequest', { copies: 2, key: 'same', delayMs: 0 })).toBe('2 copies, same key');
		expect(describeParameters('MalformedJson', { variant: 'truncated' })).toBe('Truncated body');
		expect(describeParameters('AuthExpiry', { credential: 'expired' })).toBe('Expired token');
		expect(describeParameters('EmptyResponse', {})).toBe('Empty body');
	});

	it('describes invariants with their read-back request', () => {
		expect(
			describeInvariant({
				type: 'MaxRecordsAfterDuplicates',
				params: { max: 1 },
				readBack: { method: 'GET', path: '/payments?reference={{request.body.reference}}' }
			})
		).toEqual(['Record count after duplicates ≤ 1', 'Read-back: GET /payments?reference={{request.body.reference}}']);
		expect(describeInvariant({ type: 'StatusCodeIn', params: { codes: [201, 409] } })).toEqual(['Status code in set', '201, 409']);
	});
});
