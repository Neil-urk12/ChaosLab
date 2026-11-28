<script lang="ts">
	import type { FindingOutcome, RunDetail } from '$lib/api';
	import ScoreRing from '$lib/components/ScoreRing.svelte';
	import { compareFindings, pairScores, scoreChange, type FindingChange } from '$lib/compare';
	import { outcomeLabels, scoringCategoryLabels } from '$lib/findings';
	import { dateTime, PLACEHOLDER, score } from '$lib/format';
	import { compareProblem, runLabel, statusStyles } from '$lib/runs';
	import { faultLabels } from '$lib/scenarios';

	let { data } = $props();
	const project = $derived(data.project);
	const before = $derived(data.before);
	const after = $derived(data.after);
	const runsHref = $derived(`/projects/${project.id}/runs`);

	const problem = $derived(before.run.id === after.run.id ? 'Select two different runs' : compareProblem([before.run, after.run]));
	// Scores are the stored ones (PROJECT.md 14.3, rule 9); only their difference is worked out here, as on the results page.
	const change = $derived(scoreChange(before.run.score, after.run.score));
	const changes = $derived(compareFindings(before.findings, after.findings));
	const groups = $derived([
		{ title: 'Worse', note: 'Failing now, and better or not judged before', rows: changes.worse },
		{ title: 'Better', note: 'Improved since the earlier run', rows: changes.better },
		{ title: 'Still failing', note: 'The same failed or degraded outcome in both runs', rows: changes.stillFailing },
		{ title: 'Not judged again', note: 'Failing before; Inconclusive, not applicable or not run now, so not shown fixed', rows: changes.unjudged }
	].filter((g) => g.rows.length));
	const categories = $derived(pairScores(before.run.categories ?? [], after.run.categories ?? [], (c) => c.category));
	const endpoints = $derived(
		pairScores(before.run.endpoints, after.run.endpoints, (e) => `${e.httpMethod} ${e.path}`).sort(
			(x, y) => (scoreChange(x.before?.score, x.after?.score) ?? Infinity) - (scoreChange(y.before?.score, y.after?.score) ?? Infinity)
		)
	);

	const card = 'rounded-lg border border-line';
	const eyebrow = 'text-[10px] font-semibold tracking-wider text-slate-500 uppercase';
	const th = 'py-2 font-semibold';
	const outcomeTone: Record<FindingOutcome, string> = {
		Passed: 'text-emerald-300',
		Degraded: 'text-amber-300',
		Failed: 'text-rose-300',
		Inconclusive: 'text-slate-300',
		NotApplicable: 'text-slate-400'
	};
	const methodTone = (method: string) =>
		method === 'GET' ? 'text-sky-400' : method === 'POST' ? 'text-emerald-400' : method === 'DELETE' ? 'text-rose-400' : 'text-amber-400';
	const changeTone = (value: number) => (value > 0 ? 'text-emerald-300' : value < 0 ? 'text-rose-300' : 'text-slate-400');
	const signed = (value: number | null) => (value == null ? PLACEHOLDER : `${value > 0 ? '+' : ''}${value}`);
	const runHref = (run: RunDetail) => `${runsHref}/${run.id}`;
</script>

{#snippet endpoint(method: string, path: string)}
	<span class="font-mono text-[11px] font-semibold {methodTone(method)}">{method}</span>
	<span class="font-mono text-[13px] text-slate-200">{path}</span>
{/snippet}

{#snippet outcome(change: FindingChange, side: 'before' | 'after')}
	{@const f = change[side]}
	<span class={f ? outcomeTone[f.outcome] : 'text-slate-500'}>{f ? outcomeLabels[f.outcome] : 'Not run'}</span>
{/snippet}

{#snippet runCard(run: RunDetail, label: string)}
	<section class="{card} flex flex-1 items-center gap-5 p-5" aria-label="{label} run">
		<ScoreRing value={run.score} size={96} />
		<div class="min-w-0">
			<p class={eyebrow}>{label}</p>
			<a href={runHref(run)} class="mt-1 block text-[15px] font-semibold text-accent-ink hover:text-accent-ink-strong">{runLabel(run)}</a>
			<p class="mt-1 text-xs text-slate-400">{dateTime(run.startedAt ?? run.createdAt)}</p>
			<p class="mt-2 flex flex-wrap items-center gap-2 text-[11px]">
				<span class="rounded px-2 py-0.5 {statusStyles[run.status][1]}">{statusStyles[run.status][0]}</span>
				<span class="text-slate-500">Evidence coverage {run.evidenceCoverage == null ? PLACEHOLDER : `${Math.round(run.evidenceCoverage)}%`}</span>
			</p>
		</div>
	</section>
{/snippet}

<svelte:head><title>Compare runs · {project.name} · Chaos Lab</title></svelte:head>

<a href={runsHref} class="mt-8 inline-flex items-center gap-2 text-xs text-accent-ink hover:text-accent-ink-strong">
	<span aria-hidden="true">←</span> All test runs
</a>
<h2 class="mt-3 text-lg font-semibold">{runLabel(before.run)} vs {runLabel(after.run)}</h2>
{#if problem}
	<p class="mt-6 grid min-h-40 place-items-center rounded-lg border border-dashed border-line px-4 text-center text-[13px] text-slate-500" role="alert">
		{problem}. Only runs on the same environment and scoring version compare meaningfully.
	</p>
{:else}
	<p class="mt-1 text-xs text-slate-400">{after.run.environmentName} · scoring version {after.run.scoringVersion}</p>
	<div class="mt-6 flex flex-col gap-5 lg:flex-row lg:items-center">
		{@render runCard(before.run, 'Earlier')}
		<p class="text-center" aria-label="Score change">
			<span class="block text-2xl font-bold {change == null ? 'text-slate-500' : changeTone(change)}">{signed(change)}</span>
			<span class="text-[11px] text-slate-500">{change == null ? 'Both runs need a score' : 'points'}</span>
		</p>
		{@render runCard(after.run, 'Later')}
	</div>

	<section class="{card} mt-5" aria-labelledby="changes-heading">
		<div class="border-b border-line-faint px-5 py-4">
			<h3 id="changes-heading" class="font-semibold">Finding changes</h3>
			<p class="mt-1 text-[11px] text-slate-500">Each check matched by endpoint, fault and pattern. Checks that passed in both runs are left out.</p>
		</div>
		{#each groups as group (group.title)}
			<div class="border-b border-line-faint px-5 py-4 last:border-b-0">
				<h4 class="text-[13px] font-semibold">{group.title} <span class="font-normal text-slate-500">({group.rows.length})</span></h4>
				<p class="text-[11px] text-slate-500">{group.note}</p>
				<ul class="mt-2 divide-y divide-line-faint">
					{#each group.rows as row (row.key)}
						{@const f = (row.after ?? row.before)!}
						<li class="flex flex-wrap items-baseline gap-x-3 gap-y-1 py-2 text-[13px]">
							{@render endpoint(f.httpMethod, f.path)}
							<span class="text-slate-400">{f.scenarioType ? faultLabels[f.scenarioType] : PLACEHOLDER}</span>
							<span class="text-[11px] whitespace-nowrap">
								{@render outcome(row, 'before')} <span class="text-slate-500" aria-label="to">→</span> {@render outcome(row, 'after')}
							</span>
							<span class="w-full text-xs text-slate-400 sm:w-auto sm:flex-1">{f.summary}</span>
						</li>
					{/each}
				</ul>
			</div>
		{:else}
			<p class="px-5 py-8 text-center text-xs text-slate-500">No finding changed outcome, and nothing failed in either run.</p>
		{/each}
	</section>

	<div class="mt-5 grid gap-5 lg:grid-cols-2">
		<section class={card} aria-labelledby="categories-heading">
			<div class="border-b border-line-faint px-5 py-4">
				<h3 id="categories-heading" class="font-semibold">Category scores</h3>
				<p class="mt-1 text-[11px] text-slate-500">Locked weights from scoring version {after.run.scoringVersion}.</p>
			</div>
			{#if categories.length}
				<div class="overflow-x-auto px-5 py-3">
					<table class="w-full min-w-[26rem] text-left text-[13px]">
						<thead class="text-[10px] tracking-wider text-slate-500 uppercase">
							<tr>
								<th scope="col" class={th}>Category</th>
								<th scope="col" class={th}>Weight</th>
								<th scope="col" class="{th} text-right">{runLabel(before.run)}</th>
								<th scope="col" class="{th} text-right">{runLabel(after.run)}</th>
								<th scope="col" class="{th} text-right">Change</th>
							</tr>
						</thead>
						<tbody>
							{#each categories as c (c.key)}
								{@const delta = scoreChange(c.before?.score, c.after?.score)}
								<tr>
									<td class="py-2 pr-4 text-slate-200">{scoringCategoryLabels[(c.after ?? c.before)!.category]}</td>
									<td class="py-2 pr-4 text-slate-500">{(c.after ?? c.before)!.weight}%</td>
									<td class="py-2 text-right">{score(c.before?.score)}</td>
									<td class="py-2 text-right font-semibold">{score(c.after?.score)}</td>
									<td class="py-2 text-right {delta == null ? 'text-slate-500' : changeTone(delta)}">{signed(delta)}</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			{:else}
				<p class="px-5 py-8 text-center text-xs text-slate-500">Neither run has a stored category breakdown.</p>
			{/if}
		</section>

		<section class={card} aria-labelledby="endpoints-heading">
			<div class="border-b border-line-faint px-5 py-4">
				<h3 id="endpoints-heading" class="font-semibold">Endpoint scores</h3>
				<p class="mt-1 text-[11px] text-slate-500">Biggest drop first.</p>
			</div>
			{#if endpoints.length}
				<div class="overflow-x-auto px-5 py-3">
					<table class="w-full min-w-[26rem] text-left text-[13px]">
						<thead class="text-[10px] tracking-wider text-slate-500 uppercase">
							<tr>
								<th scope="col" class={th}>Endpoint</th>
								<th scope="col" class="{th} text-right">{runLabel(before.run)}</th>
								<th scope="col" class="{th} text-right">{runLabel(after.run)}</th>
								<th scope="col" class="{th} text-right">Change</th>
							</tr>
						</thead>
						<tbody>
							{#each endpoints as e (e.key)}
								{@const delta = scoreChange(e.before?.score, e.after?.score)}
								{@const row = (e.after ?? e.before)!}
								<tr>
									<td class="py-2 pr-4">{@render endpoint(row.httpMethod, row.path)}</td>
									<td class="py-2 text-right">{score(e.before?.score)}</td>
									<td class="py-2 text-right font-semibold">{score(e.after?.score)}</td>
									<td class="py-2 text-right {delta == null ? 'text-slate-500' : changeTone(delta)}">{signed(delta)}</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			{:else}
				<p class="px-5 py-8 text-center text-xs text-slate-500">Neither run has stored endpoint scores.</p>
			{/if}
		</section>
	</div>
{/if}
