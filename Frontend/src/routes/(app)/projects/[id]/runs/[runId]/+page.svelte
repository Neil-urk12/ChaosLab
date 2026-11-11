<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import type { FeedRow, FindingOutcome, RunDetail, Severity, TimelineEvent } from '$lib/api';
	import ScoreRing from '$lib/components/ScoreRing.svelte';
	import { categoryScores, endpointScores, findingCategory, isScored, outcomeLabels } from '$lib/findings';
	import { dateTime, duration, PLACEHOLDER, score, scoreLabel } from '$lib/format';
	import { send } from '$lib/problem';
	import { clockDuration, feedResult, feedResultStyles, remainingMs, runLabel, statusStyles, type FeedResult } from '$lib/runs';
	import { categories as faultCategories, describeInvariant, describeParameters, faultLabels } from '$lib/scenarios';

	let { data } = $props();
	const project = $derived(data.project);
	// The live view polls the run on its own; a poll result only stands until load returns a newer run.
	let polled = $state.raw<{ base: RunDetail; run: RunDetail } | null>(null);
	const run = $derived(polled?.base === data.run ? polled.run : data.run);
	const runId = $derived(run.id);
	const findings = $derived(data.findings);
	const runsHref = $derived(`/projects/${project.id}/runs`);
	const findingsHref = $derived(`/projects/${project.id}/findings?run=${run.id}`);

	const count = (outcome: FindingOutcome) => findings.filter((f) => f.outcome === outcome).length;
	const outcomes = $derived(
		([
			['Passed', 'bg-emerald-400', 'text-emerald-400'],
			['Degraded', 'bg-amber-400', 'text-amber-400'],
			['Failed', 'bg-rose-400', 'text-rose-400'],
			['Inconclusive', 'bg-slate-400', 'text-slate-200'],
			['NotApplicable', 'bg-slate-600', 'text-slate-400']
		] as const).map(([outcome, bar, tone]) => ({ label: outcomeLabels[outcome], value: count(outcome), bar, tone }))
	);
	const scored = $derived(findings.filter(isScored).length);
	const inconclusive = $derived(count('Inconclusive'));
	// Same definition as the run history: scored findings over scored plus Inconclusive.
	const coverage = $derived(scored + inconclusive ? Math.round((100 * scored) / (scored + inconclusive)) : null);

	const change = $derived(run.score != null && run.previous ? Math.round(run.score - run.previous.score) : null);
	const trend = $derived(
		run.score == null ? 'Not scored' : change == null ? scoreLabel(run.score) : change > 0 ? 'Improving' : change < 0 ? 'Declining' : 'Steady'
	);

	const runnerNote = $derived.by(() => {
		if (run.failureMessage) return run.failureMessage;
		const parts = [run.status === 'Failed' ? 'The runner stopped this run.' : 'No runner errors in this run.'];
		if (run.skippedScenarios) parts.push(`${run.skippedScenarios} scenario${run.skippedScenarios === 1 ? ' was' : 's were'} skipped.`);
		if (run.completedScenarios < run.totalScenarios) parts.push(`${run.completedScenarios} of ${run.totalScenarios} scenarios completed.`);
		return parts.join(' ');
	});

	const categories = $derived(categoryScores(findings));
	const failing = $derived(findings.filter((f) => f.outcome === 'Failed' || f.outcome === 'Degraded'));
	// The findings query returns severity order, so the first rows are the worst.
	const topFindings = $derived(failing.slice(0, 4));
	const recommended = $derived(failing.filter((f) => f.recommendation));
	const endpoints = $derived(endpointScores(findings));

	// Unset or stale (after moving to another run) falls back to the first, worst timeline.
	let timelineId = $state<string>();
	const timeline = $derived(run.timelines.find((t) => t.executionId === timelineId) ?? run.timelines[0]);

	const severityStyles: Record<Severity, string> = {
		Critical: 'bg-rose-500/15 text-rose-300',
		High: 'bg-amber-500/15 text-amber-300',
		Medium: 'bg-sky-500/15 text-sky-300',
		Low: 'bg-slate-500/15 text-slate-300',
		Info: 'bg-slate-500/15 text-slate-400'
	};
	const methodTone = (method: string) =>
		method === 'GET' ? 'text-sky-400' : method === 'POST' ? 'text-emerald-400' : method === 'DELETE' ? 'text-rose-400' : 'text-amber-400';
	const scoreBar = (value: number) => (value >= 75 ? 'bg-emerald-400' : value >= 50 ? 'bg-amber-400' : 'bg-rose-400');

	const clock = (iso: string) =>
		new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3, hour12: false });

	// Dot tone and caption of one observed request.
	function event(e: TimelineEvent, method: string, path: string) {
		const status = e.clientStatusCode ?? e.upstreamStatusCode;
		const failed = ['Dropped', 'Timeout', 'ConnectionError', 'ProxyError'].includes(e.outcomeType) || (status ?? 0) >= 500;
		const injected = e.outcomeType === 'SyntheticResponse' || e.outcomeType === 'ModifiedResponse';
		const readBack = e.requestMethod !== method || e.requestPath !== path;
		return {
			tone: failed ? 'bg-rose-400' : injected || (status ?? 0) >= 400 ? 'bg-amber-400' : 'bg-emerald-400',
			title: readBack ? 'Read-back' : `Attempt ${e.attemptNo}`,
			detail: [
				readBack ? `${e.requestMethod} ${e.requestPath}` : null,
				status ?? e.outcomeType.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase(),
				injected ? 'fault injected' : null,
				e.durationMs != null ? `${e.durationMs} ms` : null,
				e.errorType
			]
				.filter((part) => part != null)
				.join(' · ')
		};
	}

	// Live view while the run is Queued or Running.
	const live = $derived(run.live);
	const active = $derived(live != null);
	const myRole = $derived(project.members.find((m) => m.userId === data.me.id)?.role);
	const canCancel = $derived(myRole === 'Owner' || myRole === 'Editor');

	let now = $state(Date.now());
	// A failed poll keeps the last good data and the next tick tries again; a 4xx (run gone, signed out) goes to load.
	// One poll at a time, so a slow response can't land after a newer one and move progress backwards.
	let polling = false;
	async function poll() {
		if (polling) return;
		polling = true;
		try {
			const base = data.run;
			// The timeout keeps a request that never answers from blocking every later poll.
			const res = await fetch(`/api/projects/${project.id}/runs/${base.id}`, { signal: AbortSignal.timeout(10_000) }).catch(() => null);
			if (!res || base !== data.run) return;
			if (res.ok) {
				const next: RunDetail | null = await res.json().catch(() => null);
				if (next) polled = { base, run: next };
			} else if (res.status < 500) invalidateAll();
		} finally {
			polling = false;
		}
	}

	let watched: string | undefined;
	$effect(() => {
		// The watched run just finished: reload everything (findings, latest score, sidebar badges).
		if (watched === runId && !active) invalidateAll();
		watched = active ? runId : undefined;
		if (!active) return;
		let ticks = 0;
		const timer = setInterval(() => {
			now = Date.now();
			// shortcut: polls every 3 s; switch to server-sent events if many viewers watch long runs.
			if (++ticks % 3 === 0) poll();
		}, 1000);
		return () => clearInterval(timer);
	});

	const results = $derived((live?.feed ?? []).map((row) => ({ row, result: feedResult(row) })));
	const tally = (...kinds: FeedResult[]) => results.filter((r) => kinds.includes(r.result)).length;
	const liveCounts = $derived(
		([
			['Pass', ['Passed'], 'text-emerald-400'],
			['Degraded', ['Degraded'], 'text-amber-400'],
			['Fail', ['Failed'], 'text-rose-400'],
			['Inconclusive', ['Inconclusive'], 'text-slate-200'],
			['Runner errors', ['RunnerError'], 'text-slate-300'],
			['Skipped', ['Skipped'], 'text-slate-400']
		] as const).map(([label, kinds, tone]) => ({ label, value: tally(...kinds), tone }))
	);

	const FEED_FILTERS = [
		['All', null],
		['Problems', ['Failed', 'Degraded', 'Inconclusive']],
		['Runner errors', ['RunnerError']]
	] as const;
	let feedFilter = $state<(typeof FEED_FILTERS)[number][0]>('All');
	const shownFeed = $derived.by(() => {
		const kinds: readonly FeedResult[] | null = FEED_FILTERS.find(([label]) => label === feedFilter)![1];
		return kinds ? results.filter((r) => kinds.includes(r.result)) : results;
	});

	const elapsed = $derived(run.startedAt ? now - Date.parse(run.startedAt) : null);
	const remaining = $derived(elapsed == null ? null : remainingMs(elapsed, run.completedScenarios, run.totalScenarios));
	const progress = $derived(run.totalScenarios ? (100 * run.completedScenarios) / run.totalScenarios : 0);

	const current = $derived(live?.current ?? null);
	const currentRow = $derived(current ? live!.feed.find((row) => row.executionId === current.executionId) : undefined);
	// Attempts of the scenario's own request; read-backs and baselines to other paths don't count.
	const attempt = $derived(
		current && currentRow
			? Math.max(0, ...current.events.filter((e) => e.requestMethod === currentRow.httpMethod && e.requestPath === currentRow.path).map((e) => e.attemptNo))
			: 0
	);
	const expected = $derived(current?.invariants[0] ? describeInvariant(current.invariants[0]).filter(Boolean).join(' · ') : null);

	const feedClock = (iso: string) => new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
	const took = (row: FeedRow) =>
		row.startedAt && row.completedAt ? `${((Date.parse(row.completedAt) - Date.parse(row.startedAt)) / 1000).toFixed(1)} s` : PLACEHOLDER;
	function observation(row: FeedRow, result: FeedResult) {
		if (result === 'Running') return row.executionId === current?.executionId && attempt ? `Attempt ${attempt} in flight` : 'In flight';
		if (result === 'RunnerError' || result === 'Skipped') return row.failureReason ?? PLACEHOLDER;
		return row.findings[0]?.summary ?? row.failureReason ?? PLACEHOLDER;
	}

	let cancelling = $state(false);
	let cancelError = $state<string | null>(null);
	async function cancelRun() {
		if (!confirm(`Cancel ${runLabel(run)}? Scenarios that haven't started won't run, and the run won't be scored.`)) return;
		cancelling = true;
		cancelError = await send(`/api/projects/${project.id}/runs/${run.id}/cancel`, 'POST');
		// On an error (the run may have just finished) refresh through poll, which tolerates an unreachable server.
		if (cancelError) await poll();
		else await invalidateAll();
		cancelling = false;
	}

	const card = 'rounded-lg border border-line';
	const chip = 'inline-flex items-center gap-1.5 rounded px-2 py-0.5 text-[11px] whitespace-nowrap';
	const eyebrow = 'text-[10px] font-semibold tracking-wider text-slate-500 uppercase';
	const disabledButton = 'flex h-10 items-center gap-2 rounded-md border border-line-strong px-4 text-[13px] text-slate-500';
</script>

{#snippet heading(title: string, subtitle: string, href?: string)}
	<div class="flex items-start justify-between gap-4 border-b border-line-faint px-5 py-4">
		<div>
			<h3 class="font-semibold">{title}</h3>
			<p class="mt-1 text-[11px] text-slate-500">{subtitle}</p>
		</div>
		{#if href}<a {href} class="text-[11px] text-accent-ink hover:text-accent-ink-strong">View all</a>{/if}
	</div>
{/snippet}

{#snippet endpoint(method: string, path: string)}
	<span class="font-mono text-[11px] font-semibold {methodTone(method)}">{method}</span>
	<span class="font-mono text-[13px] text-slate-200">{path}</span>
{/snippet}

<svelte:head><title>{runLabel(run)} · {project.name} · Chaos Lab</title></svelte:head>

<a href={runsHref} class="mt-8 inline-flex items-center gap-2 text-xs text-accent-ink hover:text-accent-ink-strong">
	<span aria-hidden="true">←</span> All test runs
</a>

<div class="mt-4 flex flex-wrap items-start justify-between gap-4">
	<div>
		<div class="flex flex-wrap items-center gap-3">
			<h2 class="text-xl font-bold">{runLabel(run)}{live ? '' : ' results'}</h2>
			<span class="{chip} {statusStyles[run.status][1]}"><span class="size-1.5 rounded-full bg-current" aria-hidden="true"></span>{statusStyles[run.status][0]}</span>
		</div>
		<p class="mt-1 text-xs text-slate-400">
			{#if live}
				{run.startedAt ? 'Started' : 'Queued'} by {run.requestedBy} · {dateTime(run.startedAt ?? run.createdAt)} · {run.environmentName} · scoring version {run.scoringVersion}
			{:else}
				{dateTime(run.startedAt ?? run.createdAt)} · {duration(run.startedAt, run.completedAt)} · started by {run.requestedBy} · {run.environmentName} · {run.totalScenarios} scenarios · scoring version {run.scoringVersion}
			{/if}
		</p>
	</div>
	{#if live}
		{#if canCancel}
			<div class="flex flex-col items-end gap-2">
				<button type="button" onclick={cancelRun} disabled={cancelling} class="flex h-10 items-center gap-2 rounded-md border border-rose-500/40 bg-rose-500/10 px-4 text-[13px] text-rose-300 hover:bg-rose-500/20 disabled:opacity-50">
					<span aria-hidden="true">✕</span> {cancelling ? 'Cancelling…' : 'Cancel run'}
				</button>
				{#if cancelError}<p class="text-xs text-rose-300" role="alert">{cancelError}</p>{/if}
			</div>
		{/if}
	{:else}
	<div class="flex flex-wrap gap-2">
		{#if run.previous}
			<button type="button" disabled title="Comparing runs is not available yet" class={disabledButton}>Compare with {runLabel(run.previous)}</button>
		{/if}
		<button type="button" disabled title="Rerunning scenarios is not available yet" class={disabledButton}>Rerun failed ({failing.length})</button>
		<button type="button" disabled title="PDF reports are not available yet" class="h-10 rounded-md bg-accent/50 px-4 text-[13px] font-semibold text-on-accent">Generate PDF report</button>
	</div>
	{/if}
</div>

{#if live}
	<section class="{card} mt-6 p-6" aria-label="Progress">
		<div class="flex flex-wrap items-end justify-between gap-6">
			<div>
				<p class={eyebrow}>Progress</p>
				<p class="mt-2"><span class="text-4xl font-bold">{run.completedScenarios}</span><span class="text-slate-400"> / {run.totalScenarios} scenarios</span></p>
			</div>
			<dl class="flex flex-wrap gap-8">
				<div>
					<dt class={eyebrow}>Elapsed</dt>
					<dd class="mt-1 font-semibold">{elapsed == null ? PLACEHOLDER : clockDuration(elapsed)}</dd>
				</div>
				<div>
					<dt class={eyebrow}>Remaining (est.)</dt>
					<dd class="mt-1 font-semibold">{remaining == null ? PLACEHOLDER : `~${clockDuration(remaining)}`}</dd>
				</div>
				<div>
					<dt class={eyebrow}>Current endpoint</dt>
					<dd class="mt-1 flex items-center gap-2">{#if currentRow}{@render endpoint(currentRow.httpMethod, currentRow.path)}{:else}<span class="text-slate-500">{PLACEHOLDER}</span>{/if}</dd>
				</div>
			</dl>
		</div>
		<div class="mt-5 h-1.5 overflow-hidden rounded-full bg-line" role="progressbar" aria-label="Scenarios completed" aria-valuemin={0} aria-valuemax={run.totalScenarios} aria-valuenow={run.completedScenarios}>
			<div class="h-full rounded-full bg-accent transition-[width]" style:width="{progress}%"></div>
		</div>
	</section>

	<dl class="{card} mt-5 grid grid-cols-2 divide-line sm:grid-cols-3 lg:grid-cols-6 lg:divide-x">
		{#each liveCounts as c (c.label)}
			<div class="px-5 py-4">
				<dt class="text-[11px] text-slate-400">{c.label}</dt>
				<dd class="mt-1 text-2xl font-bold {c.tone}">{c.value}</dd>
			</div>
		{/each}
	</dl>

	<div class="mt-5 grid items-start gap-5 lg:grid-cols-[2fr_1fr]">
		<section class={card}>
			<div class="flex flex-wrap items-start justify-between gap-4 border-b border-line-faint px-5 py-4">
				<div>
					<h3 class="font-semibold">Execution feed</h3>
					<p class="mt-1 text-[11px] text-slate-500">Newest first. Updates as each scenario finishes.</p>
				</div>
				<div class="flex rounded-md border border-line p-0.5" role="group" aria-label="Filter the feed">
					{#each FEED_FILTERS as [label] (label)}
						<button type="button" aria-pressed={feedFilter === label} onclick={() => (feedFilter = label)}
							class="rounded px-3 py-1.5 text-xs {feedFilter === label ? 'bg-panel text-slate-100' : 'text-slate-400 hover:text-slate-200'}">{label}</button>
					{/each}
				</div>
			</div>
			<div class="overflow-x-auto">
				<table class="w-full min-w-[44rem] text-left text-[13px]">
					<thead class="text-[10px] tracking-wider text-slate-500 uppercase">
						<tr class="border-b border-line-faint">
							<th scope="col" class="px-5 py-3 font-semibold">Time</th>
							<th scope="col" class="px-3 py-3 font-semibold">Scenario</th>
							<th scope="col" class="px-3 py-3 font-semibold">Endpoint</th>
							<th scope="col" class="px-3 py-3 font-semibold">Result</th>
							<th scope="col" class="px-3 py-3 font-semibold">Took</th>
							<th scope="col" class="px-5 py-3 font-semibold">Observation</th>
						</tr>
					</thead>
					<tbody class="divide-y divide-line-faint">
						{#each shownFeed as { row, result } (row.executionId)}
							<tr class={result === 'Running' ? 'bg-accent-tint/40' : ''}>
								<td class="px-5 py-3 font-mono text-[12px] whitespace-nowrap text-slate-500">{row.completedAt ?? row.startedAt ? feedClock((row.completedAt ?? row.startedAt)!) : PLACEHOLDER}</td>
								<td class="px-3 py-3 whitespace-nowrap text-slate-200">{faultLabels[row.scenarioType]}</td>
								<td class="px-3 py-3 whitespace-nowrap">{@render endpoint(row.httpMethod, row.path)}</td>
								<td class="px-3 py-3"><span class="{chip} {feedResultStyles[result][1]}"><span class="size-1.5 rounded-full bg-current" aria-hidden="true"></span>{feedResultStyles[result][0]}</span></td>
								<td class="px-3 py-3 whitespace-nowrap text-slate-300">{took(row)}</td>
								<td class="min-w-48 px-5 py-3 text-xs text-slate-400">{observation(row, result)}</td>
							</tr>
						{:else}
							<tr><td colspan="6" class="px-5 py-8 text-center text-xs text-slate-500">{live!.feed.length ? 'No scenarios match this filter.' : 'No scenario has started yet.'}</td></tr>
						{/each}
					</tbody>
				</table>
			</div>
		</section>

		<div class="grid gap-5">
			<section class={card} aria-label="Now running">
				<div class="flex items-start justify-between gap-4 border-b border-line-faint px-5 py-4">
					<div>
						<h3 class="font-semibold">Now running</h3>
						{#if currentRow}
							<p class="mt-1 text-[11px] text-slate-500">{faultLabels[currentRow.scenarioType]} · {currentRow.httpMethod} {currentRow.path} · {faultCategories[currentRow.scenarioType]}</p>
						{/if}
					</div>
					{#if attempt}<span class="{chip} bg-accent-tint text-accent-ink">Attempt {attempt}</span>{/if}
				</div>
				{#if current && currentRow}
					<div class="grid grid-cols-2 gap-3 px-5 pt-4">
						<div class="rounded-md border border-line px-3 py-2">
							<p class={eyebrow}>Fault</p>
							<p class="mt-1 text-[13px]">{describeParameters(currentRow.scenarioType, current.parameters) || faultLabels[currentRow.scenarioType]}</p>
						</div>
						<div class="rounded-md border border-line px-3 py-2">
							<p class={eyebrow}>Expected</p>
							<p class="mt-1 text-[13px]">{expected ?? 'No invariant set'}</p>
						</div>
					</div>
					<ol class="px-5 py-4">
						{#each current.events as e, i (i)}
							{@const shown = event(e, currentRow.httpMethod, currentRow.path)}
							<li class="relative border-l border-line pb-4 pl-5 last:pb-0">
								<span class="absolute top-1 -left-1.5 size-3 rounded-full {shown.tone}" aria-hidden="true"></span>
								<p class="font-mono text-[11px] text-slate-500">{clock(e.startedAt)}</p>
								<p class="text-[13px] font-semibold">{shown.title}</p>
								<p class="text-[11px] text-slate-400">{shown.detail}</p>
							</li>
						{/each}
						<li class="relative pl-5">
							<span class="absolute top-1 -left-1.5 size-3 rounded-full bg-slate-500" aria-hidden="true"></span>
							<p class="text-[13px] font-semibold">Waiting</p>
							<p class="text-[11px] text-slate-400">Scenario elapsed {currentRow.startedAt ? clockDuration(now - Date.parse(currentRow.startedAt)) : PLACEHOLDER}</p>
						</li>
					</ol>
				{:else}
					<p class="px-5 py-8 text-center text-xs text-slate-500">{run.status === 'Queued' ? 'Waiting for the runner to start.' : 'Between scenarios.'}</p>
				{/if}
			</section>

			<p class="{card} bg-panel px-4 py-3 text-xs text-slate-300">
				<strong class="font-semibold text-slate-100">Cancelling</strong> stops new scenarios from starting. In-flight requests finish or time out, the evidence collected so far is kept, and the run is marked Cancelled instead of scored.
			</p>
			<p class="{card} bg-panel px-4 py-3 text-xs text-slate-300">
				<strong class="font-semibold text-slate-100">Runner errors and skips</strong> are ChaosLab's problems or unreachable dependencies, not the target's. They're excluded from scoring.
			</p>
		</div>
	</div>
{:else}
	<div class="mt-6 grid gap-5 lg:grid-cols-[2fr_3fr]">
		<section class="{card} flex flex-wrap items-center gap-6 p-6" aria-label="Resilience score">
			<ScoreRing value={run.score} />
			<div class="min-w-48 flex-1">
				<p class={eyebrow}>Resilience score</p>
				<p class="mt-1 flex flex-wrap items-center gap-3">
					<span class="text-xl font-bold">{trend}</span>
					{#if change != null && run.previous}
						<span class="rounded px-2 py-0.5 text-[11px] {change >= 0 ? 'bg-emerald-500/10 text-emerald-300' : 'bg-rose-500/10 text-rose-300'}">
							{change > 0 ? '+' : ''}{change} vs {runLabel(run.previous)}
						</span>
					{/if}
				</p>
				<div class="mt-4 border-t border-line-faint pt-3">
					<p class="flex justify-between text-xs text-slate-300"><span>Evidence coverage</span><span>{coverage == null ? PLACEHOLDER : `${coverage}%`}</span></p>
					<div class="mt-2 h-1 overflow-hidden rounded-full bg-line" aria-hidden="true">
						<div class="h-full rounded-full bg-emerald-400" style:width="{coverage ?? 0}%"></div>
					</div>
					<p class="mt-2 text-[11px] text-slate-500">
						{scored} of {scored + inconclusive} findings scored. Inconclusive and N/A findings don't affect the score.
					</p>
				</div>
			</div>
		</section>

		<section class="{card} p-6" aria-label="Outcomes">
			<div class="flex flex-wrap justify-between gap-2">
				<p class={eyebrow}>Outcomes · {findings.length} findings</p>
				<p class="text-[11px] text-slate-500">Pass 100 · Degraded 50 · Fail 0 points</p>
			</div>
			<div class="mt-4 flex h-1.5 gap-0.5 overflow-hidden rounded-full bg-line" aria-hidden="true">
				{#each outcomes as o (o.label)}
					{#if o.value}<div class={o.bar} style:width="{(100 * o.value) / findings.length}%"></div>{/if}
				{/each}
			</div>
			<dl class="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-5">
				{#each outcomes as o (o.label)}
					<div>
						<dt class="flex items-center gap-1.5 text-[11px] text-slate-400"><span class="size-1.5 rounded-sm {o.bar}" aria-hidden="true"></span>{o.label}</dt>
						<dd class="mt-1 text-2xl font-bold {o.tone}">{o.value}</dd>
					</div>
				{/each}
			</dl>
			<p class="mt-5 rounded-md border border-line bg-panel px-4 py-3 text-xs text-slate-300">{runnerNote}</p>
		</section>
	</div>

	<div class="mt-5 grid gap-5 lg:grid-cols-[5fr_3fr]">
		<section class={card}>
			{@render heading('Category breakdown', `Each category is the mean of its findings. Locked weights from scoring version ${run.scoringVersion}.`)}
			<div class="overflow-x-auto px-5 py-3">
				<table class="w-full min-w-[34rem] text-left text-[13px]">
					<thead class="text-[10px] tracking-wider text-slate-500 uppercase">
						<tr>
							<th scope="col" class="py-2 font-semibold">Category</th>
							<th scope="col" class="py-2 font-semibold">Weight</th>
							<th scope="col" class="py-2"><span class="sr-only">Score bar</span></th>
							<th scope="col" class="py-2 text-right font-semibold">Score</th>
							<th scope="col" class="py-2 text-right font-semibold">Findings</th>
						</tr>
					</thead>
					<tbody>
						{#each categories as c (c.label)}
							<tr>
								<td class="py-2 pr-4 text-slate-200">{c.label}</td>
								<td class="py-2 pr-4 text-slate-500">{c.weight}%</td>
								{#if c.score == null}
									<td class="py-2 pr-4 text-[11px] text-slate-500">No scored findings · weight redistributed to other categories</td>
									<td class="py-2 text-right text-slate-500">{PLACEHOLDER}</td>
									<td class="py-2 pl-4 text-right whitespace-nowrap text-slate-500">{c.inconclusive ? `${c.inconclusive} inconclusive` : `${c.findings} findings`}</td>
								{:else}
									<td class="w-1/2 py-2 pr-4">
										<div class="h-1 overflow-hidden rounded-full bg-line" aria-hidden="true">
											<div class="h-full rounded-full {scoreBar(c.score)}" style:width="{c.score}%"></div>
										</div>
									</td>
									<td class="py-2 text-right font-semibold">{score(c.score)}</td>
									<td class="py-2 pl-4 text-right whitespace-nowrap text-slate-500">{c.findings} finding{c.findings === 1 ? '' : 's'}</td>
								{/if}
							</tr>
						{/each}
					</tbody>
				</table>
			</div>
		</section>

		<section class={card}>
			{@render heading('Top findings', `${count('Failed')} failed, ${count('Degraded')} degraded`, findingsHref)}
			{#if topFindings.length}
				<ul class="divide-y divide-line-faint">
					{#each topFindings as f (f.id)}
						<li class="px-5 py-3">
							<p class="flex flex-wrap items-center gap-2">
								<span class="{chip} {severityStyles[f.severity]}">{f.severity}</span>
								{@render endpoint(f.httpMethod, f.path)}
							</p>
							<p class="mt-1 text-[13px] text-slate-200">{f.summary}</p>
							<p class="mt-0.5 text-[11px] text-slate-500">{findingCategory(f)} · {outcomeLabels[f.outcome]}</p>
						</li>
					{/each}
				</ul>
			{:else}
				<p class="px-5 py-8 text-center text-xs text-slate-500">No failed or degraded findings.</p>
			{/if}
		</section>
	</div>

	<section class="{card} mt-5">
		<div class="flex flex-wrap items-start justify-between gap-4 border-b border-line-faint px-5 py-4">
			<div>
				<h3 class="font-semibold">Failure timeline</h3>
				<p class="mt-1 text-[11px] text-slate-500">Requests for one failing scenario, in order.</p>
			</div>
			{#if run.timelines.length > 1}
				<label>
					<span class="sr-only">Scenario</span>
					<select bind:value={timelineId} class="h-10 rounded-md border border-line bg-panel pr-9 text-[13px] text-slate-200 focus:border-accent focus:ring-1 focus:ring-accent">
						{#each run.timelines as t (t.executionId)}
							<option value={t.executionId}>{t.httpMethod} {t.path} · {faultLabels[t.scenarioType]}</option>
						{/each}
					</select>
				</label>
			{:else if timeline}
				<p class="flex items-center gap-2 text-xs">{@render endpoint(timeline.httpMethod, timeline.path)}<span class="text-slate-400">{faultLabels[timeline.scenarioType]}</span></p>
			{/if}
		</div>
		{#if timeline?.events.length}
			<ol class="flex gap-6 overflow-x-auto px-5 py-5">
				{#each timeline.events as e, i (i)}
					{@const shown = event(e, timeline.httpMethod, timeline.path)}
					<li class="min-w-40 flex-1">
						<span class="block size-3 rounded-full {shown.tone}" aria-hidden="true"></span>
						<p class="mt-3 font-mono text-[11px] text-slate-500">{clock(e.startedAt)}</p>
						<p class="mt-1 text-[13px] font-semibold">{shown.title}</p>
						<p class="mt-1 text-[11px] text-slate-400">{shown.detail}</p>
					</li>
				{/each}
				<li class="min-w-40 flex-1">
					<span class="block size-3 rounded-full {timeline.outcome === 'Passed' ? 'bg-emerald-400' : 'bg-rose-400'}" aria-hidden="true"></span>
					<p class="mt-3 font-mono text-[11px] text-slate-500">{timeline.completedAt ? clock(timeline.completedAt) : PLACEHOLDER}</p>
					<p class="mt-1 text-[13px] font-semibold">Scenario complete</p>
					<p class="mt-1 text-[11px] text-slate-400">{timeline.failureReason ?? timeline.outcome ?? 'No outcome'}</p>
				</li>
			</ol>
		{:else}
			<p class="px-5 py-8 text-center text-xs text-slate-500">
				{timeline ? 'No requests were recorded for this scenario.' : 'No failing scenarios with recorded requests.'}
			</p>
		{/if}
	</section>

	<div class="mt-5 grid gap-5 lg:grid-cols-[5fr_3fr]">
		<section class={card}>
			{@render heading('Recommendations', "Rule-based. ChaosLab doesn't change your code.", recommended.length > 3 ? findingsHref : undefined)}
			{#if recommended.length}
				<ul class="divide-y divide-line-faint">
					{#each recommended.slice(0, 3) as f (f.id)}
						<li class="px-5 py-4">
							<p class="flex flex-wrap items-center gap-2">
								<span class="text-[13px] font-semibold">{f.recommendation!.title}</span>
								<span class="{chip} {severityStyles[f.severity]}">{f.severity}</span>
							</p>
							<p class="mt-1 text-[13px] text-slate-300">{f.recommendation!.text}</p>
							<p class="mt-1 font-mono text-[11px] text-slate-500">{f.httpMethod} {f.path} · {findingCategory(f)}</p>
						</li>
					{/each}
				</ul>
			{:else}
				<p class="px-5 py-8 text-center text-xs text-slate-500">No recommendations for this run.</p>
			{/if}
		</section>

		<section class={card}>
			{@render heading('Endpoint scores', 'Same method, one endpoint at a time')}
			{#if endpoints.length}
				<ul class="divide-y divide-line-faint">
					{#each endpoints as e (`${e.httpMethod} ${e.path}`)}
						<li class="flex items-center gap-3 px-5 py-3">
							<span class="flex min-w-0 flex-1 items-center gap-2 truncate">{@render endpoint(e.httpMethod, e.path)}</span>
							<span class="h-1 w-24 overflow-hidden rounded-full bg-line" aria-hidden="true">
								<span class="block h-full rounded-full {scoreBar(e.score)}" style:width="{e.score}%"></span>
							</span>
							<span class="w-8 text-right text-[13px] font-semibold">{score(e.score)}</span>
						</li>
					{/each}
				</ul>
			{:else}
				<p class="px-5 py-8 text-center text-xs text-slate-500">No scored findings yet.</p>
			{/if}
		</section>
	</div>

	<section class="{card} mt-5 flex flex-wrap items-center justify-between gap-4 px-5 py-4">
		<div>
			<h3 class="text-[13px] font-semibold">PDF report</h3>
			<p class="mt-1 text-[11px] text-slate-500">
				Includes scores, evidence, invariant results, timeline and recommendations{run.previous ? ` and the comparison with ${runLabel(run.previous)}` : ''}. Credentials and secret headers are redacted.
			</p>
		</div>
		<button type="button" disabled title="PDF reports are not available yet" class={disabledButton}>Generate PDF report</button>
	</section>
{/if}
