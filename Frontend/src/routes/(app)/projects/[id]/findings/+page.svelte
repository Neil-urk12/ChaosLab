<script lang="ts">
	import { goto } from '$app/navigation';
	import type { FindingOutcome, RunFinding, Severity } from '$lib/api';
	import { dateTime } from '$lib/format';
	import { findingCategory, findingDetail, findingsCsv, outcomeLabels, SEVERITIES } from '$lib/findings';
	import { runLabel } from '$lib/runs';

	let { data } = $props();
	const project = $derived(data.project);
	const result = $derived(data.findings);
	const run = $derived(result.run);
	const findings = $derived(result.findings);
	const latestCompleted = $derived(result.runs.find((r) => r.status === 'Completed'));
	// A run opened by link can be older than the picker's list; keep it selectable so the picker shows it.
	const pickerRuns = $derived(run && !result.runs.some((r) => r.id === run.id) ? [...result.runs, run] : result.runs);

	const count = (outcome: FindingOutcome) => findings.filter((f) => f.outcome === outcome).length;
	const failed = $derived(findings.filter((f) => f.outcome === 'Failed'));
	const failNote = $derived(
		(['Critical', 'High'] as const)
			.map((s) => [failed.filter((f) => f.severity === s).length, s.toLowerCase()] as const)
			.filter(([n]) => n)
			.map(([n, s]) => `${n} ${s}`)
			.join(' · ')
	);
	const scored = $derived(count('Passed') + count('Degraded') + count('Failed'));
	const stats = $derived([
		{ label: 'Pass', value: count('Passed'), tone: 'text-emerald-400', note: 'Expected behavior observed' },
		{ label: 'Degraded', value: count('Degraded'), tone: 'text-amber-400', note: 'Working, partly unacceptable' },
		{ label: 'Fail', value: failed.length, tone: 'text-rose-400', note: failNote || 'No critical or high failures' },
		{ label: 'Inconclusive', value: count('Inconclusive'), tone: '', note: 'Not enough evidence · excluded from score' },
		{ label: 'Not applicable', value: count('NotApplicable'), tone: '', note: 'Excluded from score' }
	]);

	const outcomeStyles: Record<FindingOutcome, string> = {
		Passed: 'bg-emerald-500/10 text-emerald-300',
		Degraded: 'bg-amber-500/10 text-amber-300',
		Failed: 'bg-rose-500/10 text-rose-300',
		Inconclusive: 'bg-slate-500/15 text-slate-300',
		NotApplicable: 'bg-slate-500/15 text-slate-400'
	};
	const severityStyles: Record<Severity, string> = {
		Critical: 'bg-rose-500/15 text-rose-300',
		High: 'bg-amber-500/15 text-amber-300',
		Medium: 'bg-sky-500/15 text-sky-300',
		Low: 'bg-slate-500/15 text-slate-300',
		Info: 'bg-slate-500/15 text-slate-400'
	};
	const methodTone = (method: string) =>
		method === 'GET' ? 'text-sky-400' : method === 'POST' ? 'text-emerald-400' : method === 'DELETE' ? 'text-rose-400' : 'text-amber-400';

	// Filters are client-side: the server returns the whole run.
	let query = $state('');
	let outcomes = $state<FindingOutcome[]>(['Failed', 'Degraded']);
	let severity = $state('');
	let category = $state('');
	let endpoint = $state('');
	let outcomeMenu = $state<HTMLDetailsElement>();

	const categoryOptions = $derived([...new Set(findings.map(findingCategory))].sort());
	const endpointOptions = $derived([...new Set(findings.map((f) => `${f.httpMethod} ${f.path}`))].sort());
	const outcomeSummary = $derived(
		outcomes.length === 0 || outcomes.length === Object.keys(outcomeLabels).length ? 'All outcomes' : outcomes.map((o) => outcomeLabels[o]).join(', ')
	);

	const visible = $derived.by(() => {
		const text = query.trim().toLowerCase();
		return findings.filter(
			(f) =>
				(outcomes.length === 0 || outcomes.includes(f.outcome)) &&
				(!severity || f.severity === severity) &&
				(!category || findingCategory(f) === category) &&
				(!endpoint || `${f.httpMethod} ${f.path}` === endpoint) &&
				(!text || f.path.toLowerCase().includes(text) || f.summary.toLowerCase().includes(text))
		);
	});

	function toggleOutcome(outcome: FindingOutcome, on: boolean) {
		outcomes = on ? [...outcomes, outcome] : outcomes.filter((o) => o !== outcome);
	}

	// undefined: follow the first visible row; null: the panel was closed.
	let selectedId = $state<string | null | undefined>(undefined);
	const selected = $derived(selectedId === null ? null : (visible.find((f) => f.id === selectedId) ?? visible[0] ?? null));

	function exportCsv() {
		const url = URL.createObjectURL(new Blob([findingsCsv(visible)], { type: 'text/csv' }));
		const link = document.createElement('a');
		link.href = url;
		link.download = `${project.name}-${run ? runLabel(run) : 'findings'}.csv`.replace(/[^\w.-]+/g, '-');
		link.click();
		URL.revokeObjectURL(url);
	}

	const selectClass =
		'h-10 w-full rounded-md border border-line bg-panel pr-9 text-[13px] text-slate-200 focus:border-accent focus:ring-1 focus:ring-accent';
	const checkboxClass = 'size-4 rounded border-line-strong bg-field text-accent focus:ring-accent focus:ring-offset-0';
	const chip = 'inline-flex items-center gap-1.5 rounded px-2 py-1 text-[11px] whitespace-nowrap';
</script>

<svelte:window onclick={(e) => outcomeMenu?.open && !outcomeMenu.contains(e.target as Node) && (outcomeMenu.open = false)} />

<svelte:head><title>Findings · {project.name} · Chaos Lab</title></svelte:head>

{#snippet outcomeChip(outcome: FindingOutcome)}
	<span class="{chip} {outcomeStyles[outcome]}"><span class="size-1.5 rounded-full bg-current" aria-hidden="true"></span>{outcomeLabels[outcome]}</span>
{/snippet}

<div class="mt-8 flex flex-wrap items-start justify-between gap-4">
	<div>
		<h2 class="text-lg font-semibold">Findings</h2>
		<p class="mt-1 text-xs text-slate-400">
			{#if run}
				{[`From ${runLabel(run)}`, run.completedAt && dateTime(run.completedAt), `${scored} of ${findings.length} finding${findings.length === 1 ? '' : 's'} scored`].filter(Boolean).join(' · ')}
			{:else}
				No run has findings yet.
			{/if}
		</p>
	</div>
	{#if run}
		<div class="flex flex-wrap gap-3">
			<label class="min-w-60">
				<span class="sr-only">Test run</span>
				<select value={run.id} onchange={(e) => goto(`?run=${e.currentTarget.value}`, { keepFocus: true, noScroll: true })} class={selectClass}>
					{#each pickerRuns as option (option.id)}
						<option value={option.id}>
							{runLabel(option)}{option.id === latestCompleted?.id ? ' (latest completed)' : option.status === 'Completed' ? '' : ` (${option.status})`}
						</option>
					{/each}
				</select>
			</label>
			<button type="button" onclick={exportCsv} disabled={!visible.length} class="flex h-10 items-center gap-2 rounded-md border border-line-strong px-4 text-[13px] text-slate-200 hover:bg-panel disabled:opacity-50">
				<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11m-4-4 4 4 4-4M5 19h14" /></svg>
				Export CSV
			</button>
		</div>
	{/if}
</div>

{#if run}
	<dl class="mt-6 grid grid-cols-2 overflow-hidden rounded-lg border border-line sm:grid-cols-3 lg:grid-cols-5">
		{#each stats as stat (stat.label)}
			<div class="border-line-faint px-5 py-4 not-last:border-r max-lg:border-b">
				<dt class="text-xs text-slate-400">{stat.label}</dt>
				<dd class="mt-2 text-3xl font-bold {stat.tone}">{stat.value}</dd>
				<dd class="mt-2 text-[11px] text-slate-500">{stat.note}</dd>
			</div>
		{/each}
	</dl>

	<div class="mt-6 flex flex-wrap gap-3">
		<label class="relative min-w-60 flex-[2]">
			<span class="sr-only">Search findings</span>
			<svg viewBox="0 0 24 24" class="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-slate-500" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="6" /><path d="M20 20l-4.5-4.5" /></svg>
			<input bind:value={query} type="search" placeholder="Search endpoint or summary" class="block h-10 w-full rounded-md border border-line bg-panel pl-9 text-[13px] text-slate-100 placeholder:text-slate-500 focus:border-accent focus:ring-1 focus:ring-accent" />
		</label>
		<details bind:this={outcomeMenu} class="relative min-w-52 flex-1">
			<summary class="flex h-10 cursor-pointer list-none items-center justify-between gap-2 rounded-md border border-line bg-panel px-3 text-[13px] text-slate-200">
				<span class="truncate">Outcome: {outcomeSummary}</span>
				<svg viewBox="0 0 24 24" class="size-4 shrink-0 text-slate-400" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="m6 9 6 6 6-6" /></svg>
			</summary>
			<fieldset class="absolute z-10 mt-1 w-full space-y-1 rounded-md border border-line bg-panel p-2 shadow-lg">
				<legend class="sr-only">Outcomes</legend>
				{#each Object.entries(outcomeLabels) as [value, label] (value)}
					<label class="flex items-center gap-2 rounded px-2 py-1.5 text-[13px] text-slate-200 hover:bg-field">
						<input type="checkbox" checked={outcomes.includes(value as FindingOutcome)} onchange={(e) => toggleOutcome(value as FindingOutcome, e.currentTarget.checked)} class={checkboxClass} />
						{label}
					</label>
				{/each}
			</fieldset>
		</details>
		<label class="min-w-44 flex-1">
			<span class="sr-only">Filter by severity</span>
			<select bind:value={severity} class={selectClass}>
				<option value="">All severities</option>
				{#each SEVERITIES as value (value)}<option {value}>{value}</option>{/each}
			</select>
		</label>
		<label class="min-w-44 flex-1">
			<span class="sr-only">Filter by category</span>
			<select bind:value={category} class={selectClass}>
				<option value="">All categories</option>
				{#each categoryOptions as value (value)}<option {value}>{value}</option>{/each}
			</select>
		</label>
		<label class="min-w-44 flex-1">
			<span class="sr-only">Filter by endpoint</span>
			<select bind:value={endpoint} class={selectClass}>
				<option value="">All endpoints</option>
				{#each endpointOptions as value (value)}<option {value}>{value}</option>{/each}
			</select>
		</label>
	</div>

	<div class="mt-6 grid items-start gap-6 {selected ? 'xl:grid-cols-[minmax(0,1fr)_28rem]' : ''}">
		{#if visible.length}
			<div class="overflow-hidden rounded-lg border border-line">
				<div class="overflow-x-auto">
					<table class="w-full min-w-[44rem] text-left text-[13px]">
						<thead class="border-b border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
							<tr>
								{#each ['Severity', 'Outcome', 'Endpoint', 'Category', 'Summary'] as heading (heading)}
									<th scope="col" class="px-4 py-4 font-semibold">{heading}</th>
								{/each}
							</tr>
						</thead>
						<tbody class="divide-y divide-line-faint">
							{#each visible as f (f.id)}
								{@const current = selected?.id === f.id}
								<tr class="cursor-pointer {current ? 'bg-field' : 'hover:bg-panel'}" onclick={() => (selectedId = f.id)}>
									<td class="px-4 py-4"><span class="{chip} {severityStyles[f.severity]}">{f.severity}</span></td>
									<td class="px-4 py-4">{@render outcomeChip(f.outcome)}</td>
									<td class="px-4 py-4 whitespace-nowrap">
										<!-- The row's button gives keyboard and screen-reader users the same selection as clicking the row. -->
										<button type="button" aria-pressed={current} onclick={(e) => { e.stopPropagation(); selectedId = f.id; }} class="text-left text-slate-100 focus-visible:underline">
											<span class="mr-2 text-[11px] font-semibold {methodTone(f.httpMethod)}">{f.httpMethod}</span>{' '}{f.path}
										</button>
									</td>
									<td class="px-4 py-4 text-slate-300">{findingCategory(f)}</td>
									<td class="min-w-56 px-4 py-4">
										<p class="text-slate-100">{f.summary}</p>
										{#if findingDetail(f)}<p class="mt-0.5 text-[11px] text-slate-500">{findingDetail(f)}</p>{/if}
									</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			</div>
		{:else}
			<p class="grid min-h-40 place-items-center rounded-lg border border-dashed border-line px-4 text-center text-xs text-slate-500">
				{findings.length ? 'No findings match your search or filters.' : 'This run has no findings.'}
			</p>
		{/if}

		{#if selected}
			{@render detail(selected)}
		{/if}
	</div>
{:else}
	<p class="mt-6 grid min-h-40 place-items-center rounded-lg border border-dashed border-line px-4 text-center text-xs text-slate-500">
		Findings appear here once a test run evaluates this project's endpoints.
	</p>
{/if}

{#snippet detail(f: RunFinding)}
	{@const ev = f.evidence && typeof f.evidence === 'object' ? f.evidence : {}}
	{@const facts = Array.isArray(ev.facts) ? ev.facts : []}
	{@const notes = [['Observed', ev.interpretation?.observed], ['Suggests', ev.interpretation?.suggests], ['Not proven', ev.interpretation?.notProven]].filter(([, text]) => text)}
	<aside aria-label="Finding details" class="rounded-lg border border-line bg-panel">
		<header class="flex items-start justify-between gap-4 border-b border-line-faint px-5 py-4">
			<div class="min-w-0">
				<h3 class="text-[15px] font-semibold">{f.summary}</h3>
				<p class="mt-1 text-[11px] text-slate-400">{[`${f.httpMethod} ${f.path}`, findingDetail(f), findingCategory(f)].filter(Boolean).join(' · ')}</p>
			</div>
			<button type="button" onclick={() => (selectedId = null)} aria-label="Close finding details" class="text-slate-400 hover:text-slate-100">
				<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg>
			</button>
		</header>

		<div class="space-y-5 px-5 py-5">
			<div class="flex flex-wrap gap-2">
				{@render outcomeChip(f.outcome)}
				<span class="{chip} {severityStyles[f.severity]}">{f.severity}</span>
				{#if ev.invariantViolated}<span class="{chip} bg-rose-500/15 text-rose-300">Invariant violated</span>{/if}
			</div>

			{#if ev.expected || ev.observed}
				<div class="grid gap-3 sm:grid-cols-2">
					{#each [['Expected', ev.expected], ['Observed', ev.observed]] as [label, text] (label)}
						<div class="rounded-md border border-line p-3">
							<p class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">{label}</p>
							<p class="mt-2 text-xs leading-relaxed text-slate-200">{text ?? '—'}</p>
						</div>
					{/each}
				</div>
			{/if}

			{#if facts.length}
				<section>
					<h4 class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Evidence</h4>
					<dl class="mt-2 space-y-1.5 rounded-md border border-line p-3 text-[11px]">
						{#each facts as fact, i (i)}
							<div class="flex justify-between gap-4">
								<dt class="text-slate-400">{fact.label}</dt>
								<dd class="text-right font-mono text-slate-200">{fact.value}</dd>
							</div>
						{/each}
					</dl>
				</section>
			{/if}

			{#if notes.length}
				<section>
					<h4 class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Interpretation</h4>
					<dl class="mt-2 grid grid-cols-[6rem_1fr] gap-x-4 gap-y-2 text-[11px]">
						{#each notes as [label, text] (label)}
							<dt class="text-slate-400">{label}</dt>
							<dd class={label === 'Not proven' ? 'text-slate-500' : 'text-slate-200'}>{text}</dd>
						{/each}
					</dl>
				</section>
			{/if}

			{#if f.recommendation}
				<section class="flex gap-3 rounded-md border border-line p-4">
					<svg viewBox="0 0 24 24" class="mt-0.5 size-4 shrink-0 text-accent-ink" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><path d="M9 18h6M10 21h4M12 3a6 6 0 0 0-3.5 10.9V16h7v-2.1A6 6 0 0 0 12 3Z" /></svg>
					<div>
						<h4 class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Recommendation</h4>
						<p class="mt-1 text-[13px] font-semibold">{f.recommendation.title}</p>
						<p class="mt-1 text-xs leading-relaxed text-slate-300">{f.recommendation.text}</p>
					</div>
				</section>
			{/if}

			<div class="flex flex-wrap justify-end gap-3">
				<button type="button" disabled title="The failure timeline is not available yet" class="h-9 rounded-md border border-line-strong px-4 text-[13px] text-slate-500">View in timeline</button>
				<button type="button" disabled title="Rerunning scenarios is not available yet" class="h-9 rounded-md bg-accent/50 px-4 text-[13px] font-semibold text-on-accent">Rerun scenario</button>
			</div>
		</div>
	</aside>
{/snippet}
