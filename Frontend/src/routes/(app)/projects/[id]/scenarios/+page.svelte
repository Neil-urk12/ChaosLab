<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import Toggle from '$lib/components/Toggle.svelte';
	import type { Invariant, InvariantType, Scenario, ScenarioType } from '$lib/api';
	import {
		categories,
		describeInvariant,
		describeParameters,
		faultLabels,
		invariantLabels,
		isStateChanging,
		parameterFields,
		PRIMITIVES
	} from '$lib/scenarios';

	let { data } = $props();
	const project = $derived(data.project);
	const api = $derived(`/api/projects/${project.id}/scenarios`);
	// Viewers get a read-only page; the backend answers 403 regardless.
	const myRole = $derived(project.members.find((m) => m.userId === data.me.id)?.role);
	const canEdit = $derived(myRole === 'Owner' || myRole === 'Editor');

	// Optimistic edits replace items in this list until the next load.
	let scenarios = $derived<Scenario[]>(data.list?.scenarios ?? []);
	// Import doesn't generate; the user starts generation here. Every included endpoint gets at least one scenario.
	const generated = $derived(scenarios.length > 0);

	const icon = {
		bolt: '<path d="M13 3L5 14h6l-1 7 8-11h-6z"/>',
		search: '<circle cx="11" cy="11" r="6"/><path d="M20 20l-4.5-4.5"/>',
		refresh: '<path d="M20 11a8 8 0 00-14.7-4.3L4 8M4 4v4h4M4 13a8 8 0 0014.7 4.3L20 16M20 20v-4h-4"/>',
		play: '<path d="M7 5v14l11-7z"/>',
		close: '<path d="M6 6l12 12M18 6L6 18"/>',
		alert: '<path d="M12 4l9 16H3zM12 10v4M12 17h.01"/>',
		trash: '<path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13"/>',
		check: '<path d="M5 12l5 5 9-10"/>'
	};
	const methodClass = (method: string) =>
		({ GET: 'text-sky-300', POST: 'text-emerald-300', PUT: 'text-amber-300', PATCH: 'text-amber-300', DELETE: 'text-rose-400' })[method] ??
		'text-slate-400';

	// ---- Stats ----
	const stats = $derived.by(() => {
		const enabled = scenarios.filter((s) => s.enabled).length;
		const primitives = new Set(scenarios.map((s) => s.type).filter((t) => PRIMITIVES.includes(t))).size;
		return [
			{ label: 'Generated', value: String(scenarios.length), note: 'Only where the fault applies' },
			{ label: 'Enabled by default', value: String(enabled), note: `${scenarios.length - enabled} disabled` },
			{ label: 'Chaos primitives used', value: `${primitives} of ${PRIMITIVES.length}`, note: 'Plus duplicate-request scenarios' },
			{ label: 'With invariants', value: String(scenarios.filter((s) => s.invariants.length).length), note: 'Structured assertions' },
			{ label: 'State-changing', value: String(scenarios.filter((s) => isStateChanging(s.httpMethod)).length), note: 'Confirmed before each run', warn: true }
		];
	});

	// ---- Table ----
	const PAGE_SIZE = 20;
	let filter = $state<ScenarioType | 'All'>('All');
	let search = $state('');
	let limit = $state(PAGE_SIZE);
	let tableError = $state('');

	const chips = $derived(
		[...PRIMITIVES, 'DuplicateRequest' as ScenarioType]
			.map((type) => ({ type, count: scenarios.filter((s) => s.type === type).length }))
			.filter((chip) => chip.count)
	);
	const matches = $derived.by(() => {
		const query = search.trim().toLowerCase();
		return scenarios.filter(
			(s) =>
				(filter === 'All' || s.type === filter) &&
				(!query ||
					[s.path, s.operationId ?? '', faultLabels[s.type], categories[s.type], s.reason].some((text) =>
						text.toLowerCase().includes(query)
					))
		);
	});
	const shown = $derived(matches.slice(0, limit));

	function replace(id: string, changes: Partial<Scenario>) {
		scenarios = scenarios.map((s) => (s.id === id ? { ...s, ...changes } : s));
	}

	// Returns null on success, or the first validation message.
	async function patch(id: string, body: object): Promise<string | null> {
		try {
			const res = await fetch(`${api}/${id}`, {
				method: 'PATCH',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify(body)
			});
			if (res.ok) return null;
			if (res.status === 400) {
				const problem = await res.json().catch(() => null);
				return Object.values<string[]>(problem?.errors ?? {})[0]?.[0] ?? 'Check the values and try again.';
			}
			if (res.status === 403) return 'Only project owners and editors can change scenarios.';
			return 'Saving failed. Try again.';
		} catch {
			return 'Could not reach the server.';
		}
	}

	async function setEnabled(scenario: Scenario, enabled: boolean) {
		tableError = '';
		replace(scenario.id, { enabled });
		const error = await patch(scenario.id, { enabled });
		if (error) {
			replace(scenario.id, { enabled: !enabled });
			tableError = error;
		}
	}

	let regenerating = $state(false);
	let regenerated = $state('');
	async function regenerate() {
		const first = !generated;
		regenerating = true;
		tableError = regenerated = '';
		try {
			const res = await fetch(`${api}/generate`, { method: 'POST' });
			if (!res.ok) throw new Error(String(res.status));
			const { added, removed } = await res.json();
			await invalidateAll();
			regenerated = first
				? `Generated ${added} scenario${added === 1 ? '' : 's'}.`
				: added || removed
					? `Added ${added}, removed ${removed}. Your edits were kept.`
					: 'Already up to date.';
		} catch {
			tableError = first ? 'Generating failed. Try again.' : 'Regenerating failed. Try again.';
		} finally {
			regenerating = false;
		}
	}

	// ---- Detail ----
	let selectedId = $state<string | null>(null);
	const selected = $derived(scenarios.find((s) => s.id === selectedId) ?? null);
	let draft = $state<Record<string, string | number>>({});
	let detailError = $state('');
	let adding = $state(false);

	function select(scenario: Scenario) {
		selectedId = scenario.id;
		draft = { ...scenario.parameters };
		detailError = '';
		adding = false;
	}

	async function saveParameters() {
		if (!selected) return;
		const parameters = Object.fromEntries(
			parameterFields[selected.type].map((f) => [f.key, f.kind === 'number' ? Number(draft[f.key]) : draft[f.key]])
		);
		detailError = (await patch(selected.id, { parameters })) ?? '';
		if (!detailError) replace(selected.id, { parameters });
	}

	async function saveInvariants(scenario: Scenario, invariants: Invariant[]) {
		detailError = (await patch(scenario.id, { invariants })) ?? '';
		if (!detailError) replace(scenario.id, { invariants });
		return !detailError;
	}

	// Add-invariant form; only the fields of the chosen type are sent.
	let invType = $state<InvariantType>('MaxDurationMs');
	let inv = $state({ codes: '', path: '', value: '', min: '', max: '1', ms: '2000', name: '' });
	let readBackOn = $state(false);
	let readBackPath = $state('');
	const invTypes = $derived(
		(Object.keys(invariantLabels) as InvariantType[]).filter(
			(t) => t !== 'MaxRecordsAfterDuplicates' || selected?.type === 'DuplicateRequest'
		)
	);
	const needsReadBack = $derived(invType === 'MaxRecordsAfterDuplicates');

	function startAdding() {
		invType = selected?.type === 'DuplicateRequest' ? 'MaxRecordsAfterDuplicates' : 'MaxDurationMs';
		inv = { codes: '', path: '', value: '', min: '', max: '1', ms: '2000', name: '' };
		readBackOn = false;
		readBackPath = '';
		detailError = '';
		adding = true;
	}

	async function addInvariant() {
		if (!selected) return;
		const params: Invariant['params'] = {
			StatusCodeIn: { codes: inv.codes.split(/[\s,]+/).filter(Boolean).map(Number) },
			FieldEquals: { path: inv.path.trim(), value: inv.value },
			FieldNotBelow: { path: inv.path.trim(), min: Number(inv.min) },
			MaxRecordsAfterDuplicates: { max: Number(inv.max) },
			MaxDurationMs: { ms: Number(inv.ms) },
			HeaderPresent: { name: inv.name.trim() }
		}[invType];
		const invariant: Invariant = { type: invType, params };
		if (readBackOn || needsReadBack) invariant.readBack = { method: 'GET', path: readBackPath.trim() };
		if (await saveInvariants(selected, [...selected.invariants, invariant])) adding = false;
	}

	const invariantHint: Record<InvariantType, string> = {
		StatusCodeIn: 'Fails unless the final response has one of these codes.',
		FieldEquals: 'Fails unless the response field has this value.',
		FieldNotBelow: 'Fails if the response field is below the minimum.',
		MaxRecordsAfterDuplicates: 'Counts the records the read-back request returns.',
		MaxDurationMs: 'Fails if the operation takes longer than this, including retries.',
		HeaderPresent: 'Fails unless the response includes this header.'
	};
</script>

<svelte:head><title>Scenarios · {project.name} · Chaos Lab</title></svelte:head>

{#snippet svg(path: string, cls = 'size-4')}
	<svg viewBox="0 0 24 24" class={cls} fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{@html path}</svg>
{/snippet}

<div class="mt-8 flex flex-wrap items-end justify-between gap-4">
	<div>
		<h2 class="text-xl font-bold">Scenarios</h2>
		<p class="mt-1 text-[13px] text-slate-400">
			{#if data.list && !generated}
				Generate scenarios to see which faults apply to the {data.list.endpointCount} included endpoint{data.list.endpointCount === 1 ? '' : 's'}.
			{:else if data.list}
				{scenarios.length} applicable scenario{scenarios.length === 1 ? '' : 's'} generated from {data.list.endpointCount} included endpoint{data.list.endpointCount === 1 ? '' : 's'}. Defaults apply to future runs.
			{:else}
				Scenarios are generated from the imported OpenAPI specification.
			{/if}
		</p>
	</div>
	{#if data.list}
		<div class="flex gap-3">
			{#if canEdit && generated}
				<button type="button" onclick={regenerate} disabled={regenerating} class="flex h-9 items-center gap-2 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint disabled:opacity-60">
					{@render svg(icon.refresh)}{regenerating ? 'Regenerating…' : 'Regenerate'}
				</button>
			{/if}
			<button type="button" disabled title="Running tests is not available yet" class="flex h-9 items-center gap-2 rounded-md bg-accent/50 px-4 text-[13px] font-semibold text-on-accent">
				{@render svg(icon.play)}New test run
			</button>
		</div>
	{/if}
</div>

{#if !data.list}
	<p class="mt-6 grid min-h-40 place-items-center rounded-lg border border-dashed border-line px-4 text-center text-[13px] text-slate-500">
		<span>No specification yet. <a href="/projects/{project.id}/spec" class="text-accent-ink hover:text-accent-ink-strong">Import an OpenAPI document</a> to generate scenarios.</span>
	</p>
{:else if !generated && data.list.endpointCount}
	<div class="mt-6 grid min-h-40 place-items-center gap-4 rounded-lg border border-dashed border-line px-4 py-8 text-center text-[13px] text-slate-400">
		<p class="max-w-md">
			No scenarios yet. Generating checks each included endpoint's method, responses, security and idempotency, and adds a
			scenario only for faults that apply. You can turn them on or off and adjust them afterwards.
		</p>
		{#if canEdit}
			<button type="button" onclick={regenerate} disabled={regenerating} class="flex h-9 items-center gap-2 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover disabled:opacity-60">
				{@render svg(icon.bolt)}{regenerating ? 'Generating…' : 'Generate scenarios'}
			</button>
		{:else}
			<p class="text-slate-500">A project owner or editor can generate them.</p>
		{/if}
		{#if tableError}<p class="text-rose-300" role="alert">{tableError}</p>{/if}
	</div>
{:else}
	<dl class="mt-6 grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-line bg-line lg:grid-cols-5">
		{#each stats as stat (stat.label)}
			<div class="bg-canvas p-5 first:col-span-2 lg:first:col-span-1">
				<dt class="text-xs text-slate-400">{stat.label}</dt>
				<dd class="mt-3 text-2xl font-semibold {stat.warn && stat.value !== '0' ? 'text-amber-300' : ''}">{stat.value}</dd>
				<dd class="mt-2 text-[11px] text-slate-500">{stat.note}</dd>
			</div>
		{/each}
	</dl>

	{#if regenerated}<p class="mt-4 text-[13px] text-emerald-300" role="status">{regenerated}</p>{/if}

	<div class="mt-6 grid items-start gap-6 {selected ? 'xl:grid-cols-[minmax(0,1fr)_26rem]' : ''}">
		<section class="min-w-0 rounded-lg border border-line">
			<div class="flex flex-wrap items-start justify-between gap-4 p-6 pb-4">
				<div>
					<h3 class="flex items-center gap-2 text-[15px] font-semibold">{@render svg(icon.bolt, 'size-4 text-accent-ink')}Generated scenarios</h3>
					<p class="mt-1 text-xs text-slate-500">Each scenario records why it was generated and what it evaluates.</p>
				</div>
				<div class="relative w-full sm:w-64">
					<span class="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-slate-500">{@render svg(icon.search)}</span>
					<input type="search" bind:value={search} oninput={() => (limit = PAGE_SIZE)} placeholder="Search scenarios" aria-label="Search scenarios" class="auth-input h-9 pl-9" />
				</div>
			</div>

			<div class="flex flex-wrap gap-2 border-y border-line-faint px-6 py-4" role="group" aria-label="Filter by fault">
				{#each [{ type: 'All' as const, count: scenarios.length }, ...chips] as chip (chip.type)}
					<button
						type="button"
						aria-pressed={filter === chip.type}
						onclick={() => ((filter = chip.type), (limit = PAGE_SIZE))}
						class="rounded-full border px-3 py-1 text-[11px] {filter === chip.type ? 'border-accent bg-accent-tint text-accent-ink-strong' : 'border-line-strong text-slate-300 hover:bg-line-faint'}"
					>
						{chip.type === 'All' ? 'All' : faultLabels[chip.type]}<span class="ml-2 text-slate-500">{chip.count}</span>
					</button>
				{/each}
			</div>

			{#if tableError}<p class="px-6 pt-4 text-[13px] text-rose-300" role="alert">{tableError}</p>{/if}

			<div class="overflow-x-auto">
				<table class="w-full min-w-[56rem] text-left text-[13px]">
					<thead class="border-b border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
						<tr>
							{#each ['On', 'Endpoint', 'Fault', 'Parameters', 'Category', 'Why generated', 'Inv.'] as heading (heading)}
								<th scope="col" class="px-4 py-3 font-semibold first:pl-6 last:pr-6 last:text-right">{heading}</th>
							{/each}
						</tr>
					</thead>
					<tbody class="divide-y divide-line-faint">
						{#each shown as scenario (scenario.id)}
							<tr onclick={() => select(scenario)} class="cursor-pointer hover:bg-line-faint/50 {scenario.id === selectedId ? 'bg-accent-tint/40' : ''}">
								<td class="py-4 pr-4 pl-6">
									<Toggle checked={scenario.enabled} disabled={!canEdit} label="Enable {faultLabels[scenario.type]} on {scenario.httpMethod} {scenario.path}" onchange={(on) => setEnabled(scenario, on)} />
								</td>
								<td class="px-4 py-4">
									<button type="button" onclick={(e) => (e.stopPropagation(), select(scenario))} class="flex items-center gap-3 text-left">
										<span class="text-[11px] font-semibold {methodClass(scenario.httpMethod)}">{scenario.httpMethod}</span>
										<span class="font-mono text-xs">{scenario.path}</span>
									</button>
								</td>
								<td class="px-4 py-4 text-slate-300">{faultLabels[scenario.type]}</td>
								<td class="px-4 py-4 text-slate-300">{describeParameters(scenario.type, scenario.parameters)}</td>
								<td class="px-4 py-4 text-slate-300">{categories[scenario.type]}</td>
								<td class="max-w-72 px-4 py-4 text-xs text-slate-300">{scenario.reason}</td>
								<td class="py-4 pr-6 pl-4 text-right">
									{#if scenario.invariants.length}
										<span class="rounded bg-accent-tint px-2 py-0.5 text-[11px] text-accent-ink-strong">{scenario.invariants.length}</span>
									{:else}
										<span class="text-slate-600">—</span>
									{/if}
								</td>
							</tr>
						{:else}
							<tr><td colspan="7" class="px-6 py-10 text-center text-xs text-slate-500">
								{scenarios.length ? 'No scenarios match.' : 'No scenarios. Include endpoints on the API specification tab, then regenerate.'}
							</td></tr>
						{/each}
					</tbody>
				</table>
			</div>

			<div class="flex flex-wrap items-center justify-between gap-4 border-t border-line-faint px-6 py-4 text-[11px] text-slate-500">
				<span>Showing {shown.length} of {matches.length} · Duplicate request is a behavioral scenario, not a chaos primitive.</span>
				{#if shown.length < matches.length}
					<button type="button" onclick={() => (limit += PAGE_SIZE)} class="text-accent-ink hover:text-accent-ink-strong">Load more</button>
				{/if}
			</div>
		</section>

		{#if selected}
			{@const fields = parameterFields[selected.type]}
			<aside class="rounded-lg border border-line" aria-label="Scenario details">
				<div class="flex items-start justify-between gap-4 border-b border-line-faint p-6">
					<div class="min-w-0">
						<h3 class="text-[15px] font-semibold">{faultLabels[selected.type]}</h3>
						<p class="mt-1 truncate text-xs text-slate-500">{selected.httpMethod} {selected.path} · {categories[selected.type]}</p>
					</div>
					<button type="button" onclick={() => (selectedId = null)} aria-label="Close details" class="text-slate-400 hover:text-slate-100">{@render svg(icon.close)}</button>
				</div>

				<div class="space-y-6 p-6">
					<div>
						<p class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Why generated</p>
						<p class="mt-3 text-[13px] text-slate-300">{selected.reason}</p>
					</div>

					{#if fields.length}
						<form class="grid grid-cols-2 gap-4" onchange={saveParameters} onsubmit={(e) => (e.preventDefault(), saveParameters())}>
							{#each fields as field (field.key)}
								<label class="block text-xs text-slate-400 {fields.length === 3 && field === fields[2] ? 'col-span-2' : ''} {fields.length === 1 ? 'col-span-2' : ''}">
									{field.label}{#if field.kind === 'number' && field.unit}<span class="text-slate-500"> ({field.unit})</span>{/if}
									{#if field.kind === 'number'}
										<input type="number" bind:value={draft[field.key]} min={field.min} max={field.max} step={field.step ?? 1} required disabled={!canEdit} class="auth-input mt-2" />
									{:else}
										<select bind:value={draft[field.key]} disabled={!canEdit} class="auth-input mt-2">
											{#each field.choices as [value, label] (value)}<option {value}>{label}</option>{/each}
										</select>
									{/if}
									{#if field.hint}<span class="mt-2 block text-[11px] text-slate-500">{field.hint}</span>{/if}
								</label>
							{/each}
						</form>
					{/if}

					{#if isStateChanging(selected.httpMethod)}
						<p class="flex gap-3 rounded-md border border-amber-400/30 bg-amber-500/10 p-4 text-xs text-amber-200">
							<span class="text-amber-300">{@render svg(icon.alert)}</span>
							State-changing. Runs that include this scenario ask for confirmation first. Use test data only.
						</p>
					{/if}

					{#if detailError}<p class="text-[13px] text-rose-300" role="alert">{detailError}</p>{/if}

					<div class="border-t border-line-faint pt-6">
						<p class="flex justify-between gap-4 text-[10px] font-semibold tracking-wider text-slate-500 uppercase">
							Invariants ({selected.invariants.length})
							<span class="font-normal tracking-normal normal-case">A violated invariant makes the finding Fail</span>
						</p>
						<ul class="mt-4 space-y-3">
							{#each selected.invariants as invariant, i (i)}
								{@const [title, detail] = describeInvariant(invariant)}
								<li class="flex items-start justify-between gap-3 rounded-md border border-line p-4">
									<div class="min-w-0">
										<p class="text-[13px]">{title}</p>
										{#if detail}<p class="mt-1 text-[11px] break-all text-slate-500">{detail}</p>{/if}
									</div>
									{#if canEdit}
										<button type="button" onclick={() => saveInvariants(selected, selected.invariants.filter((_, j) => j !== i))} aria-label="Remove invariant {title}" class="text-slate-400 hover:text-rose-300">{@render svg(icon.trash)}</button>
									{/if}
								</li>
							{:else}
								<li class="text-xs text-slate-500">No invariants. Built-in rules still apply.</li>
							{/each}
						</ul>

						{#if canEdit && !adding}
							<button type="button" onclick={startAdding} class="mt-4 h-9 w-full rounded-md border border-dashed border-line-strong text-[13px] text-slate-300 hover:bg-line-faint">+ Add invariant</button>
						{/if}

						{#if adding}
							<form class="mt-4 space-y-4 rounded-md border border-accent/50 p-4" onsubmit={(e) => (e.preventDefault(), addInvariant())}>
								<p class="text-[13px] font-semibold">Add invariant</p>
								<label class="block text-xs text-slate-400">Assertion type
									<select bind:value={invType} class="auth-input mt-2">
										{#each invTypes as type (type)}<option value={type}>{invariantLabels[type]}</option>{/each}
									</select>
								</label>
								{#if invType === 'StatusCodeIn'}
									<label class="block text-xs text-slate-400">Status codes<input bind:value={inv.codes} required placeholder="201, 409" class="auth-input mt-2" /></label>
								{:else if invType === 'FieldEquals' || invType === 'FieldNotBelow'}
									<label class="block text-xs text-slate-400">Field path<input bind:value={inv.path} required placeholder="$.status" class="auth-input mt-2 font-mono" /></label>
									{#if invType === 'FieldEquals'}
										<label class="block text-xs text-slate-400">Value<input bind:value={inv.value} required class="auth-input mt-2" /></label>
									{:else}
										<label class="block text-xs text-slate-400">Minimum<input type="number" step="any" bind:value={inv.min} required class="auth-input mt-2" /></label>
									{/if}
								{:else if invType === 'MaxRecordsAfterDuplicates'}
									<label class="block text-xs text-slate-400">Maximum records<input type="number" min="1" max="100" bind:value={inv.max} required class="auth-input mt-2" /></label>
								{:else if invType === 'MaxDurationMs'}
									<label class="block text-xs text-slate-400">Value<input type="number" min="1" max="600000" bind:value={inv.ms} required class="auth-input mt-2" /></label>
								{:else}
									<label class="block text-xs text-slate-400">Header name<input bind:value={inv.name} required placeholder="X-Request-Id" class="auth-input mt-2" /></label>
								{/if}
								<p class="text-[11px] text-slate-500">{invariantHint[invType]}</p>

								<div class="flex items-center justify-between gap-4">
									<div>
										<p class="text-[13px]">Read-back request</p>
										<p class="text-[11px] text-slate-500">{needsReadBack ? 'Required. Returns the records to count.' : 'Optional. Verifies resulting state after the scenario.'}</p>
									</div>
									<Toggle checked={readBackOn || needsReadBack} disabled={needsReadBack} label="Read-back request" onchange={(on) => (readBackOn = on)} />
								</div>
								{#if readBackOn || needsReadBack}
									<label class="block text-xs text-slate-400">GET path
										<input bind:value={readBackPath} required pattern="/\S*" placeholder={'/payments?reference={{request.body.reference}}'} class="auth-input mt-2 font-mono" />
									</label>
								{/if}

								<div class="flex justify-end gap-3">
									<button type="button" onclick={() => ((adding = false), (detailError = ''))} class="h-9 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint">Cancel</button>
									<button type="submit" class="flex h-9 items-center gap-2 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover">{@render svg(icon.check)}Save invariant</button>
								</div>
							</form>
						{/if}
						<p class="mt-4 text-[11px] text-slate-500">
							Types: {Object.values(invariantLabels).join(' · ').toLowerCase()}
						</p>
					</div>
				</div>
			</aside>
		{/if}
	</div>
{/if}
