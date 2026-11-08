<script lang="ts">
	import ScoreRing from '$lib/components/ScoreRing.svelte';
	import type { ResiliencePattern, TestRunRow } from '$lib/api';
	import { dateTime, duration, initials, PLACEHOLDER, score } from '$lib/format';

	let { data } = $props();
	const project = $derived(data.project);
	const latest = $derived(project.latest);
	const change = $derived(
		latest?.previousScore != null ? Math.round(latest.score - latest.previousScore) : null
	);
	const trendLabel = $derived(change == null ? 'First result' : change > 0 ? 'Improving' : change < 0 ? 'Declining' : 'Stable');

	// Availability has no score in the model yet, so it stays a placeholder.
	const breakdown = $derived([
		{ label: 'Availability', value: null },
		{ label: 'Timeout handling', value: latest?.breakdown?.timeout ?? null },
		{ label: 'Retry strategy', value: latest?.breakdown?.retry ?? null },
		{ label: 'Circuit breaker', value: latest?.breakdown?.circuitBreaker ?? null },
		{ label: 'Fallback behavior', value: latest?.breakdown?.fallback ?? null },
		{ label: 'Idempotency', value: latest?.breakdown?.idempotency ?? null }
	]);

	const patternLabels: Record<ResiliencePattern, string> = {
		Retry: 'Retry',
		Timeout: 'Timeout',
		CircuitBreaker: 'Circuit breaker',
		Fallback: 'Fallback',
		Idempotency: 'Idempotency'
	};

	const owner = $derived(project.members.find((m) => m.role === 'Owner'));
	const others = $derived(project.members.filter((m) => m !== owner));
	const firstNames = $derived.by(() => {
		const names = project.members.map((m) => m.displayName.split(/\s+/)[0]);
		return names.length > 3 ? `${names.slice(0, 3).join(', ')} +${names.length - 3}` : names.join(', ');
	});

	function result(run: TestRunRow): [string, string] {
		if (run.status === 'Running' || run.status === 'Queued') return ['In progress', 'bg-sky-500/10 text-sky-300'];
		if (run.status === 'Failed' || run.status === 'Cancelled') return [run.status, 'bg-rose-500/10 text-rose-300'];
		if (run.failedFindings) return ['Issues found', 'bg-amber-500/10 text-amber-300'];
		// Some scenarios never ran, so no failures is not a pass.
		if (run.status === 'PartiallyCompleted') return ['Partial', 'bg-amber-500/10 text-amber-300'];
		return ['Passed', 'bg-emerald-500/10 text-emerald-300'];
	}

</script>

<svelte:head><title>{project.name} · Chaos Lab</title></svelte:head>

{#snippet panelHeader(title: string, subtitle: string, icon: string, action?: string)}
	<div class="flex items-start justify-between gap-4">
		<div class="min-w-0">
			<h2 class="flex items-center gap-2 text-[15px] font-semibold">
				<svg viewBox="0 0 24 24" class="size-4 text-accent-ink" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{@html icon}</svg>
				{title}
			</h2>
			<p class="mt-1 text-xs text-slate-500">{subtitle}</p>
		</div>
		{#if action}
			<button type="button" disabled title="Not available yet" class="shrink-0 text-xs text-slate-500">{action}</button>
		{/if}
	</div>
{/snippet}

{#snippet empty(message: string)}
	<p class="grid min-h-32 flex-1 place-items-center rounded-md border border-dashed border-line px-4 text-center text-xs text-slate-500">
		{message}
	</p>
{/snippet}


<dl class="mt-6 grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-line bg-line lg:grid-cols-5">
	{#each [
		{ label: 'OpenAPI endpoints', value: project.endpointCount, note: 'In the imported specification' },
		{ label: 'Failure scenarios available', value: project.scenarioCount, note: 'Ready for your next test' },
		{ label: 'Tests completed', value: project.completedRunCount, note: 'Across project environments' },
		{ label: 'Open findings', value: project.failedFindingCount, note: 'From the latest run', alert: project.failedFindingCount > 0 },
		{ label: 'Team members', value: project.members.length, note: 'Working on this project' }
	] as stat (stat.label)}
		<div class="bg-canvas p-5 first:col-span-2 lg:first:col-span-1">
			<dt class="text-xs text-slate-400">{stat.label}</dt>
			<dd class="mt-3 text-2xl font-semibold {stat.alert ? 'text-rose-400' : ''}">{stat.value}</dd>
			<dd class="mt-2 text-[11px] {stat.alert ? 'text-rose-400' : 'text-slate-500'}">{stat.alert ? 'Require attention' : stat.note}</dd>
		</div>
	{/each}
</dl>

<div class="mt-6 grid gap-6 lg:grid-cols-[3fr_2fr]">
	<section class="flex flex-col gap-6 rounded-lg border border-line p-6">
		{@render panelHeader(
			'Latest resilience score',
			latest ? `Completed ${dateTime(latest.completedAt)}` : 'No scored test runs yet',
			'<path d="M12 3l7 3v5c0 5-3 8.5-7 10-4-1.5-7-5-7-10V6z"/><path d="M9 12l2 2 4-4"/>',
			'View full report'
		)}
		{#if latest}
			<div class="flex flex-wrap items-center gap-8">
				<ScoreRing value={latest.score} size={130} />
				<div class="min-w-0 flex-1">
					<p class="text-[13px] text-slate-400">Overall resilience</p>
					<p class="mt-1 flex items-center gap-4">
						<span class="text-2xl font-bold">{trendLabel}</span>
						{#if change != null}
							<span class="rounded-md px-2 py-0.5 text-[11px] font-semibold {change >= 0 ? 'bg-emerald-500/10 text-emerald-300' : 'bg-rose-500/10 text-rose-300'}">
								{change >= 0 ? '+' : ''}{change} pts
							</span>
						{/if}
					</p>
					{#if latest.previousScore != null}
						<p class="mt-2 text-[13px] text-slate-400">Previous run: {score(latest.previousScore)}/100</p>
					{/if}
					{#if project.failedFindingCount}
						<p class="mt-1 text-[13px] text-rose-400">
							{project.failedFindingCount} finding{project.failedFindingCount === 1 ? '' : 's'} still need attention.
						</p>
					{/if}
				</div>
				{#if latest.previousScore != null}
					<div class="w-full space-y-3 text-[11px] text-slate-400 sm:w-52">
						{#each [{ label: 'Previous', value: latest.previousScore, color: 'bg-slate-500' }, { label: 'Latest', value: latest.score, color: 'bg-accent' }] as bar (bar.label)}
							<div>
								<p class="flex justify-between"><span>{bar.label}</span><span>{score(bar.value)}</span></p>
								<span class="mt-1 block h-1 rounded-full bg-line"><span class="block h-full rounded-full {bar.color}" style:width="{bar.value}%"></span></span>
							</div>
						{/each}
					</div>
				{/if}
			</div>
			<div class="border-t border-line pt-5">
				<p class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Resilience breakdown</p>
				<dl class="mt-4 grid gap-x-8 gap-y-5 sm:grid-cols-2">
					{#each breakdown as item (item.label)}
						<div>
							<div class="flex justify-between text-[13px]">
								<dt class="text-slate-300">{item.label}</dt>
								<dd class="font-semibold {item.value != null && item.value < 70 ? 'text-amber-300' : ''}">{score(item.value)}</dd>
							</div>
							<span class="mt-2 block h-1 rounded-full bg-line">
								<span class="block h-full rounded-full {item.value != null && item.value < 70 ? 'bg-amber-400' : 'bg-accent'}" style:width="{item.value ?? 0}%"></span>
							</span>
						</div>
					{/each}
				</dl>
			</div>
		{:else}
			{@render empty('Run a test to see this project’s resilience score.')}
		{/if}
	</section>

	<section class="flex flex-col gap-6 rounded-lg border border-line p-6">
		{@render panelHeader('Recent findings', 'Prioritize issues from the latest run.', '<path d="M12 4l9 16H3zM12 10v4M12 17h.01"/>', 'View all')}
		{#if project.findings.length}
			<ul class="-mt-2 divide-y divide-line-faint">
				{#each project.findings as finding (finding.id)}
					<li class="py-4">
						<p class="flex flex-wrap items-center gap-3 text-[13px]">
							<span class="rounded bg-rose-500/10 px-2 py-0.5 text-[11px] text-rose-300">{patternLabels[finding.pattern]}</span>
							<span class="text-[11px] font-semibold text-sky-300">{finding.httpMethod}</span>
							<span class="font-mono text-xs">{finding.path}</span>
						</p>
						<p class="mt-2 text-[13px] text-slate-300">{finding.summary}</p>
						<p class="mt-1 text-right text-[11px] text-slate-500" title="Finding status is not tracked yet">Status {PLACEHOLDER}</p>
					</li>
				{/each}
			</ul>
		{:else}
			{@render empty('No findings in the latest run.')}
		{/if}
	</section>
</div>

<section class="mt-6 flex flex-col gap-6 rounded-lg border border-line p-6">
	{@render panelHeader('Recent test runs', 'Execution history for this API project', '<path d="M7 5v14l11-7z"/>', 'View all runs')}
	{#if project.recentRuns.length}
		<div class="-mx-6 overflow-x-auto">
			<table class="w-full min-w-[52rem] text-left text-[13px]">
				<thead class="border-y border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
					<tr>
						{#each ['Run ID', 'Environment', 'Started by', 'Date', 'Duration', 'Scenarios', 'Result', 'Score'] as heading (heading)}
							<th scope="col" class="px-6 py-3 font-semibold last:text-right">{heading}</th>
						{/each}
					</tr>
				</thead>
				<tbody class="divide-y divide-line-faint">
					{#each project.recentRuns as run (run.id)}
						{@const [resultLabel, resultClass] = result(run)}
						<tr>
							<td class="px-6 py-4 text-slate-500" title="Run numbers are not assigned yet">{PLACEHOLDER}</td>
							<td class="px-6 py-4 text-slate-400">{run.environmentName}</td>
							<td class="px-6 py-4 text-slate-400">{run.requestedBy}</td>
							<td class="px-6 py-4 text-slate-400">{dateTime(run.startedAt ?? run.createdAt)}</td>
							<td class="px-6 py-4 text-slate-400">{duration(run.startedAt, run.completedAt)}</td>
							<td class="px-6 py-4 text-slate-400">{run.completedScenarios} / {run.totalScenarios}</td>
							<td class="px-6 py-4"><span class="rounded px-2 py-1 text-[11px] {resultClass}">{resultLabel}</span></td>
							<td class="px-6 py-4 text-right font-semibold">{score(run.score)}<span class="ml-1 text-[10px] font-normal text-slate-500">/100</span></td>
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
		{@render panelHeader(
			'API specification',
			project.spec ? 'The active specification used for scenario generation.' : 'No specification imported yet.',
			'<path d="M7 3h7l5 5v13H7z"/><path d="M14 3v5h5M10 13h6M10 17h6"/>'
		)}
		{#if project.spec}
			<dl class="grid grid-cols-2 gap-6 sm:grid-cols-3">
				<div>
					<dt class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">OpenAPI version</dt>
					<dd class="mt-2 text-lg">{project.spec.openApiVersion}</dd>
				</div>
				<div>
					<dt class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Operations</dt>
					<dd class="mt-2 text-lg">{project.endpointCount}</dd>
				</div>
				<div>
					<dt class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Last imported</dt>
					<dd class="mt-2 text-lg">{dateTime(project.spec.importedAt)}</dd>
				</div>
			</dl>
			<div class="flex items-center justify-between gap-4 border-t border-line pt-5 text-[13px]">
				<span class="text-slate-500" title="Specification validation results are not stored yet">Validation {PLACEHOLDER}</span>
				<a href="/projects/{project.id}/spec" class="rounded-md border border-line-strong px-4 py-2 text-xs text-slate-300 hover:bg-line-faint">View specification →</a>
			</div>
		{:else}
			{@render empty('Import an OpenAPI 3.x specification to generate failure scenarios.')}
		{/if}
	</section>

	<section class="flex flex-col gap-6 rounded-lg border border-line p-6">
		{@render panelHeader(
			'Project team',
			`${project.members.length} member${project.members.length === 1 ? ' has' : 's have'} access to ${project.name}.`,
			'<circle cx="9" cy="8" r="3.5"/><path d="M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6M16 4.5a3.5 3.5 0 010 7M21 20c0-3-1.5-5-4-5.7"/>',
			'Manage team'
		)}
		<div class="flex items-center gap-4">
			<div class="flex -space-x-2">
				{#each project.members.slice(0, 4) as member (member.userId)}
					<span class="grid size-9 place-items-center rounded-full border-2 border-canvas bg-accent-tint text-[11px] font-semibold text-accent-ink" title={member.displayName}>{initials(member.displayName)}</span>
				{/each}
			</div>
			<div class="min-w-0">
				<p class="truncate text-[13px]">{firstNames}</p>
				<p class="text-[11px] text-slate-500">Collaborating on API resilience</p>
			</div>
		</div>
		{#if owner}
			<div class="border-t border-line pt-5">
				<p class="flex items-center justify-between text-[13px]">
					{owner.displayName}
					<span class="rounded bg-accent-tint px-2 py-0.5 text-[11px] text-accent-ink-strong">Owner</span>
				</p>
				{#if others.length}
					<p class="mt-2 text-[11px] text-slate-500">{others.map((m) => m.displayName).join(' · ')}</p>
				{/if}
			</div>
		{/if}
	</section>
</div>

<footer class="mt-8 flex justify-between text-[11px] text-slate-500">
	<span>{project.name}</span>
	<span>{project.defaultEnvironment?.name ?? ''}</span>
</footer>
