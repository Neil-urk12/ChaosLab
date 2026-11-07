<script lang="ts">
	import ScoreRing from '$lib/components/ScoreRing.svelte';
	import type { TestRunStatus } from '$lib/api';
	import { duration, relative, score, scoreLabel, trendPaths } from '$lib/format';

	let { data } = $props();
	const overview = $derived(data.overview);

	const now = new Date();
	const hour = now.getHours();
	const greeting = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
	const firstName = $derived(data.me.displayName.split(/\s+/)[0]);

	const runChange = $derived(
		overview.runsPrevious7Days
			? Math.round(((overview.runsLast7Days - overview.runsPrevious7Days) / overview.runsPrevious7Days) * 1000) / 10
			: null
	);
	const scoreChange = $derived(
		overview.averageScore != null && overview.previousAverageScore != null
			? Math.round(overview.averageScore - overview.previousAverageScore)
			: null
	);
	const label = $derived(overview.averageScore == null ? null : scoreLabel(overview.averageScore));
	const summaries = {
		Good: 'Your services recover well from common failures.',
		Fair: 'Your services handle some failures, but several resilience patterns need work.',
		Poor: 'Your services struggle to recover from common failures. Start with the findings below.'
	};

	const chart = { width: 800, height: 120 };
	const trend = $derived(trendPaths(overview.scoreTrend.map((p) => p.score), chart.width, chart.height));
	const trendDates = $derived.by(() => {
		const points = overview.scoreTrend;
		if (!points.length) return [];
		const day = (iso: string) => new Date(iso).toLocaleDateString(undefined, { month: 'short', day: '2-digit' });
		return [...new Set([points[0], points[Math.floor((points.length - 1) / 2)], points.at(-1)!].map((p) => day(p.completedAt)))];
	});

	const signed = (value: number) => `${value >= 0 ? '+' : ''}${value}`;
	const stats = $derived([
		{ label: 'Projects', value: String(overview.projectCount), note: 'In your workspace', tone: 'text-slate-500' },
		{
			label: 'APIs monitored',
			value: String(overview.endpointCount),
			note: `Across ${overview.projectCount} project${overview.projectCount === 1 ? '' : 's'}`,
			tone: 'text-slate-500'
		},
		{
			label: 'Tests · last 7 days',
			value: String(overview.runsLast7Days),
			note: runChange == null ? 'No runs the week before' : `${runChange >= 0 ? '↗' : '↘'} ${Math.abs(runChange)}% vs prior week`,
			tone: runChange == null ? 'text-slate-500' : runChange >= 0 ? 'text-emerald-400' : 'text-rose-400'
		},
		{
			label: 'Avg. resilience score',
			value: score(overview.averageScore),
			suffix: overview.averageScore == null ? undefined : '/100',
			note: scoreChange == null ? 'Last 30 days' : `${signed(scoreChange)} pts vs prior 30 days`,
			tone: scoreChange == null ? 'text-slate-500' : scoreChange >= 0 ? 'text-emerald-400' : 'text-rose-400'
		},
		{
			label: 'Open findings',
			value: String(overview.failedFindingCount),
			valueTone: overview.failedFindingCount ? 'text-rose-400' : undefined,
			note: 'From each project’s latest run',
			tone: 'text-slate-500'
		}
	]);

	const statusStyles: Record<TestRunStatus, [string, string]> = {
		Queued: ['Queued', 'border-slate-500/30 bg-slate-500/10 text-slate-300'],
		Running: ['Running', 'border-sky-500/30 bg-sky-500/10 text-sky-300'],
		Completed: ['Completed', 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300'],
		PartiallyCompleted: ['Partial', 'border-amber-500/30 bg-amber-500/10 text-amber-300'],
		Failed: ['Failed', 'border-rose-500/30 bg-rose-500/10 text-rose-300'],
		Cancelled: ['Cancelled', 'border-slate-500/30 bg-slate-500/10 text-slate-400']
	};
</script>

<svelte:head><title>Overview · Chaos Lab</title></svelte:head>

{#snippet panelHeader(title: string, subtitle: string, icon: string, link?: { href: string; label: string })}
	<div class="flex items-start justify-between gap-4">
		<div>
			<h2 class="flex items-center gap-2 text-[15px] font-semibold">
				<svg viewBox="0 0 24 24" class="size-4 text-accent-ink" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{@html icon}</svg>
				{title}
			</h2>
			<p class="mt-1 text-xs text-slate-500">{subtitle}</p>
		</div>
		{#if link}
			<a href={link.href} class="shrink-0 text-xs text-accent-ink hover:text-accent-ink-strong">{link.label}</a>
		{/if}
	</div>
{/snippet}

{#snippet empty(message: string)}
	<p class="grid min-h-32 flex-1 place-items-center rounded-md border border-dashed border-line px-4 text-center text-xs text-slate-500">
		{message}
	</p>
{/snippet}

<div class="flex flex-wrap items-end justify-between gap-6">
	<div>
		<p class="text-[11px] font-semibold tracking-wide text-slate-400 uppercase">
			{now.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' })}
		</p>
		<h1 class="mt-2 text-3xl font-bold tracking-tight">{greeting}, {firstName}</h1>
		<p class="mt-2 text-sm text-slate-400">
			{#if overview.failedFindingCount}
				{overview.failedFindingCount} finding{overview.failedFindingCount === 1 ? '' : 's'} need attention.
			{:else}
				Here's what's happening across your APIs.
			{/if}
		</p>
	</div>
	<div class="flex gap-3">
		<a href="/runs" class="flex h-9 items-center gap-2 rounded-md border border-line-strong px-4 text-[13px] hover:bg-field">
			<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" aria-hidden="true"><path d="M7 5v14l11-7z" /></svg>
			Run test
		</a>
		<a href="/projects" class="flex h-9 items-center gap-2 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover">
			<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
			Create project
		</a>
	</div>
</div>

<dl class="mt-8 grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-line bg-line lg:grid-cols-5">
	{#each stats as stat (stat.label)}
		<div class="bg-canvas p-5 first:col-span-2 lg:first:col-span-1">
			<dt class="text-xs text-slate-400">{stat.label}</dt>
			<dd class="mt-3 text-2xl font-semibold {stat.valueTone ?? ''}">
				{stat.value}{#if stat.suffix}<span class="ml-1 text-xs font-normal text-slate-500">{stat.suffix}</span>{/if}
			</dd>
			<dd class="mt-2 text-[11px] {stat.tone}">{stat.note}</dd>
		</div>
	{/each}
</dl>

<div class="mt-6 grid gap-6 lg:grid-cols-[3fr_2fr]">
	<section class="flex flex-col gap-6 rounded-lg border border-line p-6">
		<div class="flex items-start justify-between gap-4">
			{@render panelHeader(
				'Resilience overview',
				overview.scoredRunsLast30Days
					? `Based on ${overview.scoredRunsLast30Days} test run${overview.scoredRunsLast30Days === 1 ? '' : 's'} over the last 30 days`
					: 'Score trend across your test runs',
				'<circle cx="12" cy="12" r="8"/><path d="M8 14l3-3 2 2 3-4"/>'
			)}
			<button type="button" disabled title="Other ranges are not available yet" class="shrink-0 text-xs text-slate-500">
				Last 30 days ▾
			</button>
		</div>
		{#if overview.averageScore != null && label}
			<div class="flex flex-wrap items-center gap-6">
				<ScoreRing value={overview.averageScore} size={110} />
				<div class="min-w-0 flex-1">
					<p class="flex items-center gap-3">
						<span class="text-lg font-semibold">{label}</span>
						{#if scoreChange != null}
							<span class="rounded-full px-2 py-0.5 text-[10px] font-semibold {scoreChange >= 0 ? 'bg-emerald-500/10 text-emerald-300' : 'bg-rose-500/10 text-rose-300'}">{signed(scoreChange)} pts</span>
						{/if}
					</p>
					<p class="mt-2 text-xs text-slate-400">{summaries[label]}</p>
				</div>
			</div>
			<div class="border-t border-line pt-4">
				<div class="flex justify-between text-[10px] text-slate-500">
					{#each trendDates as day (day)}<span>{day}</span>{/each}
				</div>
				<svg viewBox="0 0 {chart.width} {chart.height}" preserveAspectRatio="none" class="mt-3 h-28 w-full" role="img" aria-label="Resilience score trend">
					<defs>
						<linearGradient id="trend-fill" x1="0" y1="0" x2="0" y2="1">
							<stop offset="0" stop-color="var(--color-accent)" stop-opacity="0.35" />
							<stop offset="1" stop-color="var(--color-accent)" stop-opacity="0" />
						</linearGradient>
					</defs>
					<path d={trend.area} fill="url(#trend-fill)" />
					<path d={trend.line} fill="none" stroke="var(--color-accent)" stroke-width="2" vector-effect="non-scaling-stroke" />
				</svg>
			</div>
		{:else}
			{@render empty('No test runs yet. Your resilience score appears after the first run.')}
		{/if}
	</section>

	<section class="flex flex-col gap-6 rounded-lg border border-danger-line bg-danger-panel p-6">
		{@render panelHeader(
			'Open findings',
			'Issues that can impact reliability or data integrity',
			'<path d="M12 4l9 16H3zM12 10v4M12 17h.01"/>',
			{ href: '/findings', label: 'View all' }
		)}
		{#if overview.findings.length}
			<ul class="-mt-2 divide-y divide-danger-divide">
				{#each overview.findings as finding (finding.id)}
					<li>
						<a href="/projects/{finding.projectId}" class="flex items-center gap-3 py-3 text-[13px] hover:text-strong">
							<span class="size-1.5 shrink-0 rounded-full bg-rose-400" aria-hidden="true"></span>
							<span class="min-w-0 flex-1">
								<span class="block truncate"><span class="font-mono text-xs">{finding.httpMethod} {finding.path}</span> · {finding.summary}</span>
								<span class="block truncate text-[11px] text-slate-500">{finding.projectName}</span>
							</span>
							<span class="text-slate-500" aria-hidden="true">›</span>
						</a>
					</li>
				{/each}
			</ul>
		{:else}
			{@render empty('No findings yet.')}
		{/if}
	</section>
</div>

<section class="mt-6 flex flex-col gap-6 rounded-lg border border-line p-6">
	<div class="flex items-start justify-between gap-4">
		{@render panelHeader('Active & recent test runs', 'Monitor test execution across your environments', '<path d="M13 3L5 14h6l-1 7 8-11h-6z"/>')}
		<div class="flex shrink-0 gap-4 text-xs">
			<button type="button" disabled title="Filtering is not available yet" class="text-slate-500">Filter</button>
			<a href="/runs" class="text-accent-ink hover:text-accent-ink-strong">View all runs</a>
		</div>
	</div>
	{#if overview.recentRuns.length}
		<div class="-mx-6 overflow-x-auto">
			<table class="w-full min-w-[56rem] text-left text-[13px]">
				<thead class="border-y border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
					<tr>
						{#each ['Project', 'Environment', 'Status', 'Progress', 'Scenarios', 'Started by', 'Started at', 'Duration', 'Score'] as heading (heading)}
							<th scope="col" class="px-6 py-3 font-semibold">{heading}</th>
						{/each}
					</tr>
				</thead>
				<tbody class="divide-y divide-line-faint">
					{#each overview.recentRuns as run (run.id)}
						{@const progress = run.totalScenarios ? Math.round((run.completedScenarios / run.totalScenarios) * 100) : 0}
						{@const [statusLabel, statusClass] = statusStyles[run.status]}
						<tr>
							<td class="px-6 py-4"><a href="/projects/{run.projectId}" class="hover:text-strong">{run.projectName}</a></td>
							<td class="px-6 py-4 text-slate-400">{run.environmentName}</td>
							<td class="px-6 py-4"><span class="rounded-full border px-2 py-0.5 text-[11px] {statusClass}">{statusLabel}</span></td>
							<td class="px-6 py-4">
								<span class="flex items-center gap-3">
									<span class="h-1.5 w-16 overflow-hidden rounded-full bg-line">
										<span class="block h-full rounded-full {run.status === 'Failed' ? 'bg-rose-400' : 'bg-accent'}" style:width="{progress}%"></span>
									</span>
									<span class="text-[11px] text-slate-500">{progress}%</span>
								</span>
							</td>
							<td class="px-6 py-4 text-slate-400">{run.completedScenarios} / {run.totalScenarios}</td>
							<td class="px-6 py-4 text-slate-400">{run.requestedBy}</td>
							<td class="px-6 py-4 text-slate-400">{relative(run.startedAt ?? run.createdAt)}</td>
							<td class="px-6 py-4 text-slate-400">{duration(run.startedAt, run.completedAt)}</td>
							<td class="px-6 py-4 font-semibold">{score(run.score)}</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{:else}
		{@render empty('No test runs yet.')}
	{/if}
</section>

<div class="mt-6 grid gap-6 lg:grid-cols-[3fr_2fr]">
	<section class="flex flex-col gap-6 rounded-lg border border-line p-6">
		{@render panelHeader('Recent projects', 'Your most recently created API projects', '<path d="M8 8l-4 4 4 4M16 8l4 4-4 4"/>', { href: '/projects', label: 'View projects' })}
		{#if overview.recentProjects.length}
			<ul class="-mx-6 divide-y divide-line-faint border-t border-line-faint">
				{#each overview.recentProjects as project (project.id)}
					<li class="flex items-center gap-4 px-6 py-4">
						<span class="grid size-9 shrink-0 place-items-center rounded-md bg-accent-tint font-mono text-xs text-accent-ink" aria-hidden="true">&lt;/&gt;</span>
						<div class="min-w-0 flex-1">
							<a href="/projects/{project.id}" class="block truncate text-[13px] hover:text-strong">{project.name}</a>
							<p class="truncate text-[11px] text-slate-500">{project.baseUrl ?? project.description ?? 'No environment configured'}</p>
						</div>
						<div class="shrink-0 text-right">
							<p class="text-[13px] font-semibold">{score(project.latestScore)}<span class="ml-1 text-[11px] font-normal text-slate-500">/100</span></p>
							<p class="text-[11px] text-slate-500">{project.lastRunAt ? `Last scored · ${relative(project.lastRunAt)}` : 'Not tested yet'}</p>
						</div>
						<span class="w-8 shrink-0 text-[11px] text-slate-500" title="Open findings">⚠ {project.failedFindings}</span>
						<button type="button" disabled title="Project actions are not available yet" aria-label="Project actions" class="shrink-0 text-slate-600">⋯</button>
					</li>
				{/each}
			</ul>
		{:else}
			{@render empty('No projects yet. Create one to import an OpenAPI spec.')}
		{/if}
	</section>
	<section class="flex flex-col gap-6 rounded-lg border border-line p-6">
		{@render panelHeader('Recent activity', "What's happening in your workspace", '<circle cx="12" cy="12" r="8"/><path d="M12 8v4l3 2"/>')}
		{@render empty('Activity feed is not available yet.')}
	</section>
</div>
