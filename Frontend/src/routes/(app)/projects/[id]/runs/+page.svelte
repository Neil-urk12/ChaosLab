<script lang="ts">
	import { afterNavigate, beforeNavigate, goto } from '$app/navigation';
	import { page } from '$app/state';
	import type { RunHistoryRow } from '$lib/api';
	import { dateTime, duration, PLACEHOLDER, score } from '$lib/format';
	import { canStartRuns, compareProblem, DAY_RANGES, DEFAULT_DAYS, pageWindow, runLabel, statusStyles } from '$lib/runs';

	let { data } = $props();
	const project = $derived(data.project);
	const history = $derived(data.history);
	const runs = $derived(history.runs);
	const canRun = $derived(canStartRuns(project, data.me));

	const active = (run: RunHistoryRow) => run.status === 'Queued' || run.status === 'Running';

	function note(run: RunHistoryRow) {
		if (run.status === 'Running') return 'In progress';
		if (run.status === 'Cancelled') return `Cancelled at ${run.completedScenarios} / ${run.totalScenarios}`;
		return run.status === 'Completed' ? null : run.failureMessage;
	}

	const coverageBar = (value: number) => (value >= 75 ? 'bg-emerald-400' : value >= 50 ? 'bg-amber-400' : 'bg-rose-400');

	// Viewer's time zone abbreviation for the date column, e.g. "PHT" or "GMT+8".
	const zone = new Intl.DateTimeFormat(undefined, { timeZoneName: 'short' })
		.formatToParts(new Date())
		.find((part) => part.type === 'timeZoneName')?.value;

	// The server filters and pages; the URL holds the filters (see +page.ts).
	const params = $derived(page.url.searchParams);
	const pageCount = $derived(Math.max(1, Math.ceil(history.matched / history.pageSize)));

	// Any filter change goes back to page 1.
	function hrefWith(key: string, value: string | null) {
		const next = new URLSearchParams(params);
		if (value) next.set(key, value);
		else next.delete(key);
		if (key !== 'page') next.delete('page');
		return `?${next}`;
	}
	const setFilter = (key: string, value: string | null) =>
		goto(hrefWith(key, value), { replaceState: true, keepFocus: true, noScroll: true });

	let queryText = $state(page.url.searchParams.get('query') ?? '');
	let searchTimer: ReturnType<typeof setTimeout>;
	function onSearch() {
		clearTimeout(searchTimer);
		searchTimer = setTimeout(() => setFilter('query', queryText.trim() || null), 300);
	}
	// Our own filter navigations are 'goto'; any other navigation drops a pending search and refills the box from the URL.
	beforeNavigate(({ type }) => {
		if (type !== 'goto') clearTimeout(searchTimer);
	});
	afterNavigate(({ type }) => {
		if (type !== 'goto') queryText = page.url.searchParams.get('query') ?? '';
	});

	// Selection survives paging and filtering, so it keeps whole rows rather than ids.
	let selected = $state<RunHistoryRow[]>([]);
	const isSelected = (run: RunHistoryRow) => selected.some((s) => s.id === run.id);
	const allVisibleSelected = $derived(runs.length > 0 && runs.every(isSelected));
	const someVisibleSelected = $derived(runs.some(isSelected));
	const problem = $derived(compareProblem(selected));

	function toggle(run: RunHistoryRow, on: boolean) {
		selected = on ? [...selected, run] : selected.filter((s) => s.id !== run.id);
	}
	function toggleVisible(on: boolean) {
		const rest = selected.filter((s) => !runs.some((run) => run.id === s.id));
		selected = on ? [...rest, ...runs] : rest;
	}

	const selectClass =
		'h-10 w-full rounded-md border border-line bg-panel pr-9 text-[13px] text-slate-200 focus:border-accent focus:ring-1 focus:ring-accent';
	const checkboxClass = 'size-4 rounded border-line-strong bg-field text-accent focus:ring-accent focus:ring-offset-0';
</script>

{#snippet pageLink(target: number, label: string, ariaLabel: string)}
	{#if target >= 1 && target <= pageCount}
		<a href={hrefWith('page', String(target))} data-sveltekit-noscroll aria-label={ariaLabel} class="grid size-8 place-items-center rounded-md border border-line hover:text-slate-200">{label}</a>
	{:else}
		<span aria-hidden="true" class="grid size-8 place-items-center rounded-md border border-line opacity-40">{label}</span>
	{/if}
{/snippet}

<svelte:head><title>Test runs · {project.name} · Chaos Lab</title></svelte:head>

<div class="mt-8 flex flex-wrap items-start justify-between gap-4">
	<div>
		<h2 class="text-lg font-semibold">Test runs</h2>
		<p class="mt-1 text-xs text-slate-400">
			{history.matched === history.total ? '' : `${history.matched} of `}{history.total} run{history.total === 1 ? '' : 's'}{#if history.scoringVersions.length} · scored with scoring version {history.scoringVersions.join(', ')}{/if}
		</p>
	</div>
	{#if canRun}
		<a href="/projects/{project.id}/runs/new" class="flex h-9 items-center gap-2 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover">
			<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" aria-hidden="true"><path d="M7 5v14l11-7z" /></svg>
			New test run
		</a>
	{/if}
</div>

<div class="mt-5 flex flex-wrap gap-3">
	<label class="relative min-w-60 flex-[2]">
		<span class="sr-only">Search runs</span>
		<svg viewBox="0 0 24 24" class="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-slate-500" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="6" /><path d="M20 20l-4.5-4.5" /></svg>
		<input
			bind:value={queryText}
			oninput={onSearch}
			type="search"
			placeholder="Search run ID or person"
			class="block h-10 w-full rounded-md border border-line bg-panel pl-9 text-[13px] text-slate-100 placeholder:text-slate-500 focus:border-accent focus:ring-1 focus:ring-accent"
		/>
	</label>
	<label class="min-w-44 flex-1">
		<span class="sr-only">Filter by status</span>
		<select value={params.get('status') ?? 'all'} onchange={(e) => setFilter('status', e.currentTarget.value === 'all' ? null : e.currentTarget.value)} class={selectClass}>
			<option value="all">All statuses</option>
			{#each Object.entries(statusStyles) as [value, [label]] (value)}<option {value}>{label}</option>{/each}
		</select>
	</label>
	<label class="min-w-44 flex-1">
		<span class="sr-only">Filter by environment</span>
		<select value={params.get('environment') ?? ''} onchange={(e) => setFilter('environment', e.currentTarget.value || null)} class={selectClass}>
			<option value="">All environments</option>
			{#each history.environments as environment (environment)}<option value={environment}>{environment}</option>{/each}
		</select>
	</label>
	<label class="min-w-44 flex-1">
		<span class="sr-only">Date range</span>
		<select value={data.days} onchange={(e) => setFilter('days', e.currentTarget.value === DEFAULT_DAYS ? null : e.currentTarget.value)} class={selectClass}>
			{#each DAY_RANGES as [value, label] (value)}<option {value}>{label}</option>{/each}
		</select>
	</label>
</div>

{#if runs.length}
	<div class="mt-6 overflow-hidden rounded-lg border border-line">
		<div class="overflow-x-auto">
			<table class="w-full min-w-[68rem] text-left text-[13px]">
				<thead class="border-b border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
					<tr>
						<th scope="col" class="w-12 py-4 pl-5">
							<input
								type="checkbox"
								aria-label="Select all runs on this page"
								checked={allVisibleSelected}
								indeterminate={someVisibleSelected && !allVisibleSelected}
								onchange={(e) => toggleVisible(e.currentTarget.checked)}
								class={checkboxClass}
							/>
						</th>
						{#each ['Run', 'Status', 'Environment', 'Started by', `Date${zone ? ` · ${zone}` : ''}`, 'Duration', 'Scenarios', 'Evidence coverage'] as heading (heading)}
							<th scope="col" class="px-4 py-4 font-semibold">{heading}</th>
						{/each}
						<th scope="col" class="px-5 py-4 text-right font-semibold">Score</th>
					</tr>
				</thead>
				<tbody class="divide-y divide-line-faint">
					{#each runs as run (run.id)}
						{@const [statusLabel, statusClass] = statusStyles[run.status]}
						{@const checked = isSelected(run)}
						{@const subtitle = note(run)}
						{@const change = run.score != null && run.previousScore != null ? Math.round(run.score - run.previousScore) : null}
						<tr class={checked ? 'bg-panel' : 'hover:bg-panel'}>
							<td class="py-4 pl-5">
								<input type="checkbox" aria-label="Select {runLabel(run)}" {checked} onchange={(e) => toggle(run, e.currentTarget.checked)} class={checkboxClass} />
							</td>
							<td class="max-w-48 px-4 py-4">
								<a href="/projects/{project.id}/runs/{run.id}" class="font-semibold text-accent-ink hover:text-accent-ink-strong">{runLabel(run)}</a>
								{#if subtitle}<p class="truncate text-[11px] text-slate-500" title={subtitle}>{subtitle}</p>{/if}
							</td>
							<td class="px-4 py-4">
								<span class="inline-flex items-center gap-1.5 rounded px-2 py-1 text-[11px] whitespace-nowrap {statusClass}">
									<span class="size-1.5 rounded-full bg-current" aria-hidden="true"></span>{statusLabel}
								</span>
							</td>
							<td class="px-4 py-4 text-slate-300">{run.environmentName}</td>
							<td class="px-4 py-4 text-slate-300">{run.requestedBy}</td>
							<td class="px-4 py-4 whitespace-nowrap text-slate-300">{dateTime(run.startedAt ?? run.createdAt)}</td>
							<td class="px-4 py-4 text-slate-300">{duration(run.startedAt, run.completedAt ?? (run.status === 'Running' ? new Date().toISOString() : null))}</td>
							<td class="px-4 py-4 text-slate-300">{run.completedScenarios} / {run.totalScenarios}</td>
							<td class="px-4 py-4 text-slate-300">
								{#if run.evidenceCoverage != null && !active(run)}
									<span class="flex items-center gap-2">
										<span class="h-1 w-16 overflow-hidden rounded-full bg-line" aria-hidden="true">
											<span class="block h-full rounded-full {coverageBar(run.evidenceCoverage)}" style:width="{run.evidenceCoverage}%"></span>
										</span>
										{run.evidenceCoverage}%
									</span>
								{:else}<span class="text-slate-500">{PLACEHOLDER}</span>{/if}
							</td>
							<td class="px-5 py-4 text-right whitespace-nowrap">
								{#if run.status === 'Completed' && run.score != null}
									{#if change}
										<span class="mr-1 text-[11px] {change > 0 ? 'text-emerald-400' : 'text-rose-400'}">{change > 0 ? '+' : ''}{change}</span>
									{/if}
									<span class="text-[15px] font-semibold">{score(run.score)}</span><span class="ml-1 text-[10px] text-slate-500">/100</span>
								{:else if active(run)}
									<span class="text-slate-500">{PLACEHOLDER}</span>
								{:else}
									<span class="text-[11px] text-slate-500">Not scored</span>
								{/if}
							</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>

		<div class="flex flex-wrap items-center justify-between gap-4 border-t border-line-faint px-5 py-4 text-[11px] text-slate-500">
			<p>Partial, cancelled and runner-failed runs keep their evidence but aren't scored as complete. Runner errors never count against the target.</p>
			<nav aria-label="Pagination" class="flex flex-wrap gap-2">
				{@render pageLink(history.page - 1, '‹', 'Previous page')}
				{#each pageWindow(history.page, pageCount) as n, i (i)}
					{#if n === null}
						<span aria-hidden="true" class="grid size-8 place-items-center">…</span>
					{:else}
						<a
							href={hrefWith('page', String(n))}
							data-sveltekit-noscroll
							aria-current={n === history.page ? 'page' : undefined}
							class={['grid size-8 place-items-center rounded-md border', n === history.page ? 'border-line-strong bg-field text-slate-100' : 'border-line hover:text-slate-200']}
						>{n}</a>
					{/if}
				{/each}
				{@render pageLink(history.page + 1, '›', 'Next page')}
			</nav>
		</div>
	</div>
{:else}
	<p class="mt-6 grid min-h-40 place-items-center rounded-lg border border-dashed border-line px-4 text-center text-xs text-slate-500">
		{history.total ? 'No runs match your search or filters.' : 'No test runs yet.'}
	</p>
{/if}

{#if selected.length}
	<div class="mt-6 flex max-w-3xl flex-wrap items-center gap-x-6 gap-y-3 rounded-lg border border-accent/40 bg-panel px-5 py-3 text-[13px]" aria-live="polite">
		<p class="font-semibold">{selected.length} run{selected.length === 1 ? '' : 's'} selected</p>
		<p class="flex-1 text-xs text-slate-400">
			{problem ?? `${selected.map(runLabel).join(' vs ')} · same environment and scoring version`}
		</p>
		<button type="button" onclick={() => (selected = [])} class="text-accent-ink hover:text-accent-ink-strong">Clear</button>
		<button type="button" disabled title={problem ?? 'Comparing runs is not available yet'} class="h-9 rounded-md bg-accent/50 px-4 font-semibold text-on-accent">
			Compare runs
		</button>
	</div>
{/if}
