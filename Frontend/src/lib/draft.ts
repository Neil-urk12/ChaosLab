// "Draft from schema" for the test-values body editor (PROJECT.md 26): a value that follows a JSON schema, for the
// user to review and save. Never sent until saved. Fills required properties only; each value is the schema's
// example, then enum[0], then default, then a placeholder for its format, then one for its type.
type Schema = {
	$ref?: string;
	type?: string | string[];
	format?: string;
	example?: unknown;
	enum?: unknown[];
	default?: unknown;
	properties?: Record<string, Schema>;
	required?: string[];
	items?: Schema;
	minItems?: number;
	allOf?: Schema[];
	oneOf?: Schema[];
	anyOf?: Schema[];
	nullable?: boolean;
};

const FORMATS: Record<string, unknown> = {
	uuid: '00000000-0000-4000-8000-000000000000',
	'date-time': '2026-01-01T00:00:00Z',
	date: '2026-01-01',
	time: '00:00:00',
	email: 'user@example.com',
	uri: 'https://example.com',
	url: 'https://example.com',
	hostname: 'example.com',
	ipv4: '192.0.2.1',
	ipv6: '2001:db8::1'
};

// schemas: components.schemas, which "#/components/schemas/Name" refers to.
export function draftFromSchema(schema: unknown, schemas: Record<string, unknown> | null = null): unknown {
	return draft(schema as Schema, schemas ?? {}, []);
}

function draft(schema: Schema | undefined, schemas: Record<string, unknown>, seen: string[]): unknown {
	if (!schema || typeof schema !== 'object') return null;
	if (schema.$ref) {
		const name = schema.$ref.startsWith('#/components/schemas/') ? schema.$ref.split('/').pop()! : null;
		// A schema that contains itself stops at null instead of recursing forever.
		if (!name || seen.includes(name) || !(name in schemas)) return null;
		return draft(schemas[name] as Schema, schemas, [...seen, name]);
	}
	if (schema.example !== undefined) return schema.example;
	if (schema.enum?.length) return schema.enum[0];
	if (schema.default !== undefined) return schema.default;
	if (schema.allOf?.length) {
		const parts = schema.allOf.map((part) => draft(part, schemas, seen));
		if (parts.every((part) => part !== null && typeof part === 'object' && !Array.isArray(part)))
			return Object.assign({}, ...parts);
		return parts.find((part) => part !== null) ?? null;
	}
	if (schema.oneOf?.length) return draft(schema.oneOf[0], schemas, seen);
	if (schema.anyOf?.length) return draft(schema.anyOf[0], schemas, seen);
	if (schema.format && schema.format in FORMATS) return FORMATS[schema.format];

	// OpenAPI 3.1 allows a list of types; the first non-null one decides.
	const type = Array.isArray(schema.type) ? schema.type.find((t) => t !== 'null') : schema.type;
	switch (type ?? (schema.properties ? 'object' : schema.items ? 'array' : undefined)) {
		case 'object':
			return Object.fromEntries(
				(schema.required ?? []).map((name) => [name, draft(schema.properties?.[name], schemas, seen)])
			);
		case 'array':
			return (schema.minItems ?? 0) > 0 ? [draft(schema.items, schemas, seen)] : [];
		case 'string':
			return 'string';
		case 'integer':
		case 'number':
			return 0;
		case 'boolean':
			return false;
		default:
			return null;
	}
}
