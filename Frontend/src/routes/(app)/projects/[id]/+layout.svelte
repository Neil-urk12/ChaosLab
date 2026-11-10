<script lang="ts">
	import { page } from '$app/state';
	import { score } from '$lib/format';

	let { data, children } = $props();
	const project = $derived(data.project);
	const latest = $derived(project.latest);

	const base = $derived(`/projects/${project.id}`);
	// Tabs without an href are not built yet.
	const tabs = $derived([
		{ label: 'Overview', href: base },
		{ label: 'API specification', href: `${base}/spec` },
		{ label: 'Scenarios', href: `${base}/scenarios` },
		{ label: 'Test runs', href: `${base}/runs` },
		{ label: 'Findings', href: `${base}/findings`, badge: project.failedFindingCount },
		{ label: 'Team', href: `${base}/team` },
		{ label: 'Settings', href: `${base}/settings` }
	]);

	let copied = $state(false);
	async function copyBaseUrl() {
		if (!project.defaultEnvironment) return;
		try {
			await navigator.clipboard.writeText(project.defaultEnvironment.baseUrl);
			copied = true;
			setTimeout(() => (copied = false), 1500);
		} catch {
			// Clipboard can be blocked (insecure origin or permissions); the URL stays selectable.
		}
	}
</script>

<header class="flex flex-wrap items-start justify-between gap-6">
	<div class="flex min-w-0 gap-4">
		<span class="grid size-9 shrink-0 place-items-center rounded-md bg-accent-tint font-mono text-xs text-accent-ink" aria-hidden="true">&lt;/&gt;</span>
		<div class="min-w-0">
			<p class="text-[10px] font-semibold tracking-wider text-slate-400 uppercase">Project</p>
			<div class="mt-1 flex flex-wrap items-center gap-4">
				<h1 class="text-3xl font-bold tracking-tight">{project.name}</h1>
				{#if project.defaultEnvironment}
					<span class="rounded-md bg-accent-tint px-3 py-1 text-[11px] text-accent-ink-strong">{project.defaultEnvironment.name}</span>
				{/if}
			</div>
			<p class="mt-3 flex items-center gap-3 text-[13px]">
				<span class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">API base URL</span>
				{#if project.defaultEnvironment}
					<span class="truncate font-mono text-slate-200">{project.defaultEnvironment.baseUrl}</span>
					<button type="button" onclick={copyBaseUrl} class="text-xs text-slate-400 hover:text-slate-100" aria-label="Copy API base URL">
						{copied ? 'Copied' : 'Copy'}
					</button>
				{:else}
					<span class="text-slate-500">No environment configured</span>
				{/if}
			</p>
		</div>
	</div>
	<div class="flex items-center gap-6">
		<div class="text-right">
			<p class="text-[10px] font-semibold tracking-wider text-slate-400 uppercase">Latest resilience score</p>
			<p class="mt-1 text-3xl font-bold text-accent-ink">{score(latest?.score)}<span class="ml-1 text-sm font-normal text-slate-500">/100</span></p>
		</div>
		<button type="button" disabled title="Running tests is not available yet" class="flex h-10 items-center gap-2 rounded-md bg-accent/50 px-6 text-[13px] font-semibold text-on-accent">
			<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" aria-hidden="true"><path d="M7 5v14l11-7z" /></svg>
			Run Test
		</button>
		<button type="button" disabled title="Project actions are not available yet" aria-label="Project actions" class="grid size-10 place-items-center rounded-md border border-line-strong text-slate-600">⋮</button>
	</div>
</header>

<nav aria-label="Project sections" class="mt-8 flex gap-8 overflow-x-auto border-b border-line text-[13px]">
	{#each tabs as tab (tab.label)}
		{#if tab.href}
			{@const current = page.url.pathname === tab.href}
			<a
				href={tab.href}
				aria-current={current ? 'page' : undefined}
				class="flex shrink-0 items-center gap-2 border-b-2 pb-3 {current ? 'border-accent font-semibold text-accent-ink-strong' : 'border-transparent text-slate-400 hover:text-slate-100'}"
			>
				{tab.label}
				{#if tab.badge}
					<span class="rounded bg-rose-500/15 px-1.5 text-[10px] font-normal text-rose-300">{tab.badge}</span>
				{/if}
			</a>
		{:else}
			<span class="shrink-0 pb-3 text-slate-500" title="Not available yet">{tab.label}</span>
		{/if}
	{/each}
</nav>

<!-- SvelteKit reuses page components across projects; rebuild so page state (selections, inputs) doesn't carry over. -->
{#key project.id}
	{@render children()}
{/key}
