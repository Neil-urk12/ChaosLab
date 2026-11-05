<script lang="ts">
	let { data } = $props();

	const now = new Date();
	const hour = now.getHours();
	const greeting = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
	const firstName = $derived(data.me.displayName.split(/\s+/)[0]);
	const recentProjects = $derived(data.projects.slice(0, 3));

	// Only the project count has a backend source today; the rest wait on runs and findings.
	const stats = $derived([
		{ label: 'Projects', value: String(data.projects.length) },
		{ label: 'APIs monitored', value: '—' },
		{ label: 'Tests · last 7 days', value: '—' },
		{ label: 'Avg. resilience score', value: '—' },
		{ label: 'Critical findings', value: '—' }
	]);
</script>

<svelte:head><title>Overview · Chaos Lab</title></svelte:head>

{#snippet panelHeader(title: string, subtitle: string, link?: { href: string; label: string })}
	<div class="flex items-start justify-between gap-4">
		<div>
			<h2 class="text-[15px] font-semibold">{title}</h2>
			<p class="mt-1 text-xs text-slate-500">{subtitle}</p>
		</div>
		{#if link}
			<a href={link.href} class="shrink-0 text-xs text-[#a5a6fd] hover:text-[#c3c4fe]">{link.label}</a>
		{/if}
	</div>
{/snippet}

{#snippet empty(message: string)}
	<p class="grid min-h-32 flex-1 place-items-center rounded-md border border-dashed border-[#1e2433] px-4 text-center text-xs text-slate-500">
		{message}
	</p>
{/snippet}

<div class="flex flex-wrap items-end justify-between gap-6">
	<div>
		<p class="text-[11px] font-semibold tracking-wide text-slate-400 uppercase">
			{now.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' })}
		</p>
		<h1 class="mt-2 text-3xl font-bold tracking-tight">{greeting}, {firstName}</h1>
		<p class="mt-2 text-sm text-slate-400">Here's what's happening across your APIs.</p>
	</div>
	<div class="flex gap-3">
		<a href="/runs" class="flex h-9 items-center gap-2 rounded-md border border-[#2a3142] px-4 text-[13px] hover:bg-[#0d111a]">
			<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" aria-hidden="true"><path d="M7 5v14l11-7z" /></svg>
			Run test
		</a>
		<a href="/projects" class="flex h-9 items-center gap-2 rounded-md bg-[#7b7dfb] px-4 text-[13px] font-semibold text-[#0b0d1a] hover:bg-[#8d8ffc]">
			<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
			Create project
		</a>
	</div>
</div>

<dl class="mt-8 grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-[#1e2433] bg-[#1e2433] lg:grid-cols-5">
	{#each stats as stat (stat.label)}
		<div class="bg-[#07090e] p-5 first:col-span-2 lg:first:col-span-1">
			<dt class="text-xs text-slate-400">{stat.label}</dt>
			<dd class="mt-3 text-2xl font-semibold">{stat.value}</dd>
		</div>
	{/each}
</dl>

<div class="mt-6 grid gap-6 lg:grid-cols-[3fr_2fr]">
	<section class="flex flex-col gap-6 rounded-lg border border-[#1e2433] p-6">
		{@render panelHeader('Resilience overview', 'Score trend across your test runs')}
		{@render empty('No test runs yet. Your resilience score appears after the first run.')}
	</section>
	<section class="flex flex-col gap-6 rounded-lg border border-[#3a1d27] bg-[#0f0a0e] p-6">
		{@render panelHeader('Critical findings', 'Issues that can impact reliability or data integrity', { href: '/findings', label: 'View all' })}
		{@render empty('No findings yet.')}
	</section>
</div>

<section class="mt-6 flex flex-col gap-6 rounded-lg border border-[#1e2433] p-6">
	{@render panelHeader('Active & recent test runs', 'Monitor test execution across your environments', { href: '/runs', label: 'View all runs' })}
	{@render empty('No test runs yet.')}
</section>

<div class="mt-6 grid gap-6 lg:grid-cols-[3fr_2fr]">
	<section class="flex flex-col gap-6 rounded-lg border border-[#1e2433] p-6">
		{@render panelHeader('Recent projects', 'Your most recently created API projects', { href: '/projects', label: 'View projects' })}
		{#if recentProjects.length}
			<ul class="-mx-6 divide-y divide-[#1a1f2b] border-t border-[#1a1f2b]">
				{#each recentProjects as project (project.id)}
					<li class="flex items-center gap-4 px-6 py-4">
						<span class="grid size-9 shrink-0 place-items-center rounded-md bg-[#1b1d3a] font-mono text-xs text-[#a5a6fd]" aria-hidden="true">&lt;/&gt;</span>
						<div class="min-w-0 flex-1">
							<p class="truncate text-[13px]">{project.name}</p>
							{#if project.description}
								<p class="truncate text-[11px] text-slate-500">{project.description}</p>
							{/if}
						</div>
						<p class="shrink-0 text-[11px] text-slate-500">
							Created {new Date(project.createdAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
						</p>
					</li>
				{/each}
			</ul>
		{:else}
			{@render empty('No projects yet. Create one to import an OpenAPI spec.')}
		{/if}
	</section>
	<section class="flex flex-col gap-6 rounded-lg border border-[#1e2433] p-6">
		{@render panelHeader('Recent activity', "What's happening in your workspace")}
		{@render empty('No activity yet.')}
	</section>
</div>
