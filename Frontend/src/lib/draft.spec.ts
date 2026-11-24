import { describe, expect, it } from 'vitest';
import { draftFromSchema } from './draft';

describe('draftFromSchema', () => {
	it('fills required properties only, resolving component refs', () => {
		const schemas = {
			VehicleCreate: {
				type: 'object',
				required: ['plate', 'status', 'owner', 'seats', 'electric', 'tags'],
				properties: {
					plate: { type: 'string' },
					status: { type: 'string', enum: ['active', 'retired'], default: 'retired' },
					owner: { $ref: '#/components/schemas/Owner' },
					seats: { type: 'integer', default: 4 },
					electric: { type: 'boolean' },
					tags: { type: 'array', items: { type: 'string' } },
					nickname: { type: 'string' }
				}
			},
			Owner: {
				type: 'object',
				required: ['id', 'email', 'since'],
				properties: {
					id: { type: 'string', format: 'uuid' },
					email: { type: 'string', format: 'email' },
					since: { type: 'string', format: 'date-time' }
				}
			}
		};
		expect(draftFromSchema({ $ref: '#/components/schemas/VehicleCreate' }, schemas)).toEqual({
			plate: 'string',
			status: 'active',
			owner: { id: '00000000-0000-4000-8000-000000000000', email: 'user@example.com', since: '2026-01-01T00:00:00Z' },
			seats: 4,
			electric: false,
			tags: []
		});
	});

	it('uses examples, merges allOf, takes the first oneOf and fills arrays that need an item', () => {
		expect(draftFromSchema({ type: 'string', example: 'ABC-123' })).toBe('ABC-123');
		expect(
			draftFromSchema({
				allOf: [
					{ type: 'object', required: ['a'], properties: { a: { type: 'number' } } },
					{ type: 'object', required: ['b'], properties: { b: { type: ['null', 'string'] } } }
				]
			})
		).toEqual({ a: 0, b: 'string' });
		expect(draftFromSchema({ oneOf: [{ type: 'integer' }, { type: 'string' }] })).toBe(0);
		expect(draftFromSchema({ type: 'array', minItems: 1, items: { type: 'integer' } })).toEqual([0]);
	});

	it('stops at a schema that refers to itself and at refs it cannot resolve', () => {
		const schemas = {
			Node: { type: 'object', required: ['child'], properties: { child: { $ref: '#/components/schemas/Node' } } }
		};
		expect(draftFromSchema({ $ref: '#/components/schemas/Node' }, schemas)).toEqual({ child: null });
		expect(draftFromSchema({ $ref: '#/components/schemas/Missing' }, schemas)).toBeNull();
		expect(draftFromSchema(undefined)).toBeNull();
	});
});
